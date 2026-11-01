namespace SlimeGoals.Core.Domain;

/// <summary>Tipo de recorrência da meta (spec §5.1).</summary>
public enum Recurrence
{
    /// <summary>Hábito diário: alvo por dia nos dias ativos.</summary>
    Daily,
    /// <summary>Meta semanal: alvo por semana (segunda a domingo).</summary>
    Weekly,
}
