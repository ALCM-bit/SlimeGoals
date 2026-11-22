using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.Hosting;

/// <summary>
/// O contêiner dos heads (registros do app e da infraestrutura) com um banco SQLite de verdade numa pasta temporária.
/// Guarda os parâmetros opcionais que o contêiner preenche: sem o <see cref="ITagRepository"/>, o
/// <see cref="GoalService"/> salvaria a meta sem as tags, sem erro nenhum.
/// </summary>
public sealed class AppContainerSqliteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"slimegoals-container-{Guid.NewGuid():N}");
    private readonly ServiceProvider _services;

    public AppContainerSqliteTests()
    {
        _services = new ServiceCollection()
            .AddSlimeGoalsInfrastructure(Path.Combine(_directory, "slimegoals.db"))
            .AddSingleton<INotificationService>(new FakeNotificationService())
            .AddSlimeGoalsApp()
            .BuildServiceProvider();
        using var connection = _services.GetRequiredService<SqliteConnectionFactory>().Open();
        MigrationRunner.Migrate(connection);
    }

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Goal_service_from_the_container_saves_the_tags_of_the_draft()
    {
        var goals = _services.GetRequiredService<GoalService>();
        var draft = new GoalDraft("Ler", Recurrence.Daily, 3, "book", GoalColor.Purple, 120,
            new TimeOnly(9, 0), new TimeOnly(18, 0), WeekDays.All, null, null)
        { Tags = ["Saúde", "estudos"] };

        var created = await goals.CreateAsync(draft);

        (await goals.GetAsync(created.Id))!.Tags.Should().Equal("estudos", "saúde");
        (await _services.GetRequiredService<TagService>().ListAsync()).Select(t => t.Name).Should().Contain("saúde");
    }
}
