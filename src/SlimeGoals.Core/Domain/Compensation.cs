namespace SlimeGoals.Core.Domain;

/// <summary>
/// Marca um registro gravado para compensar o total limitado do período (spec de sincronização §3):
/// o início do período e as compensações do mesmo período que o aparelho já tinha ao gravar.
/// </summary>
public sealed record Compensation(DateOnly Period, IReadOnlyList<Guid> Seen);
