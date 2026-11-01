using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Cronômetro das metas de tempo (spec §5.2): um por vez, gravado no banco para sobreviver ao fechamento
/// do app. Parar congela os minutos; o registro só acontece quando o usuário confirma.
/// </summary>
public sealed class TimerService(IGoalRepository goals, ITimerRepository timers, CheckinService checkins, TimeProvider time)
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

        if (await timers.GetAsync(ct) is { } existing)
        {
            // Cronômetro de meta que sumiu ou foi arquivada é órfão: dá lugar ao novo.
            var owner = await goals.GetAsync(existing.GoalId, ct);
            if (owner is { ArchivedAt: null })
                throw Conflict(goalId, owner.Title);
            await timers.ClearAsync(ct);
        }

        if (!await timers.TryStartAsync(new ActiveTimer(goalId, time.GetUtcNow()), ct))
            throw Conflict(goalId, "outra meta");
    }

    /// <summary>Congela o cronômetro (idempotente) e devolve os minutos.</summary>
    public async Task<int> StopAsync(CancellationToken ct = default)
    {
        var timer = await timers.GetAsync(ct) ?? throw new GoalStateException(Guid.Empty, "Nenhum cronômetro rodando.");
        if (timer.StoppedAt is null)
        {
            timer = timer with { StoppedAt = time.GetUtcNow() };
            await timers.SaveAsync(timer, ct);
        }
        return Minutes(timer, time.GetUtcNow());
    }

    /// <summary>Registra os minutos (ajustáveis) do cronômetro parado, limpa-o e devolve o novo total do período.</summary>
    public async Task<int> RegisterAsync(int minutes, string? note = null, CancellationToken ct = default)
    {
        if (await timers.GetAsync(ct) is not { StoppedAt: not null } timer)
            throw new GoalStateException(Guid.Empty, "Pare o cronômetro antes de registrar.");
        var total = await checkins.AdjustAsync(timer.GoalId, minutes, note, ct);
        await timers.ClearAsync(ct);
        return total;
    }

    public Task DiscardAsync(CancellationToken ct = default) => timers.ClearAsync(ct);

    /// <summary>Minutos inteiros entre o início e a parada (ou agora), arredondando ao mais próximo.</summary>
    public static int Minutes(ActiveTimer timer, DateTimeOffset now) =>
        (int)Math.Round(((timer.StoppedAt ?? now) - timer.StartedAt).TotalMinutes, MidpointRounding.AwayFromZero);

    private static GoalStateException Conflict(Guid goalId, string title) =>
        new(goalId, $"Já há um cronômetro em \"{title}\". Pare-o antes de iniciar outro.");
}
