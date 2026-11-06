namespace SlimeGoals.Core.Domain;

/// <summary>
/// Total de um período como se mostra: de 0 até o alvo nas metas de conclusão, no mínimo 0 nas demais.
/// Registros de aparelhos diferentes podem somar fora disso; o dado fica como está e só a leitura limita.
/// Vale para o total de um período, não para a soma de um dia dentro de uma semana.
/// </summary>
public static class GoalTotals
{
    public static int Effective(Goal goal, int total) =>
        goal.Measure == Measure.Completion ? Math.Clamp(total, 0, 1) : Math.Max(0, total);
}
