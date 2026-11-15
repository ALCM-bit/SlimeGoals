using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Core.Tests.Services;

/// <summary>O rótulo do cronômetro fora do app: a meta, e a tarefa depois de um ponto médio.</summary>
public class RunningTimerTests
{
    private static RunningTimer Timer(string? task) =>
        new(Guid.Empty, "Estudar", "book", GoalColor.Green, DateTimeOffset.UnixEpoch, task);

    [Fact]
    public void Label_without_task_is_the_goal_title() => Timer(null).Label.Should().Be("Estudar");

    [Fact]
    public void Label_with_task_joins_with_a_middle_dot() => Timer("Inglês").Label.Should().Be("Estudar · Inglês");
}
