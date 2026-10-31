using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Core.Abstractions;

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

public class InfrastructurePathTests
{
    // Revisão final #4: caminho com ';' não pode quebrar a connection string.
    [Fact]
    public void Database_path_with_semicolon_opens_that_exact_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"slimegoals-semi-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "a;b", "slimegoals.db");
        try
        {
            using var provider = new ServiceCollection().AddSlimeGoalsInfrastructure(path).BuildServiceProvider();
            using (provider.GetRequiredService<SqliteConnectionFactory>().Open()) { }
            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
