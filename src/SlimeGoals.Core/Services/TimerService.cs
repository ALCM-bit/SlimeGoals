using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Cronômetro das metas de tempo (spec §5.2): um por vez, gravado no banco para sobreviver ao fechamento
/// do app. Parar congela os segundos; o registro só acontece quando o usuário confirma.
/// </summary>
public sealed class TimerService(IGoalRepository goals, ITimerRepository timers, CheckinService checkins, TimeProvider time,
    DataChangeNotifier? changes = null, IFocusSessionRepository? focus = null)
{
    public Task<ActiveTimer?> GetAsync(CancellationToken ct = default) => timers.GetAsync(ct);

    public async Task StartAsync(Guid goalId, CancellationToken ct = default)
    {
        var goal = await goals.GetAsync(goalId, ct) ?? throw new GoalNotFoundException(goalId);
        if (goal.Measure != Measure.Duration)
            throw new GoalStateException(goalId, "Só metas de tempo têm cronômetro.");
        if (goal.ArchivedAt is not null)
            throw new GoalStateException(goalId, "Meta arquivada não recebe registros.");
        if (!GoalPeriod.IsShownOn(goal, DateOnly.FromDateTime(time.GetLocalNow().DateTime)))
            throw new GoalStateException(goalId, "Esta meta não está ativa hoje.");

        await EnsureNoFocusSessionAsync(goalId, ct);

        if (await timers.GetAsync(ct) is { } existing)
        {
            // Cronômetro de meta que sumiu ou foi arquivada é órfão: dá lugar ao novo.
            var owner = await goals.GetAsync(existing.GoalId, ct);
            if (owner is { ArchivedAt: null })
                throw Conflict(goalId, owner.Title);
            await timers.ClearAsync(ct);
        }

        if (!await timers.TryStartAsync(new ActiveTimer(goalId, time.GetUtcNow()), ct))
        {
            // A sessão pode ter nascido entre a checagem e a gravação: a mensagem certa é a dela.
            await EnsureNoFocusSessionAsync(goalId, ct);
            throw Conflict(goalId, "outra meta");
        }
        changes?.NotifyChanged();
    }

    /// <summary>Um relógio por vez: qualquer sessão de Pomodoro, até a terminada sem destino, recusa o cronômetro.</summary>
    private async Task EnsureNoFocusSessionAsync(Guid goalId, CancellationToken ct)
    {
        if (focus is not null && await focus.GetAsync(ct) is not null)
            throw new GoalStateException(goalId, "Há uma sessão de Pomodoro aberta. Termine-a na aba Tempo.");
    }

    /// <summary>Congela o cronômetro (idempotente) e devolve os segundos.</summary>
    public async Task<int> StopAsync(CancellationToken ct = default)
    {
        if (await timers.GetAsync(ct) is null)
            throw NoTimer();
        if (await timers.StopAsync(time.GetUtcNow(), ct))
            changes?.NotifyChanged();
        // Relê: uma troca de tarefa de outro processo pode ter mudado o início entre a leitura e a parada.
        var timer = await timers.GetAsync(ct) ?? throw NoTimer();
        return Seconds(timer, time.GetUtcNow());
    }

    /// <summary>Registra os segundos (ajustáveis) do cronômetro parado, limpa-o e devolve o novo total do período.</summary>
    public async Task<int> RegisterAsync(int seconds, string? note = null, CancellationToken ct = default)
    {
        if (await timers.GetAsync(ct) is not { StoppedAt: not null } timer)
            throw new GoalStateException(Guid.Empty, "Pare o cronômetro antes de registrar.");
        var total = await checkins.AdjustAsync(timer.GoalId, seconds, note, ct);
        await timers.ClearAsync(ct);
        changes?.NotifyChanged();
        return total;
    }

    /// <summary>Dá (ou tira, com vazio) o nome da tarefa do cronômetro, rodando ou parado. Nunca cria um cronômetro.</summary>
    public async Task SetTaskAsync(string? task, CancellationToken ct = default)
    {
        task = NormalizeTask(task);
        if (!await timers.SetTaskAsync(task, ct))
            throw NoTimer();
        changes?.NotifyChanged();
    }

    /// <summary>
    /// Registra o trecho da tarefa atual e segue contando com <paramref name="newTask"/>. O trecho leva só os segundos
    /// inteiros, e o novo início é o antigo mais esses segundos: a fração de segundo que sobra passa para a próxima
    /// tarefa, e nenhum instante é contado duas vezes nem perdido. Abaixo de um segundo, só o nome muda. Devolve o total
    /// do período.
    /// </summary>
    public async Task<int> SwitchTaskAsync(string? newTask, CancellationToken ct = default)
    {
        newTask = NormalizeTask(newTask);
        // Só há ⇄ com o cronômetro rodando: parado ou sumido, foi outro processo (o "Parar" da notificação, da janelinha).
        if (await timers.GetAsync(ct) is not { StoppedAt: null } timer)
            throw Changed(Guid.Empty);

        var seconds = Seconds(timer, time.GetUtcNow());
        if (seconds <= 0)
        {
            // Sem trecho, só o nome muda, mas pela mesma guarda: um cronômetro parado nesse meio-tempo não é renomeado.
            if (!await timers.SwitchAsync(timer.StartedAt, timer with { Task = newTask }, chunk: null, ct))
                throw Changed(timer.GoalId);
            changes?.NotifyChanged();
            return await checkins.PeriodTotalAsync(timer.GoalId, ct);
        }

        var (chunk, total) = await checkins.PrepareAsync(timer.GoalId, seconds, timer.Task, ct);
        var next = timer with { StartedAt = timer.StartedAt.AddSeconds(seconds), Task = newTask };
        if (!await timers.SwitchAsync(timer.StartedAt, next, chunk, ct))
            throw Changed(timer.GoalId);
        changes?.NotifyChanged();
        return total;
    }

    /// <summary>Sugestões para o nome da tarefa: as notas recentes dos registros da meta.</summary>
    public Task<IReadOnlyList<string>> TaskSuggestionsAsync(Guid goalId, CancellationToken ct = default) =>
        checkins.RecentNotesAsync(goalId, ct);

    private static string? NormalizeTask(string? task)
    {
        task = string.IsNullOrWhiteSpace(task) ? null : task.Trim();
        if (task is { Length: > CheckinService.MaxNoteLength })
            throw new GoalValidationException(
                [new ValidationError("Task", $"A tarefa deve ter até {CheckinService.MaxNoteLength} caracteres.")]);
        return task;
    }

    private static GoalStateException NoTimer() => new(Guid.Empty, "Nenhum cronômetro rodando.");

    private static GoalStateException Changed(Guid goalId) => new(goalId, "O cronômetro mudou. Confira em Hoje.");

    public async Task DiscardAsync(CancellationToken ct = default)
    {
        await timers.ClearAsync(ct);
        changes?.NotifyChanged();
    }

    /// <summary>Segundos inteiros entre o início e a parada (ou agora), truncando a fração.</summary>
    public static int Seconds(ActiveTimer timer, DateTimeOffset now) =>
        (int)Math.Floor(((timer.StoppedAt ?? now) - timer.StartedAt).TotalSeconds);

    private static GoalStateException Conflict(Guid goalId, string title) =>
        new(goalId, $"Já há um cronômetro em \"{title}\". Pare-o antes de iniciar outro.");
}
