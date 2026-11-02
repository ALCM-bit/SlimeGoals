using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Trata o que o usuário escolheu numa notificação de lembrete (spec §9.11): executa a ação principal,
/// adia, ignora ou pede para abrir o app, e grava a resposta no log.
/// </summary>
public sealed class ReminderResponseHandler(
    IReminderLogRepository log,
    IGoalRepository goals,
    CheckinService checkins,
    TimerService timers,
    TimeProvider time,
    DataChangeNotifier? changes = null)
{
    // Serializa as respostas: verificação de "já respondido", ação e gravação ficam atômicas no processo.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ReminderOutcome> HandleAsync(Guid logId, ReminderAction action, CancellationToken ct = default)
    {
        if (action == ReminderAction.Open)
            return ReminderOutcome.OpenApp;

        await _gate.WaitAsync(ct);
        try
        {
            // Retém os avisos até a resposta ser gravada: senão o agendador, avisado pela ação (registro ou
            // cronômetro), veria o lembrete ainda pendente com a meta já atendida e o marcaria como ignorado.
            using (changes?.Defer())
                return await HandleLockedAsync(logId, action, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ReminderOutcome> HandleLockedAsync(Guid logId, ReminderAction action, CancellationToken ct)
    {
        var entry = await log.GetAsync(logId, ct);
        if (entry is null || entry.Response is not null)
            return ReminderOutcome.Stale;

        switch (action)
        {
            case ReminderAction.Snooze:
                return await RespondAsync(logId, ReminderResponse.Snooze, ct);
            case ReminderAction.Ignore:
                return await RespondAsync(logId, ReminderResponse.Ignore, ct);
            case ReminderAction.Primary:
                return await PrimaryAsync(entry, ct);
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Ação desconhecida.");
        }
    }

    private async Task<ReminderOutcome> PrimaryAsync(ReminderLogEntry entry, CancellationToken ct)
    {
        var response = ReminderResponse.Done;
        try
        {
            var goal = await goals.GetAsync(entry.GoalId, ct) ?? throw new GoalNotFoundException(entry.GoalId);
            if (goal.Measure == Measure.Duration)
            {
                if (await HasOtherActiveTimerAsync(entry.GoalId, ct))
                    return ReminderOutcome.OpenApp; // o log continua pendente
                await timers.StartAsync(entry.GoalId, ct);
            }
            else
            {
                await checkins.AdjustAsync(entry.GoalId, 1, ct: ct);
            }
        }
        catch (Exception e) when (e is GoalStateException or GoalNotFoundException)
        {
            response = ReminderResponse.Ignore; // meta concluída, arquivada, inativa hoje ou removida
        }
        return await RespondAsync(entry.Id, response, ct);
    }

    /// <summary>Cronômetro de outra meta ainda viva; o de meta arquivada ou removida é órfão e não conta.</summary>
    private async Task<bool> HasOtherActiveTimerAsync(Guid goalId, CancellationToken ct)
    {
        if (await timers.GetAsync(ct) is not { } timer || timer.GoalId == goalId)
            return false;
        return await goals.GetAsync(timer.GoalId, ct) is { ArchivedAt: null };
    }

    private async Task<ReminderOutcome> RespondAsync(Guid logId, ReminderResponse response, CancellationToken ct)
    {
        if (!await log.RespondAsync(logId, response, time.GetUtcNow(), ct))
            return ReminderOutcome.Stale;
        changes?.NotifyChanged();
        return ReminderOutcome.Handled;
    }
}
