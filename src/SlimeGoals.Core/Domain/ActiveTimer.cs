namespace SlimeGoals.Core.Domain;

/// <summary>
/// Cronômetro de uma meta de tempo; no máximo um em todo o app (spec §5.2).
/// <see cref="StoppedAt"/> nulo → rodando; preenchido → parado, aguardando registrar ou descartar.
/// </summary>
public sealed record ActiveTimer(Guid GoalId, DateTimeOffset StartedAt, DateTimeOffset? StoppedAt = null);
