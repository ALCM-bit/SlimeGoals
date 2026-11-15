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

    private static Checkin Chunk(Guid goal, int minutes, string? note) => new()
    {
        Id = Guid.CreateVersion7(T0), GoalId = goal, DoneAt = T0.AddMinutes(minutes), Amount = minutes, Note = note,
        UpdatedAt = T0.AddMinutes(minutes),
    };

    private long Count(string sql) => _db.Anchor.ExecuteScalar<long>(sql);

    [Fact]
    public async Task Task_round_trips()
    {
        await Repo.SaveAsync(new ActiveTimer(_a, T0, null, "Inglês"));

        (await Repo.GetAsync()).Should().Be(new ActiveTimer(_a, T0, null, "Inglês"));
    }

    [Fact]
    public async Task Set_task_updates_only_the_name()
    {
        var stopped = new ActiveTimer(_a, T0, T0.AddMinutes(5));
        await Repo.SaveAsync(stopped);

        (await Repo.SetTaskAsync("Ler")).Should().BeTrue();

        (await Repo.GetAsync()).Should().Be(stopped with { Task = "Ler" });
    }

    [Fact]
    public async Task Set_task_without_timer_returns_false_and_creates_nothing()
    {
        (await Repo.SetTaskAsync("Ler")).Should().BeFalse();

        (await Repo.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Switch_writes_the_chunk_and_the_new_start_together()
    {
        _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");
        await Repo.TryStartAsync(new ActiveTimer(_a, T0, null, "A"));
        var chunk = Chunk(_a, 25, "A");
        var next = new ActiveTimer(_a, T0.AddMinutes(25), null, "B");

        (await Repo.SwitchAsync(T0, next, chunk)).Should().BeTrue();

        (await Repo.GetAsync()).Should().Be(next);
        Count($"SELECT COUNT(*) FROM checkins WHERE id = '{chunk.Id:D}' AND amount = 25 AND note = 'A'").Should().Be(1);
        Count($"SELECT COUNT(*) FROM sync_outbox WHERE record_id = '{chunk.Id:D}' AND kind = 'checkin'").Should().Be(1);
    }

    [Theory]
    [InlineData("other-start")]
    [InlineData("stopped")]
    [InlineData("other-goal")]
    public async Task Switch_with_a_different_timer_returns_false_and_writes_nothing(string change)
    {
        var current = change switch
        {
            "other-start" => new ActiveTimer(_a, T0.AddSeconds(1), null, "A"),
            "stopped" => new ActiveTimer(_a, T0, T0.AddMinutes(25), "A"),
            _ => new ActiveTimer(_b, T0, null, "A"),
        };
        _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");
        await Repo.SaveAsync(current);

        (await Repo.SwitchAsync(T0, new ActiveTimer(_a, T0.AddMinutes(25), null, "B"), Chunk(_a, 25, "A"))).Should().BeFalse();

        (await Repo.GetAsync()).Should().Be(current);
        Count("SELECT COUNT(*) FROM checkins").Should().Be(0);
        Count("SELECT COUNT(*) FROM sync_outbox WHERE kind = 'checkin'").Should().Be(0);
    }

    [Fact]
    public async Task Switch_after_the_timer_was_cleared_does_not_bring_it_back()
    {
        await Repo.TryStartAsync(new ActiveTimer(_a, T0));
        await Repo.ClearAsync();

        (await Repo.SwitchAsync(T0, new ActiveTimer(_a, T0.AddMinutes(25), null, "B"), Chunk(_a, 25, null))).Should().BeFalse();

        (await Repo.GetAsync()).Should().BeNull();
        Count("SELECT COUNT(*) FROM checkins").Should().Be(0);
    }
}
