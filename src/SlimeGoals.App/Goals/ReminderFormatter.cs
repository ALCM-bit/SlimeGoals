using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Goals;

/// <summary>
/// Texto da notificação de lembrete (spec §9.11): título da meta, progresso do período como na tela Hoje
/// e rótulo da ação principal conforme a medida.
/// </summary>
public sealed class ReminderFormatter : IReminderFormatter
{
    public ReminderText Format(Goal goal, int periodTotal) => new(
        goal.Title,
        GoalFormatting.Progress(new GoalProgress(goal, periodTotal, periodTotal >= goal.Target)),
        goal.Measure switch
        {
            Measure.Completion => "Feito",
            Measure.Quantity => "+1",
            Measure.Duration => "Iniciar",
            _ => throw new ArgumentOutOfRangeException(nameof(goal), goal.Measure, null),
        });
}
