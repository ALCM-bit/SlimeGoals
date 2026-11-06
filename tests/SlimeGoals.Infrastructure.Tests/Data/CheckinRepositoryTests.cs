using Dapper;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class CheckinRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private readonly Goal _goal;
    private readonly Goal _other;
    private CheckinRepository Repo => new(_db.Factory);

    public CheckinRepositoryTests()
    {
        _goal = NewGoal();
        _other = NewGoal();
        var goals = new GoalRepository(_db.Factory);
        goals.AddAsync(_goal).GetAwaiter().GetResult();
        goals.AddAsync(_other).GetAwaiter().GetResult();
    }

    public void Dispose() => _db.Dispose();

    private static Goal NewGoal() => new()
    {
        Id = Guid.CreateVersion7(), Title = "Água", Recurrence = Recurrence.Daily, Target = 8, Icon = "water",
        Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = T0, Unit = "copos",
    };

    private static Checkin At(Goal goal, DateTimeOffset doneAt, int amount, string? note = null) => new()
    {
        Id = Guid.CreateVersion7(), GoalId = goal.Id, DoneAt = doneAt, Amount = amount, Note = note, UpdatedAt = doneAt,
    };

    [Theory]
    [InlineData("id = 'não é um guid'")]
    [InlineData("updated_at = 'agora'")]
    [InlineData("done_at = '2026-11-02T12:30:00 lixo'")]
    [InlineData("amount = 'muitos'")]
    public async Task List_skips_invalid_row_in_any_position_and_keeps_the_others(string assignment)
    {
        foreach (var position in Enumerable.Range(0, 3))
        {
            _db.Anchor.Execute("DELETE FROM checkins");
            var checkins = Enumerable.Range(0, 3).Select(i => At(_goal, T0.AddHours(i), 1)).ToList();
            foreach (var checkin in checkins)
                await Repo.AddAsync(checkin);
            var bad = checkins[position];
            _db.Anchor.Execute($"UPDATE checkins SET {assignment} WHERE id = @Id", new { Id = bad.Id.ToString("D") });
            using var trace = new TraceCapture();

            var listed = await Repo.ListAsync(T0.AddDays(-1), T0.AddDays(1));

            listed.Select(c => c.Id).Should().Equal(checkins.Where(c => c != bad).Select(c => c.Id));
            trace.Messages.Should().Contain(m => m.Contains("checkins"));
        }
    }

    [Fact]
    public async Task Checkin_round_trip_and_sum_ignore_deleted_and_respect_half_open_range()
    {
        // offset ≠ 0 e fração de segundo: o banco grava em UTC e devolve o mesmo instante
        var first = At(_goal, new DateTimeOffset(2026, 11, 2, 9, 0, 0, 500, TimeSpan.FromHours(-3)), 3, "📚 manhã");
        var removal = At(_goal, T0.AddHours(1), -1);
        var deleted = At(_goal, T0.AddHours(2), 10) with { DeletedAt = T0.AddHours(3) };
        var atEnd = At(_goal, T0.AddDays(1), 5);
        var otherGoal = At(_other, T0, 7);
        foreach (var c in new[] { atEnd, removal, first, deleted, otherGoal })
            await Repo.AddAsync(c);

        var list = await Repo.ListAsync(T0, T0.AddDays(1));

        list.Should().Equal(otherGoal, first, removal); // first = 12:00:00,5 UTC
        list[1].DoneAt.Offset.Should().Be(TimeSpan.Zero);
        (await Repo.SumAsync(_goal.Id, T0, T0.AddDays(1))).Should().Be(2);
        (await Repo.SumAsync(_goal.Id, DateTimeOffset.MinValue, DateTimeOffset.MaxValue)).Should().Be(7);
        _db.Anchor.ExecuteScalar<string>("SELECT done_at FROM checkins WHERE amount = 3").Should().Be("2026-11-02T12:00:00.5000000+00:00");
    }

    [Fact]
    public async Task HasAny_ignores_deleted()
    {
        (await Repo.HasAnyAsync(_goal.Id)).Should().BeFalse();
        await Repo.AddAsync(At(_goal, T0, 1) with { DeletedAt = T0 });
        (await Repo.HasAnyAsync(_goal.Id)).Should().BeFalse();
        await Repo.AddAsync(At(_goal, T0, 1));
        (await Repo.HasAnyAsync(_goal.Id)).Should().BeTrue();
        (await Repo.HasAnyAsync(_other.Id)).Should().BeFalse();
    }

    private static readonly DateOnly Monday = new(2026, 11, 2);

    [Fact]
    public async Task Sum_and_list_ignore_superseded()
    {
        var kept = At(_goal, T0, 2);
        var gone = At(_goal, T0.AddHours(1), 5);
        await Repo.AddAsync(kept);
        await Repo.AddAsync(gone);
        _db.Anchor.Execute("UPDATE checkins SET superseded = 1 WHERE id = @Id", new { Id = gone.Id.ToString("D") });

        (await Repo.ListAsync(T0, T0.AddDays(1))).Should().Equal(kept);
        (await Repo.SumAsync(_goal.Id, T0, T0.AddDays(1))).Should().Be(2);
    }

    [Fact]
    public async Task HasAny_counts_superseded()
    {
        var only = At(_goal, T0, 5);
        await Repo.AddAsync(only);
        _db.Anchor.Execute("UPDATE checkins SET superseded = 1");

        (await Repo.HasAnyAsync(_goal.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Compensation_round_trips()
    {
        var seen = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var compensated = At(_goal, T0, 5) with { Compensation = new Compensation(Monday, seen) };
        var empty = At(_goal, T0.AddHours(1), 1) with { Compensation = new Compensation(Monday, []) };
        var normal = At(_goal, T0.AddHours(2), 1);
        foreach (var c in new[] { compensated, empty, normal })
            await Repo.AddAsync(c);

        var list = await Repo.ListAsync(T0, T0.AddDays(1));

        list[0].Compensation!.Period.Should().Be(Monday);
        list[0].Compensation!.Seen.Should().Equal(seen);
        list[1].Compensation!.Seen.Should().BeEmpty();
        list[2].Compensation.Should().BeNull();
        _db.Anchor.ExecuteScalar<string>("SELECT compensation_period FROM checkins WHERE id = @Id", new { Id = compensated.Id.ToString("D") })
            .Should().Be("2026-11-02");
    }

    [Fact]
    public async Task CompensationIds_lists_only_that_goal_and_period()
    {
        var mine = At(_goal, T0, 5) with { Compensation = new Compensation(Monday, []) };
        var superseded = At(_goal, T0.AddHours(1), 5) with { Compensation = new Compensation(Monday, []) };
        var otherPeriod = At(_goal, T0.AddHours(2), 5) with { Compensation = new Compensation(Monday.AddDays(7), []) };
        var otherGoal = At(_other, T0, 5) with { Compensation = new Compensation(Monday, []) };
        var normal = At(_goal, T0.AddHours(3), 1);
        foreach (var c in new[] { mine, superseded, otherPeriod, otherGoal, normal })
            await Repo.AddAsync(c);
        _db.Anchor.Execute("UPDATE checkins SET superseded = 1 WHERE id = @Id", new { Id = superseded.Id.ToString("D") });

        (await Repo.CompensationIdsAsync(_goal.Id, Monday)).Should().BeEquivalentTo([mine.Id, superseded.Id]);
    }
}
