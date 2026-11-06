using Dapper;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class TimerRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private readonly Guid _a;
    private readonly Guid _b;
    private TimerRepository Repo => new(_db.Factory);

    public TimerRepositoryTests()
    {
        var goals = new GoalRepository(_db.Factory);
        Goal New() => new()
        {
            Id = Guid.CreateVersion7(), Title = "Estudar", Recurrence = Recurrence.Daily, Target = 60, Icon = "book",
            Color = GoalColor.Purple, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
            ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = T0, Measure = Measure.Duration, Unit = null,
        };
        var a = New(); var b = New();
        goals.AddAsync(a).GetAwaiter().GetResult();
        goals.AddAsync(b).GetAwaiter().GetResult();
        (_a, _b) = (a.Id, b.Id);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Timer_try_start_twice_returns_false_save_replaces_and_clear()
    {
        (await Repo.GetAsync()).Should().BeNull();

        (await Repo.TryStartAsync(new ActiveTimer(_a, T0))).Should().BeTrue();
        (await Repo.TryStartAsync(new ActiveTimer(_b, T0.AddMinutes(1)))).Should().BeFalse();
        (await Repo.GetAsync()).Should().Be(new ActiveTimer(_a, T0));

        var stopped = new ActiveTimer(_a, T0, new DateTimeOffset(2026, 11, 2, 9, 25, 40, TimeSpan.FromHours(-3)));
        await Repo.SaveAsync(stopped);
        (await Repo.GetAsync()).Should().Be(stopped);

        await Repo.ClearAsync();
        (await Repo.GetAsync()).Should().BeNull();
        (await Repo.TryStartAsync(new ActiveTimer(_b, T0))).Should().BeTrue();
    }

    [Theory]
    [InlineData("started_at", "'ontem'")]
    [InlineData("stopped_at", "'nunca'")]
    [InlineData("started_at", "42")]
    public async Task Unreadable_row_counts_as_no_timer_and_frees_the_slot(string column, string value)
    {
        await Repo.TryStartAsync(new ActiveTimer(_a, T0));
        _db.Anchor.Execute($"UPDATE active_timer SET {column} = {value}");

        (await Repo.GetAsync()).Should().BeNull();

        (await Repo.TryStartAsync(new ActiveTimer(_b, T0))).Should().BeTrue();
    }
}
