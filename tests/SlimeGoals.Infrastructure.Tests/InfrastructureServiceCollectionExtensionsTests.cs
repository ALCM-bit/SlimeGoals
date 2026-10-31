using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests;

public class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSlimeGoalsInfrastructure_creates_missing_directory_and_resolves_repository()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"slimegoals-di-{Guid.NewGuid():N}", "nested");
        try
        {
            using var provider = new ServiceCollection()
                .AddSlimeGoalsInfrastructure(Path.Combine(dir, "slimegoals.db"))
                .BuildServiceProvider();

            Directory.Exists(dir).Should().BeTrue();
            provider.GetRequiredService<IGoalRepository>().Should().BeOfType<GoalRepository>();
            provider.GetRequiredService<SqliteConnectionFactory>().Should().BeSameAs(provider.GetRequiredService<SqliteConnectionFactory>());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }
}
