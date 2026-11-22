namespace SlimeGoals.Core.Domain;

/// <summary>Uma tag visível com as metas (não excluídas) que a usam; as arquivadas são contadas à parte.</summary>
public sealed record TagSummary(string Name, GoalColor Color, int ActiveGoals, int ArchivedGoals);
