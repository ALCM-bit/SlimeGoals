using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;

namespace SlimeGoals.Core.Services;

/// <summary>Próximo disparo de uma meta que ainda cai hoje.</summary>
public sealed record UpcomingReminder(Goal Goal, DateTimeOffset At);

/// <summary>
/// Orquestra os lembretes (spec §9): levanta o estado de cada meta, decide com o <see cref="ReminderPlanner"/>,
/// grava os disparos no log e os mostra pelo <see cref="INotificationService"/>.
/// </summary>
public sealed class ReminderService(IGoalRepository goals, ICheckinRepository checkins, ITimerRepository timers,
    IReminderLogRepository log, INotificationService notifications, IReminderFormatter formatter, TimeProvider time,
    ReminderPause? pause = null, AppSettings? settings = null) : IRemoteDeletionGate
{
    // Disparo e exclusão não se cruzam: um disparo em andamento já gravou o log, mas ainda não mostrou a notificação.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Retira pendentes que não valem mais, dispara os devidos (Next ≤ agora) e devolve o próximo disparo.</summary>
    public async Task<DateTimeOffset?> SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SyncLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Apaga a meta e retira da central as notificações pendentes dela. A ordem importa: só o log sabe quais são,
    /// então elas são lidas antes de apagar; e só saem da central depois que o banco aceitou, senão uma falha
    /// deixaria a meta viva sem o lembrete. Depois disso o log não existe mais, e o <see cref="SyncAsync"/> não as acharia.
    /// </summary>
    public async Task DeleteGoalAsync(Guid goalId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = (await log.ListPendingAsync(ct).ConfigureAwait(false)).Where(e => e.GoalId == goalId).ToList();
            await goals.DeleteAsync(goalId, ct).ConfigureAwait(false);
            foreach (var entry in pending)
                await WithdrawAsync(entry.Id, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Aplica exclusões vindas do servidor sob o mesmo portão, na mesma ordem de <see cref="DeleteGoalAsync"/>:
    /// os pendentes são lidos antes (o log some com a meta) e só saem da central depois que <paramref name="apply"/>
    /// terminou. Se ele falhar, nada é retirado.
    /// </summary>
    public async Task RunAsync(Func<CancellationToken, Task<IReadOnlyList<Guid>>> apply, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = await log.ListPendingAsync(ct).ConfigureAwait(false);
            var deleted = (await apply(ct).ConfigureAwait(false)).ToHashSet();
            foreach (var entry in pending.Where(e => deleted.Contains(e.GoalId)))
                await WithdrawAsync(entry.Id, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DateTimeOffset?> SyncLockedAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var snapshot = await LoadAsync(now, ct);

        // Pausado: nada vale, nada dispara.
        if (snapshot.Paused)
        {
            foreach (var entry in await log.ListPendingAsync(ct))
                await IgnoreAsync(entry, now, ct);
            return null;
        }

        // 1. Pendentes que não valem mais.
        var pending = await log.ListPendingAsync(ct);
        var newestFire = snapshot.Fires.Concat(pending).GroupBy(e => e.GoalId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.FiredAt));
        var stillPending = new List<ReminderLogEntry>();
        foreach (var entry in pending)
        {
            var goal = snapshot.Goals.FirstOrDefault(g => g.Id == entry.GoalId)
                       ?? await goals.GetIfReadableAsync(entry.GoalId, ct);
            // Disparado antes de hoje (dia local): não vale mais depois da meia-noite.
            var stale = goal is null || goal.ArchivedAt is not null
                        || entry.FiredAt < snapshot.TodayStart
                        || newestFire[entry.GoalId] > entry.FiredAt
                        || NextFor(goal, snapshot, now) is not { } next || next >= snapshot.TomorrowStart;
            if (stale)
                await IgnoreAsync(entry, now, ct);
            else
                stillPending.Add(entry);
        }

        // 2-3. Dispara os devidos; falha ao mostrar não impede o resto.
        foreach (var goal in snapshot.Goals)
        {
            if (NextFor(goal, snapshot, now) is not { } due || due > now)
                continue;

            foreach (var previous in stillPending.Where(e => e.GoalId == goal.Id))
                await IgnoreAsync(previous, now, ct);

            var entry = new ReminderLogEntry(Guid.CreateVersion7(now), goal.Id, now);
            await log.AddAsync(entry, ct);
            snapshot.Fires.Add(entry);

            try
            {
                var text = formatter.Format(goal, snapshot.PeriodTotal(goal));
                await notifications.ShowAsync(new Reminder(entry.Id, goal.Id, text), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Trace.TraceError($"Falha ao mostrar o lembrete da meta {goal.Id}: {e}");
            }
        }

        // 4. Próximo disparo.
        return snapshot.Goals.Select(g => NextFor(g, snapshot, now)).OfType<DateTimeOffset>()
            .Select(d => (DateTimeOffset?)d).Min();
    }

    /// <summary>Próximo disparo de cada meta que ainda cai hoje (dia local), ordenado por horário.</summary>
    public async Task<IReadOnlyList<UpcomingReminder>> UpcomingTodayAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var snapshot = await LoadAsync(now, ct);
        if (snapshot.Paused)
            return [];
        return snapshot.Goals
            .Select(g => (Goal: g, At: NextFor(g, snapshot, now)))
            .Where(x => x.At is { } at && at < snapshot.TomorrowStart)
            .Select(x => new UpcomingReminder(x.Goal, x.At!.Value))
            .OrderBy(u => u.At)
            .ToList();
    }

    /// <summary>Marca todos os pendentes como Ignore e os retira (ao abrir e ao encerrar o app).</summary>
    public async Task ExpirePendingAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        foreach (var entry in await log.ListPendingAsync(ct))
            await IgnoreAsync(entry, now, ct);
    }

    private async Task IgnoreAsync(ReminderLogEntry entry, DateTimeOffset now, CancellationToken ct)
    {
        await log.RespondAsync(entry.Id, ReminderResponse.Ignore, now, ct);
        await WithdrawAsync(entry.Id, ct);
    }

    private async Task WithdrawAsync(Guid logId, CancellationToken ct)
    {
        try
        {
            await notifications.WithdrawAsync(logId, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao retirar o lembrete {logId}: {e}");
        }
    }

    private DateTimeOffset? NextFor(Goal goal, Snapshot s, DateTimeOffset now) =>
        ReminderPlanner.Next(goal, s.StateOf(goal), s.SnoozeMinutes, now, s.Zone);

    private async Task<Snapshot> LoadAsync(DateTimeOffset now, CancellationToken ct)
    {
        var zone = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var monday = WeekMath.StartOfWeek(today);
        var (from, to) = new DateRange(monday, monday.AddDays(7)).ToUtc(zone);
        var (todayStart, tomorrowStart) = new DateRange(today, today.AddDays(1)).ToUtc(zone);

        var active = await goals.ListAsync(includeArchived: false, ct);
        var weekCheckins = await checkins.ListAsync(from, to, ct);
        var fires = (await log.ListAsync(from, to, ct)).ToList();
        var timer = await timers.GetAsync(ct);
        var pauseState = pause is null ? new PauseState(false, null) : await pause.GetAsync(ct);
        var snooze = settings is null ? AppSettings.DefaultSnoozeMinutes : await settings.GetSnoozeMinutesAsync(ct);

        var byGoalAndDay = weekCheckins
            .GroupBy(c => (c.GoalId, Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.DoneAt, zone).DateTime)))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        return new Snapshot(active, zone, today, todayStart, tomorrowStart, byGoalAndDay, weekCheckins, fires, timer,
            pauseState, snooze);
    }

    /// <summary>Fatos da semana local levantados de uma vez; <see cref="Fires"/> cresce conforme se dispara.</summary>
    private sealed class Snapshot(IReadOnlyList<Goal> goals, TimeZoneInfo zone, DateOnly today,
        DateTimeOffset todayStart, DateTimeOffset tomorrowStart,
        Dictionary<(Guid GoalId, DateOnly Day), int> sums, IReadOnlyList<Checkin> weekCheckins,
        List<ReminderLogEntry> fires, ActiveTimer? timer, PauseState pauseState, int snoozeMinutes)
    {
        public bool Paused => pauseState.IsPaused;
        public int SnoozeMinutes => snoozeMinutes;
        public IReadOnlyList<Goal> Goals { get; } = goals;
        public TimeZoneInfo Zone { get; } = zone;
        public DateTimeOffset TodayStart => todayStart;
        public DateTimeOffset TomorrowStart => tomorrowStart;
        public List<ReminderLogEntry> Fires { get; } = fires;

        public int PeriodTotal(Goal goal) =>
            GoalPeriod.EvaluationPeriod(goal, today) is { } range
                ? GoalTotals.Effective(goal, sums.Where(kv => kv.Key.GoalId == goal.Id && range.Contains(kv.Key.Day)).Sum(kv => kv.Value))
                : 0;

        public ReminderState StateOf(Goal goal)
        {
            var todayFires = Fires.Where(f => f.GoalId == goal.Id && f.FiredAt >= todayStart && f.FiredAt < tomorrowStart)
                .OrderBy(f => f.FiredAt).ToList();
            var last = todayFires.LastOrDefault();
            var lastCheckin = weekCheckins
                .Where(c => c.GoalId == goal.Id && c.DoneAt >= todayStart && c.DoneAt < tomorrowStart)
                .Select(c => (DateTimeOffset?)c.DoneAt).Max();
            // Parar conta como atividade: sem isso o lembrete sairia enquanto a pessoa confere o tempo.
            var stoppedToday = timer?.GoalId == goal.Id && timer.StoppedAt is { } stopped
                               && stopped >= todayStart && stopped < tomorrowStart ? timer.StoppedAt : null;
            var snoozedAt = last is { Response: ReminderResponse.Snooze } ? last.RespondedAt : null;
            var intervalStart = new[] { goal.CreatedAt, pauseState.ResumedAt }
                .Where(t => t is { } v && v >= todayStart && v < tomorrowStart)
                .Max();
            return new ReminderState(PeriodTotal(goal) >= goal.Target, timer is { StoppedAt: null } && timer.GoalId == goal.Id,
                last?.FiredAt, new[] { lastCheckin, stoppedToday }.Max(), snoozedAt, intervalStart);
        }
    }
}
