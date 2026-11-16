using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Sessão de Pomodoro (spec Pomodoro §4): uma por vez, gravada no banco, e nunca junto com um cronômetro. Cada corte
/// (⇄, fim de um foco, ⏭, ✕) vira um trecho; com meta, o trecho entra nela na hora como registro comum, e sem meta fica
/// guardado até a pessoa dar um destino ao tempo no fim. Toda gravação passa pela revisão da sessão: se outro processo
/// mudou a sessão entre a leitura e a gravação, nada é gravado.
/// </summary>
public sealed class FocusService(IGoalRepository goals, IFocusSessionRepository sessions, ITimerRepository timers,
    CheckinService checkins, AppSettings settings, TimeProvider time, DataChangeNotifier? changes = null)
{
    public Task<FocusSession?> GetAsync(CancellationToken ct = default) => sessions.GetAsync(ct);

    public Task<IReadOnlyList<FocusChunk>> ChunksAsync(CancellationToken ct = default) => sessions.ChunksAsync(ct);

    /// <summary>A sequência da última sessão começada, ou a da primeira vez. A meta nunca é sugerida.</summary>
    public Task<FocusPlan> SuggestedPlanAsync(CancellationToken ct = default) => settings.GetFocusPlanAsync(ct);

    /// <summary>
    /// O motivo de "Começar" estar desligado: um cronômetro rodando ou parado à espera de registro. O de meta que sumiu
    /// ou foi arquivada não conta, porque dá lugar à sessão. Nulo quando nada impede.
    /// </summary>
    public async Task<string?> TimerConflictAsync(CancellationToken ct = default)
    {
        if (await timers.GetAsync(ct) is not { } timer)
            return null;
        return await goals.GetAsync(timer.GoalId, ct) is { ArchivedAt: null } owner ? TimerConflict(owner.Title) : null;
    }

    /// <summary>
    /// Sugestões para a subatividade: as notas recentes da meta da sessão, ou de todas as metas na sessão sem meta
    /// (spec Pomodoro §4.2). Sem sessão, nenhuma.
    /// </summary>
    public async Task<IReadOnlyList<string>> TaskSuggestionsAsync(CancellationToken ct = default) =>
        await sessions.GetAsync(ct) is { } session ? await checkins.RecentNotesAsync(session.GoalId, ct) : [];

    private static string TimerConflict(string title) =>
        $"Já há um cronômetro em \"{title}\". Registre ou descarte no Hoje antes de começar o Pomodoro.";

    public async Task StartAsync(FocusPlan plan, Guid? goalId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Validate() is { Count: > 0 } errors)
            throw new GoalValidationException(errors);
        if (goalId is { } id)
            await TimeGoalAsync(id, ct);
        // Antes de gravar a sugestão: uma sessão aberta não deve ter a sequência sugerida trocada por um toque recusado.
        if (await sessions.GetAsync(ct) is not null)
            throw Exists();

        if (await timers.GetAsync(ct) is { } timer)
        {
            // Cronômetro de meta que sumiu ou foi arquivada é órfão: dá lugar à sessão, como no TimerService.
            var owner = await goals.GetAsync(timer.GoalId, ct);
            if (owner is { ArchivedAt: null })
                throw new GoalStateException(timer.GoalId, TimerConflict(owner.Title));
            await timers.ClearAsync(ct);
        }

        await settings.SetFocusPlanAsync(plan, ct);
        var now = time.GetUtcNow();
        var session = new FocusSession(plan, goalId, now, Step: 0, BlockStartedAt: now, CutAt: now, Carry: TimeSpan.Zero,
            PausedAt: null, Task: null, FinishedAt: null, AlertedStep: null, Revision: 0);
        // A guarda atômica de "nem sessão nem cronômetro" fica no repositório.
        if (!await sessions.TryStartAsync(session, ct))
            throw Exists();
        changes?.NotifyChanged();
    }

    /// <summary>Congela o relógio do bloco (idempotente). Recusa a sessão terminada e o bloco à espera do toque.</summary>
    public async Task PauseAsync(CancellationToken ct = default)
    {
        var session = await RequireAsync(ct);
        var now = time.GetUtcNow();
        if (session.PausedAt is not null)
            return;
        if (session.FinishedAt is not null || FocusClock.IsWaiting(session, now))
            throw Changed();
        await SaveAsync(session, session with { PausedAt = now }, cut: null, ct);
    }

    /// <summary>Solta o relógio (idempotente): o bloco e o corte andam pelo tempo pausado, que assim não conta.</summary>
    public async Task ResumeAsync(CancellationToken ct = default)
    {
        var session = await RequireAsync(ct);
        if (session.PausedAt is not { } pausedAt)
            return;
        var paused = time.GetUtcNow() - pausedAt;
        await SaveAsync(session, session with
        {
            BlockStartedAt = session.BlockStartedAt + paused,
            CutAt = session.CutAt + paused,
            PausedAt = null,
        }, cut: null, ct);
    }

    /// <summary>
    /// O toque no fim do bloco e o ⏭: fecha o trecho de um foco e começa o próximo bloco agora. No último bloco, termina a
    /// sessão e devolve o resumo. <paramref name="reviewedMinutes"/> é o tempo conferido do trecho aberto.
    /// </summary>
    public async Task<FocusSummary?> AdvanceAsync(int? reviewedMinutes = null, CancellationToken ct = default)
    {
        var session = await RunningAsync(ct);
        var now = time.GetUtcNow();
        if (session.IsLastStep)
            return await FinishAsync(session, reviewedMinutes, now, ct);

        // Depois de uma pausa nada é cortado: o resto de minuto segue para o próximo foco. O fim do último foco é o
        // último corte mesmo quando a sessão ainda tem pausas depois dele: arredonda e não deixa resto.
        var cut = session.Block.Kind == FocusBlockKind.Focus
            ? await CutAsync(session, reviewedMinutes, last: IsLastFocus(session), now, ct)
            : new Cut(null, null, session.GoalId, session.Carry);
        await SaveAsync(session, session with
        {
            Step = session.Step + 1,
            BlockStartedAt = now,
            CutAt = now,
            PausedAt = null,
            Carry = cut.Carry,
            GoalId = cut.GoalId,
        }, cut, ct);
        return null;
    }

    /// <summary>O ✕: fecha o último trecho e termina a sessão.</summary>
    public async Task<FocusSummary> EndAsync(int? reviewedMinutes = null, CancellationToken ct = default) =>
        await FinishAsync(await RunningAsync(ct), reviewedMinutes, time.GetUtcNow(), ct);

    /// <summary>O ⇄: fecha o trecho com o nome atual e segue contando o mesmo foco com <paramref name="task"/>.</summary>
    public async Task SwitchTaskAsync(string? task, CancellationToken ct = default)
    {
        task = NormalizeTask(task);
        var session = await RunningAsync(ct);
        if (session.Block.Kind != FocusBlockKind.Focus)
            throw Changed();
        var now = time.GetUtcNow();
        var cut = await CutAsync(session, reviewedMinutes: null, last: false, now, ct);
        // Pausado, o corte fica no instante da pausa: o retomar o desloca junto com o bloco.
        await SaveAsync(session, session with
        {
            Task = task,
            CutAt = session.PausedAt ?? now,
            Carry = cut.Carry,
            GoalId = cut.GoalId,
        }, cut, ct);
    }

    /// <summary>Dá (ou tira, com vazio) o nome da subatividade, sem cortar o trecho.</summary>
    public async Task SetTaskAsync(string? task, CancellationToken ct = default)
    {
        task = NormalizeTask(task);
        var session = await RunningAsync(ct);
        await SaveAsync(session, session with { Task = task }, cut: null, ct);
    }

    /// <summary>
    /// Soma os trechos guardados de uma sessão terminada sem meta em <paramref name="goalId"/>, cada um com a sua nota e
    /// com o instante da escolha, e fecha a sessão. Devolve o total do período da meta.
    /// </summary>
    public async Task<int> AssignAsync(Guid goalId, CancellationToken ct = default)
    {
        var session = await RequireAsync(ct);
        if (session.FinishedAt is null)
            throw Changed();
        await TimeGoalAsync(goalId, ct);

        var held = (await sessions.ChunksAsync(ct)).Where(c => c.CheckinId is null).ToList();
        int? total = null;
        var wrote = false;
        try
        {
            // Um trecho por vez: se um falhar, os já somados ficam ligados aos registros e não entram de novo.
            foreach (var chunk in held)
            {
                var (checkin, after) = await checkins.PrepareAsync(goalId, chunk.Minutes, chunk.Task, ct);
                if (!await sessions.AssignChunkAsync(chunk.Id, checkin, ct))
                    continue;
                wrote = true;
                total = after;
            }
            await sessions.ClearAsync(ct);
            wrote = true;
        }
        finally
        {
            if (wrote) changes?.NotifyChanged();
        }
        return total ?? await checkins.PeriodTotalAsync(goalId, ct);
    }

    /// <summary>Apaga os trechos guardados e a sessão terminada sem meta.</summary>
    public async Task DiscardAsync(CancellationToken ct = default)
    {
        var session = await RequireAsync(ct);
        if (session.FinishedAt is null)
            throw Changed();
        await sessions.ClearAsync(ct);
        changes?.NotifyChanged();
    }

    /// <summary>
    /// O último corte. Com meta, a sessão some e o resumo sai dos trechos. Sem meta, ela fica terminada, à espera de um
    /// destino para os trechos guardados; sem nada guardado, some também.
    /// </summary>
    private async Task<FocusSummary> FinishAsync(FocusSession session, int? reviewedMinutes, DateTimeOffset now,
        CancellationToken ct)
    {
        var before = await sessions.ChunksAsync(ct);
        var cut = await CutAsync(session, reviewedMinutes, last: true, now, ct);
        IReadOnlyList<FocusChunk> chunks = cut.Chunk is null ? before : [.. before, cut.Chunk];
        var held = chunks.Where(c => c.CheckinId is null).ToList();

        if (cut.GoalId is not null || held.Count == 0)
        {
            await SaveAsync(session, next: null, cut, ct);
            return FocusSummary.Of(chunks, assigned: true);
        }
        await SaveAsync(session, session with
        {
            GoalId = null,
            Carry = TimeSpan.Zero,
            PausedAt = null,
            FinishedAt = now,
        }, cut, ct);
        return FocusSummary.Of(held, assigned: false);
    }

    /// <summary>
    /// Fecha o foco aberto: os minutos (inteiros no meio da sessão, arredondados no último corte, ou os conferidos) viram
    /// um trecho e o resto de minuto vira o novo <see cref="FocusSession.Carry"/>. Com meta, monta o registro; se a meta
    /// deixou de aceitar registros, o trecho fica guardado e a sessão segue sem meta.
    /// </summary>
    private async Task<Cut> CutAsync(FocusSession session, int? reviewedMinutes, bool last, DateTimeOffset now,
        CancellationToken ct)
    {
        if (reviewedMinutes is null && FocusClock.NeedsReview(session, now))
            throw new GoalStateException(session.GoalId ?? Guid.Empty, "Confira o tempo do último trecho antes de seguir.");

        // Um relógio que voltou não deixa um trecho negativo.
        var open = FocusClock.OpenFocus(session, now);
        if (open < TimeSpan.Zero) open = TimeSpan.Zero;
        var minutes = reviewedMinutes ?? FocusClock.CutMinutes(open, last);
        if (minutes < 0 || minutes > CheckinService.MaxDurationAmount)
            throw new GoalValidationException([new ValidationError("Amount", "Valor fora do limite.")]);
        var carry = reviewedMinutes is not null || last ? TimeSpan.Zero : open - TimeSpan.FromMinutes(minutes);

        if (minutes == 0)
            return new Cut(null, null, session.GoalId, carry);
        var chunk = new FocusChunk(Guid.CreateVersion7(now), minutes, session.Task, now, CheckinId: null);
        if (session.GoalId is not { } goalId)
            return new Cut(chunk, null, null, carry);
        try
        {
            var (checkin, _) = await checkins.PrepareAsync(goalId, minutes, session.Task, ct);
            return new Cut(chunk with { CheckinId = checkin.Id }, checkin, goalId, carry);
        }
        catch (Exception e) when (e is GoalNotFoundException or GoalStateException)
        {
            Trace.TraceInformation($"Meta da sessão de Pomodoro deixou de aceitar registros: {TraceText.Types(e)}");
            return new Cut(chunk, null, null, carry);
        }
    }

    /// <summary>Se nenhum passo depois do atual é de foco.</summary>
    private static bool IsLastFocus(FocusSession session) =>
        Enumerable.Range(session.Step + 1, session.Plan.TotalSteps - session.Step - 1)
            .All(step => session.Plan.BlockAt(step).Kind != FocusBlockKind.Focus);

    private async Task SaveAsync(FocusSession current, FocusSession? next, Cut? cut, CancellationToken ct)
    {
        if (!await sessions.UpdateAsync(current.Revision, next, cut?.Chunk, cut?.Checkin, ct))
            throw Changed();
        changes?.NotifyChanged();
    }

    private async Task TimeGoalAsync(Guid goalId, CancellationToken ct)
    {
        var goal = await goals.GetAsync(goalId, ct) ?? throw new GoalNotFoundException(goalId);
        if (goal.Measure != Measure.Duration)
            throw new GoalStateException(goalId, "Só metas de tempo têm cronômetro.");
        if (goal.ArchivedAt is not null)
            throw new GoalStateException(goalId, "Meta arquivada não recebe registros.");
        if (!GoalPeriod.IsShownOn(goal, DateOnly.FromDateTime(time.GetLocalNow().DateTime)))
            throw new GoalStateException(goalId, "Esta meta não está ativa hoje.");
    }

    private async Task<FocusSession> RequireAsync(CancellationToken ct) =>
        await sessions.GetAsync(ct) ?? throw new GoalStateException(Guid.Empty, "Nenhuma sessão de Pomodoro aberta.");

    /// <summary>A sessão ainda rodando; terminada, só aceita um destino para o tempo.</summary>
    private async Task<FocusSession> RunningAsync(CancellationToken ct)
    {
        var session = await RequireAsync(ct);
        return session.FinishedAt is null ? session : throw Changed();
    }

    private static string? NormalizeTask(string? task)
    {
        task = string.IsNullOrWhiteSpace(task) ? null : task.Trim();
        if (task is { Length: > CheckinService.MaxNoteLength })
            throw new GoalValidationException(
                [new ValidationError("Task", $"A nota deve ter até {CheckinService.MaxNoteLength} caracteres.")]);
        return task;
    }

    private static GoalStateException Exists() => new(Guid.Empty, "Já há uma sessão de Pomodoro aberta.");

    private static GoalStateException Changed() => new(Guid.Empty, "A sessão mudou. Confira na aba Tempo.");

    /// <summary>O que um corte grava: o trecho (nulo com zero minutos), o registro na meta e o que a sessão leva adiante.</summary>
    private sealed record Cut(FocusChunk? Chunk, Checkin? Checkin, Guid? GoalId, TimeSpan Carry);
}

/// <summary>
/// Resumo de uma sessão terminada: o foco total e os minutos por subatividade. <see cref="Assigned"/> diz que nada
/// espera destino; falso, o resumo traz só os trechos guardados, que a pessoa soma numa meta ou descarta.
/// </summary>
public sealed record FocusSummary(int Minutes, IReadOnlyList<FocusSummaryLine> Lines, bool Assigned)
{
    /// <summary>Agrupa os trechos por subatividade, na ordem da primeira aparição.</summary>
    public static FocusSummary Of(IReadOnlyList<FocusChunk> chunks, bool assigned)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var lines = chunks.GroupBy(c => c.Task).Select(g => new FocusSummaryLine(g.Key, g.Sum(c => c.Minutes))).ToList();
        return new FocusSummary(chunks.Sum(c => c.Minutes), lines, assigned);
    }
}

/// <summary>Minutos de uma subatividade no resumo; <see cref="Task"/> nulo é o foco sem nome.</summary>
public sealed record FocusSummaryLine(string? Task, int Minutes);
