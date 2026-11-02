using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta que monta o texto do lembrete (pt-BR) a partir da meta e do total do período.</summary>
public interface IReminderFormatter
{
    /// <param name="goal">Meta que está lembrando.</param>
    /// <param name="periodTotal">Soma da meta no período de avaliação atual.</param>
    ReminderText Format(Goal goal, int periodTotal);
}
