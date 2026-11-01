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
}
