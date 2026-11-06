using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

/// <summary>Exclusão definitiva no banco real: chaves estrangeiras ligadas, transação e bytes do arquivo.</summary>
public class GoalDeletionSqliteTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private GoalRepository Goals => new(_db.Factory);
    private CheckinRepository Checkins => new(_db.Factory);
    private ReminderLogRepository Log => new(_db.Factory);
    private TimerRepository Timers => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static Goal NewGoal(string title = "Meta") => new()
    {
        Id = Guid.CreateVersion7(), Title = title, Recurrence = Recurrence.Daily, Target = 8, Icon = "water",
        Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = T0, Unit = "copos",
    };

    private static Checkin NewCheckin(Goal goal, string? note = null) => new()
    {
        Id = Guid.CreateVersion7(), GoalId = goal.Id, DoneAt = T0, Amount = 1, Note = note, UpdatedAt = T0,
    };

    private async Task<Goal> AddWithEverythingLinked(GoalRepository goals, CheckinRepository checkins, ReminderLogRepository log,
        string title = "Meta", string? note = null)
    {
        var goal = NewGoal(title);
        await goals.AddAsync(goal);
        await checkins.AddAsync(NewCheckin(goal, note));
        await log.AddAsync(new ReminderLogEntry(Guid.CreateVersion7(), goal.Id, T0));
        return goal;
    }

    private long Count(string table, Goal goal) =>
        _db.Anchor.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table} WHERE {(table == "goals" ? "id" : "goal_id")} = @Id",
            new { Id = goal.Id.ToString("D") });

    [Fact]
    public async Task Delete_removes_the_goal_and_every_linked_row_of_that_goal_only()
    {
        var goal = await AddWithEverythingLinked(Goals, Checkins, Log);
        var other = await AddWithEverythingLinked(Goals, Checkins, Log);
        await Timers.TryStartAsync(new ActiveTimer(goal.Id, T0));

        await Goals.DeleteAsync(goal.Id);

        foreach (var table in new[] { "goals", "checkins", "reminder_log", "active_timer" })
            Count(table, goal).Should().Be(0, table);
        foreach (var table in new[] { "goals", "checkins", "reminder_log" })
            Count(table, other).Should().Be(1, table);
    }

    [Fact]
    public async Task Delete_keeps_the_timer_of_another_goal()
    {
        var goal = await AddWithEverythingLinked(Goals, Checkins, Log);
        var other = await AddWithEverythingLinked(Goals, Checkins, Log);
        await Timers.TryStartAsync(new ActiveTimer(other.Id, T0));

        await Goals.DeleteAsync(goal.Id);

        (await Timers.GetAsync())!.GoalId.Should().Be(other.Id);
    }

    // Toda tabela com goal_id precisa entrar na exclusão: com foreign_keys ON, a que faltar impede apagar a meta.
    [Fact]
    public void Every_table_that_references_goals_is_covered()
    {
        _db.Anchor.Query<string>("""
            SELECT m.name FROM sqlite_master m, pragma_foreign_key_list(m.name) f
            WHERE m.type = 'table' AND f."table" = 'goals'
            """).Should().BeEquivalentTo("checkins", "reminder_log", "active_timer");
    }

    [Fact]
    public async Task Delete_of_missing_goal_throws_not_found()
    {
        var act = () => Goals.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<GoalNotFoundException>();
    }

    [Fact]
    public async Task Failure_midway_rolls_everything_back()
    {
        var goal = await AddWithEverythingLinked(Goals, Checkins, Log);
        await Timers.TryStartAsync(new ActiveTimer(goal.Id, T0));
        _db.Anchor.Execute("CREATE TRIGGER block BEFORE DELETE ON goals BEGIN SELECT RAISE(ABORT, 'bloqueado'); END");

        var act = () => Goals.DeleteAsync(goal.Id);

        await act.Should().ThrowAsync<SqliteException>().Where(e => e.Message.Contains("bloqueado"));
        foreach (var table in new[] { "goals", "checkins", "reminder_log", "active_timer" })
            Count(table, goal).Should().Be(1, table);
    }

    // A meta pode sair entre a leitura e a escrita de quem registra: vira "meta não existe", que o Core já trata.
    [Fact]
    public async Task Checkin_of_goal_that_no_longer_exists_throws_not_found()
    {
        var act = () => Checkins.AddAsync(NewCheckin(NewGoal()));

        await act.Should().ThrowAsync<GoalNotFoundException>();
    }

    [Fact]
    public async Task Timer_of_goal_that_no_longer_exists_throws_not_found()
    {
        var act = () => Timers.TryStartAsync(new ActiveTimer(Guid.NewGuid(), T0));

        await act.Should().ThrowAsync<GoalNotFoundException>();
    }

    [Fact]
    public void Connections_have_secure_delete_on()
    {
        using var c = _db.Factory.Open();

        c.ExecuteScalar<long>("PRAGMA secure_delete").Should().Be(1);
    }

    [Fact]
    public async Task Deleted_content_is_not_left_readable_in_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"slimegoals-delete-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory($"Data Source={path}");
            using (var c = factory.Open()) MigrationRunner.Migrate(c);
            var goals = new GoalRepository(factory);
            var goal = await AddWithEverythingLinked(goals, new CheckinRepository(factory), new ReminderLogRepository(factory),
                "TituloSensivelXyz", "NotaSensivelXyz");
            await AddWithEverythingLinked(goals, new CheckinRepository(factory), new ReminderLogRepository(factory), "TituloQueFica");

            await goals.DeleteAsync(goal.Id);

            SqliteConnection.ClearAllPools();
            var file = File.ReadAllBytes(path);
            file.AsSpan().IndexOf("TituloQueFica"u8).Should().BeGreaterThan(0);
            file.AsSpan().IndexOf("TituloSensivelXyz"u8).Should().Be(-1);
            file.AsSpan().IndexOf("NotaSensivelXyz"u8).Should().Be(-1);
            file.AsSpan().IndexOf(Encoding.UTF8.GetBytes(goal.Id.ToString("D"))).Should().Be(-1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
