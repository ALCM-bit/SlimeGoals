using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Testing;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class GoalRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private GoalRepository Repo => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("7")]
    [InlineData("1")]
    [InlineData("azul")]
    public async Task Unknown_color_in_database_throws_format_exception(string color)
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal);
        _db.Anchor.Execute("UPDATE goals SET color = @Color WHERE id = @Id", new { Color = color, Id = goal.Id.ToString("D") });

        var act = () => Repo.GetAsync(goal.Id);

        await act.Should().ThrowAsync<FormatException>();
    }

    private void Corrupt(Goal goal, string assignment) =>
        _db.Anchor.Execute($"UPDATE goals SET {assignment} WHERE id = @Id", new { Id = goal.Id.ToString("D") });

    [Theory]
    [InlineData("color = 'azul'")]
    [InlineData("color = x'00ff'")]
    [InlineData("active_from = 'cedo'")]
    [InlineData("active_days = '9'")]
    [InlineData("start_date = 'ontem'")]
    [InlineData("updated_at = 'agora'")]
    [InlineData("archived_at = 'antes'")]
    [InlineData("target = 'muitos'")]
    [InlineData("target = 99999999999")]
    [InlineData("id = 'não é um guid'")]
    public async Task List_skips_invalid_row_in_any_position_and_keeps_the_others(string assignment)
    {
        var t0 = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

        // A posição importa: o leitor de colunas nasce da primeira linha do resultado.
        foreach (var position in Enumerable.Range(0, 3))
        {
            _db.Anchor.Execute("DELETE FROM goals");
            var goals = Enumerable.Range(0, 3).Select(i => NewGoal($"Meta {i}", t0.AddMinutes(i))).ToList();
            foreach (var goal in goals)
                await Repo.AddAsync(goal);
            var bad = goals[position];
            Corrupt(bad, assignment);

            var listed = await Repo.ListAsync(includeArchived: true);

            listed.Select(g => g.Id).Should().Equal(goals.Where(g => g != bad).Select(g => g.Id));
        }
    }

    [Fact]
    public async Task List_with_only_invalid_rows_is_empty()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal);
        Corrupt(goal, "color = 'azul'");

        (await Repo.ListAsync(includeArchived: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task List_records_the_skipped_row_in_trace()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal);
        Corrupt(goal, "color = 'azul'");
        using var trace = new TraceCapture();

        await Repo.ListAsync(includeArchived: true);

        trace.Messages.Should().Contain(m => m.Contains(goal.Id.ToString("D")) && m.Contains("goals"));
    }

    [Fact]
    public async Task List_does_not_hide_database_failure()
    {
        _db.Anchor.Execute("DROP TABLE reminder_log; DROP TABLE checkins; DROP TABLE active_timer; DROP TABLE goals;");

        var act = () => Repo.ListAsync(includeArchived: true);

        await act.Should().ThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task Get_of_row_with_wrong_column_type_throws_format_exception()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal);
        Corrupt(goal, "target = 'muitos'");

        var act = () => Repo.GetAsync(goal.Id);

        await act.Should().ThrowAsync<FormatException>();
    }

    private static readonly DateTimeOffset TagT0 = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static Tag NewTag(string name) =>
        new() { Id = TagIds.ForTag(name), Name = name, Color = GoalColor.Pink, UpdatedAt = TagT0 };

    private static GoalTag NewLink(Guid goalId, string name) =>
        new() { Id = TagIds.ForLink(goalId, name), GoalId = goalId, TagName = name, UpdatedAt = TagT0 };

    [Fact]
    public async Task Add_with_links_reads_back_visible_tags_sorted()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal, new TagChanges([NewTag("saúde"), NewTag("casa")],
            [NewLink(goal.Id, "saúde"), NewLink(goal.Id, "casa")]));

        (await Repo.GetAsync(goal.Id))!.Tags.Should().Equal("casa", "saúde");
        (await Repo.ListAsync(includeArchived: false)).Single().Tags.Should().Equal("casa", "saúde");
    }

    [Fact]
    public async Task Update_with_removed_link_hides_tag()
    {
        var goal = NewGoal();
        var link = NewLink(goal.Id, "casa");
        await Repo.AddAsync(goal, new TagChanges([NewTag("casa")], [link]));

        await Repo.UpdateAsync(goal, new TagChanges([], [link with { UpdatedAt = TagT0.AddMinutes(1), RemovedAt = TagT0.AddMinutes(1) }]));

        (await Repo.GetAsync(goal.Id))!.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task List_ignores_links_of_other_goals()
    {
        var a = NewGoal("A", TagT0);
        var b = NewGoal("B", TagT0.AddMinutes(1));
        await Repo.AddAsync(a, new TagChanges([NewTag("casa"), NewTag("saúde")], [NewLink(a.Id, "casa")]));
        await Repo.AddAsync(b, new TagChanges([], [NewLink(b.Id, "saúde")]));

        var listed = await Repo.ListAsync(includeArchived: false);

        listed.Single(g => g.Id == a.Id).Tags.Should().Equal("casa");
        listed.Single(g => g.Id == b.Id).Tags.Should().Equal("saúde");
    }

    [Fact]
    public async Task Link_to_missing_tag_row_still_listed()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal, new TagChanges([], [NewLink(goal.Id, "nova")]));

        (await Repo.ListAsync(includeArchived: false)).Single().Tags.Should().Equal("nova");
    }

    [Fact]
    public async Task Goal_update_without_changes_keeps_links()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal, new TagChanges([NewTag("casa")], [NewLink(goal.Id, "casa")]));

        await Repo.UpdateAsync(goal with { Title = "Outro título" });

        var read = (await Repo.GetAsync(goal.Id))!;
        read.Title.Should().Be("Outro título");
        read.Tags.Should().Equal("casa");
    }

    private static Goal NewGoal(string title = "Ler 📚 ação", DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Title = title,
        Recurrence = Recurrence.Daily,
        Target = 8,
        Icon = "book",
        Color = GoalColor.Pink,
        ReminderEveryMinutes = 90,
        ActiveFrom = new TimeOnly(9, 0),
        ActiveUntil = new TimeOnly(18, 30),
        ActiveDays = WeekDays.Monday | WeekDays.Sunday,
        StartDate = new DateOnly(2026, 11, 1),
        EndDate = null,
        // fração de segundo e offset ≠ 0 de propósito: o banco grava em UTC
        CreatedAt = createdAt ?? new DateTimeOffset(2026, 11, 2, 9, 15, 30, 123, TimeSpan.Zero).AddTicks(4567),
        UpdatedAt = new DateTimeOffset(2026, 11, 2, 9, 15, 30, TimeSpan.Zero),
    };

    [Theory]
    [InlineData(Measure.Completion, null, 1)]
    [InlineData(Measure.Quantity, "copos", 8)]
    [InlineData(Measure.Duration, null, 150)]
    public async Task Goal_round_trips_measure_and_unit(Measure measure, string? unit, int target)
    {
        var goal = NewGoal() with { Measure = measure, Unit = unit, Target = target };
        await Repo.AddAsync(goal);
        (await Repo.GetAsync(goal.Id)).Should().Be(goal);

        var changed = goal with { Unit = measure == Measure.Quantity ? "copos de 250 ml" : null };
        await Repo.UpdateAsync(changed);
        (await Repo.GetAsync(goal.Id)).Should().Be(changed);
    }

    [Fact]
    public async Task Add_then_Get_round_trips_every_field()
    {
        var goal = NewGoal() with { ArchivedAt = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero) };
        await Repo.AddAsync(goal);

        (await Repo.GetAsync(goal.Id)).Should().Be(goal);
    }

    [Fact]
    public async Task Instants_are_stored_as_utc_iso_text_and_ids_as_text()
    {
        var goal = NewGoal() with { CreatedAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-3)) };
        await Repo.AddAsync(goal);

        var row = _db.Anchor.QuerySingle<(string Id, string CreatedAt, string Type)>(
            "SELECT id, created_at, typeof(id) FROM goals");
        row.Id.Should().Be(goal.Id.ToString("D"));
        row.Type.Should().Be("text");
        row.CreatedAt.Should().Be("2026-11-02T12:00:00.0000000+00:00");
    }

    [Fact]
    public async Task Get_finds_goal_by_id_after_add()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal);
        (await Repo.GetAsync(goal.Id)).Should().NotBeNull();
        (await Repo.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Update_persists_changes()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal);
        var changed = goal with { Title = "Outro", Recurrence = Recurrence.Weekly, Target = 3, EndDate = new DateOnly(2026, 12, 31) };

        await Repo.UpdateAsync(changed);

        (await Repo.GetAsync(goal.Id)).Should().Be(changed);
    }

    [Fact]
    public async Task Update_unknown_id_throws_GoalNotFoundException()
    {
        var act = () => Repo.UpdateAsync(NewGoal());
        await act.Should().ThrowAsync<GoalNotFoundException>();
    }

    [Fact]
    public async Task Get_returns_null_for_soft_deleted()
    {
        var goal = NewGoal();
        await Repo.AddAsync(goal);
        await Repo.UpdateAsync(goal with { DeletedAt = goal.UpdatedAt });
        (await Repo.GetAsync(goal.Id)).Should().BeNull();
    }

    [Fact]
    public async Task List_excludes_archived_unless_requested_and_orders_by_created_at()
    {
        var t = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        var second = NewGoal("B", t.AddHours(1));
        var first = NewGoal("A", t);
        var archived = NewGoal("C", t.AddHours(2)) with { ArchivedAt = t };
        var deleted = NewGoal("D", t.AddHours(3)) with { DeletedAt = t };
        foreach (var g in new[] { second, first, archived, deleted }) await Repo.AddAsync(g);

        (await Repo.ListAsync(includeArchived: false)).Select(g => g.Title).Should().Equal("A", "B");
        (await Repo.ListAsync(includeArchived: true)).Select(g => g.Title).Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task Data_survives_reopening_file_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"slimegoals-test-{Guid.NewGuid():N}.db");
        try
        {
            var goal = NewGoal();
            var factory = new SqliteConnectionFactory($"Data Source={path}");
            using (var c = factory.Open()) MigrationRunner.Migrate(c);
            await new GoalRepository(factory).AddAsync(goal);

            var reopened = new SqliteConnectionFactory($"Data Source={path}");
            using (var c = reopened.Open())
            {
                MigrationRunner.Migrate(c).Should().Be(10);
                c.ExecuteScalar<long>("PRAGMA user_version").Should().Be(10);
            }
            (await new GoalRepository(reopened).GetAsync(goal.Id)).Should().Be(goal);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
