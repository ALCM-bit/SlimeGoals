namespace SlimeGoals.Core.Domain;

/// <summary>Resposta do usuário a um lembrete (spec §8: 'done' | 'snooze' | 'ignore').</summary>
public enum ReminderResponse { Done, Snooze, Ignore }

/// <summary>Registro de um lembrete disparado; instantes em UTC. Sem resposta = ainda pendente.</summary>
public sealed record ReminderLogEntry(
    Guid Id,
    Guid GoalId,
    DateTimeOffset FiredAt,
    ReminderResponse? Response = null,
    DateTimeOffset? RespondedAt = null);
