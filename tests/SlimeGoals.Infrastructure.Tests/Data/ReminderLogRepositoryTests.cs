using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class ReminderLogRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private readonly Guid _goal;
    private ReminderLogRepository Repo => new(_db.Factory);

    public ReminderLogRepositoryTests()
    {
        var goal = new Goal
        {
            Id = Guid.CreateVersion7(), Title = "Estudar", Recurrence = Recurrence.Daily, Target = 60, Icon = "book",
            Color = GoalColor.Purple, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
            ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = T0, Measure = Measure.Duration, Unit = null,
        };
        new GoalRepository(_db.Factory).AddAsync(goal).GetAwaiter().GetResult();
        _goal = goal.Id;
    }

    public void Dispose() => _db.Dispose();

    private static ReminderLogEntry Entry(Guid goalId, DateTimeOffset firedAt, ReminderResponse? response = null,
        DateTimeOffset? respondedAt = null) =>
        new(Guid.CreateVersion7(), goalId, firedAt, response, respondedAt);

    [Theory]
    [InlineData("id = 'não é um guid'")]
    [InlineData("fired_at = '2026-11-02T12:30:00 lixo'")]
    [InlineData("responded_at = 'agora'")]
    [InlineData("response = 'later'")]
    public async Task List_skips_invalid_row_in_any_position_and_keeps_the_others(string assignment)
    {
        foreach (var position in Enumerable.Range(0, 3))
        {
            _db.Anchor.Execute("DELETE FROM reminder_log");
            var entries = Enumerable.Range(0, 3).Select(i => Entry(_goal, T0.AddHours(i))).ToList();
            foreach (var entry in entries)
                await Repo.AddAsync(entry);
            var bad = entries[position];
            _db.Anchor.Execute($"UPDATE reminder_log SET {assignment} WHERE id = @Id", new { Id = bad.Id.ToString("D") });
            using var trace = new TraceCapture();

            var listed = await Repo.ListAsync(T0.AddDays(-1), T0.AddDays(1));

            listed.Select(e => e.Id).Should().Equal(entries.Where(e => e != bad).Select(e => e.Id));
            trace.Messages.Should().Contain(m => m.Contains("reminder_log"));
        }
    }

    [Theory]
    [InlineData("id = 'não é um guid'")]
    [InlineData("fired_at = 'agora'")]
    [InlineData("responded_at = 'agora'")]
    public async Task List_pending_skips_invalid_row_and_keeps_the_others(string assignment)
    {
        var bad = Entry(_goal, T0);
        var good = Entry(_goal, T0.AddHours(1));
        await Repo.AddAsync(bad);
        await Repo.AddAsync(good);
        _db.Anchor.Execute($"UPDATE reminder_log SET {assignment} WHERE id = @Id", new { Id = bad.Id.ToString("D") });

        var pending = await Repo.ListPendingAsync();

        pending.Should().ContainSingle().Which.Id.Should().Be(good.Id);
    }

    [Fact]
    public async Task Add_then_get_round_trips_utc_instants()
    {
        var local = new DateTimeOffset(2026, 11, 2, 9, 0, 0, 250, TimeSpan.FromHours(-3));
        var entry = Entry(_goal, local);
        await Repo.AddAsync(entry);

        var found = await Repo.GetAsync(entry.Id);
        found.Should().Be(entry);
        found!.FiredAt.Offset.Should().Be(TimeSpan.Zero);

        var responded = new DateTimeOffset(2026, 11, 2, 12, 5, 0, TimeSpan.Zero);
        await Repo.RespondAsync(entry.Id, ReminderResponse.Done, responded);
        var answered = await Repo.GetAsync(entry.Id);
        answered!.RespondedAt.Should().Be(responded);
        answered.RespondedAt!.Value.Offset.Should().Be(TimeSpan.Zero);

        (await Repo.GetAsync(Guid.CreateVersion7())).Should().BeNull();
    }

    [Fact]
    public async Task Respond_only_once()
    {
        var entry = Entry(_goal, T0);
        await Repo.AddAsync(entry);

        (await Repo.RespondAsync(entry.Id, ReminderResponse.Snooze, T0.AddMinutes(1))).Should().BeTrue();
        (await Repo.RespondAsync(entry.Id, ReminderResponse.Done, T0.AddMinutes(2))).Should().BeFalse();
        (await Repo.RespondAsync(Guid.CreateVersion7(), ReminderResponse.Done, T0)).Should().BeFalse();

        var stored = await Repo.GetAsync(entry.Id);
        stored.Should().Be(entry with { Response = ReminderResponse.Snooze, RespondedAt = T0.AddMinutes(1) });
    }

    [Fact]
    public async Task List_filters_by_fired_at_range_and_orders()
    {
        var late = Entry(_goal, T0.AddHours(2));
        var early = Entry(_goal, T0.AddMinutes(30));
        var atStart = Entry(_goal, T0);
        var atEnd = Entry(_goal, T0.AddHours(3));
        foreach (var e in new[] { late, atEnd, early, atStart })
            await Repo.AddAsync(e);

        var list = await Repo.ListAsync(T0, T0.AddHours(3));

        list.Should().Equal(atStart, early, late);
    }

    [Fact]
    public async Task List_pending_returns_only_null_responses()
    {
        var pendingLate = Entry(_goal, T0.AddHours(1));
        var pendingEarly = Entry(_goal, T0);
        var answered = Entry(_goal, T0.AddMinutes(10), ReminderResponse.Ignore, T0.AddMinutes(11));
        foreach (var e in new[] { pendingLate, answered, pendingEarly })
            await Repo.AddAsync(e);

        var pending = await Repo.ListPendingAsync();

        pending.Should().Equal(pendingEarly, pendingLate);
    }

    [Fact]
    public async Task Response_text_matches_spec_values()
    {
        var entry = Entry(_goal, T0);
        await Repo.AddAsync(entry);
        await Repo.RespondAsync(entry.Id, ReminderResponse.Snooze, T0.AddMinutes(1));

        var raw = _db.Anchor.ExecuteScalar<string>("SELECT response FROM reminder_log WHERE id = @Id", new { Id = entry.Id.ToString("D") });

        raw.Should().Be("snooze");
    }

    [Theory]
    [InlineData(ReminderResponse.Done, "done")]
    [InlineData(ReminderResponse.Ignore, "ignore")]
    public async Task Each_response_is_stored_with_its_spec_text(ReminderResponse response, string text)
    {
        var entry = Entry(_goal, T0);
        await Repo.AddAsync(entry);
        await Repo.RespondAsync(entry.Id, response, T0.AddMinutes(1));

        _db.Anchor.ExecuteScalar<string>("SELECT response FROM reminder_log WHERE id = @Id", new { Id = entry.Id.ToString("D") })
            .Should().Be(text);
    }

    [Fact]
    public async Task Unknown_response_text_in_database_throws_format_exception()
    {
        var entry = Entry(_goal, T0);
        await Repo.AddAsync(entry);
        _db.Anchor.Execute("UPDATE reminder_log SET response = 'later', responded_at = '2026-11-02T12:01:00.0000000+00:00' WHERE id = @Id",
            new { Id = entry.Id.ToString("D") });

        var act = () => Repo.GetAsync(entry.Id);

        await act.Should().ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task Goal_foreign_key_is_enforced()
    {
        var orphan = Entry(Guid.CreateVersion7(), T0);

        var act = () => Repo.AddAsync(orphan);

        await act.Should().ThrowAsync<SqliteException>().Where(e => e.Message.Contains("FOREIGN KEY"));
    }
}
