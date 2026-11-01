namespace SlimeGoals.Core.Domain;

/// <summary>O que se conta no período da meta (spec §5.2). Independente da recorrência.</summary>
public enum Measure
{
    /// <summary>Feito ou não feito no período; alvo sempre 1.</summary>
    Completion,
    /// <summary>Inteiro numa unidade livre (copos, páginas, vezes).</summary>
    Quantity,
    /// <summary>Tempo em minutos.</summary>
    Duration,
}
