using Dapper;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class FocusSessionRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly FocusPlan Plan = new([new(FocusBlockKind.Focus, 50), new(FocusBlockKind.Break, 10)], 3);
    private readonly TestDatabase _db = new();
    private readonly Guid _goal;
    private FocusSessionRepository Repo => new(_db.Factory);
    private TimerRepository Timers => new(_db.Factory);

    public FocusSessionRepositoryTests()
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

    private FocusSession Session(int revision = 0) => new(Plan, _goal, T0, Step: 0, BlockStartedAt: T0, CutAt: T0,
        Carry: TimeSpan.Zero, PausedAt: null, Task: null, FinishedAt: null, AlertedStep: null, Revision: revision);

    private static FocusChunk Chunk(int minutes, Guid? checkinId = null) =>
        new(Guid.CreateVersion7(T0), minutes, "Capítulo 3", T0.AddMinutes(minutes), checkinId);

    private Checkin Record(int minutes) => new()
    {
        Id = Guid.CreateVersion7(T0), GoalId = _goal, DoneAt = T0.AddMinutes(minutes), Amount = minutes, Note = "Capítulo 3",
        UpdatedAt = T0.AddMinutes(minutes),
    };

    private long Count(string sql) => _db.Anchor.ExecuteScalar<long>(sql);

    [Fact]
    public async Task Session_round_trips_every_field()
    {
        // Instantes em fusos diferentes e um resto de minuto com milissegundos.
        var session = new FocusSession(Plan, _goal, StartedAt: new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.FromHours(-3)),
            Step: 3, BlockStartedAt: T0.AddMinutes(60), CutAt: T0.AddMinutes(61), Carry: TimeSpan.FromMilliseconds(42_517),
            PausedAt: T0.AddMinutes(70), Task: "Inglês", FinishedAt: T0.AddMinutes(80), AlertedStep: 2, Revision: 5);
        (await Repo.TryStartAsync(session)).Should().BeTrue();

        (await Repo.GetAsync()).Should().Be(session);

        var bare = Session() with { GoalId = null };
        await Repo.ClearAsync();
        (await Repo.TryStartAsync(bare)).Should().BeTrue();
        (await Repo.GetAsync()).Should().Be(bare);
    }

    [Fact]
    public async Task Chunks_round_trip_in_order_of_end()
    {
        await Repo.TryStartAsync(Session());
        var later = Chunk(30, Guid.CreateVersion7());
        var earlier = Chunk(25) with { Task = null };

        await Repo.UpdateAsync(0, Session(), later, checkin: null);
        await Repo.UpdateAsync(1, Session(), earlier, checkin: null);

        (await Repo.ChunksAsync()).Should().Equal(earlier, later);
    }

    [Fact]
    public async Task Start_is_refused_with_a_timer_and_timer_is_refused_with_a_session()
    {
        await Timers.TryStartAsync(new ActiveTimer(_goal, T0));
        (await Repo.TryStartAsync(Session())).Should().BeFalse();
        (await Repo.GetAsync()).Should().BeNull();

        await Timers.ClearAsync();
        (await Repo.TryStartAsync(Session())).Should().BeTrue();
        (await Repo.TryStartAsync(Session() with { Task = "outra" })).Should().BeFalse();
        (await Timers.TryStartAsync(new ActiveTimer(_goal, T0))).Should().BeFalse();

        (await Timers.GetAsync()).Should().BeNull();
        (await Repo.GetAsync()).Should().Be(Session());
    }

    [Fact]
    public async Task Update_with_a_stale_revision_writes_nothing()
    {
        _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");
        await Repo.TryStartAsync(Session(revision: 3));
        var next = Session() with { Step = 1, CutAt = T0.AddMinutes(50) };

        (await Repo.UpdateAsync(2, next, Chunk(50), Record(50))).Should().BeFalse();
        (await Repo.UpdateAsync(2, next: null, Chunk(50), Record(50))).Should().BeFalse();

        (await Repo.GetAsync()).Should().Be(Session(revision: 3));
        (await Repo.ChunksAsync()).Should().BeEmpty();
        Count("SELECT COUNT(*) FROM checkins").Should().Be(0);
        Count("SELECT COUNT(*) FROM sync_outbox").Should().Be(0);
    }

    [Fact]
    public async Task Update_without_a_session_returns_false_and_creates_nothing()
    {
        (await Repo.UpdateAsync(0, Session(), Chunk(50), Record(50))).Should().BeFalse();

        (await Repo.GetAsync()).Should().BeNull();
        (await Repo.ChunksAsync()).Should().BeEmpty();
        Count("SELECT COUNT(*) FROM checkins").Should().Be(0);
    }

    [Fact]
    public async Task Update_writes_checkin_chunk_and_session_together_and_enqueues_the_checkin()
    {
        _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");
        await Repo.TryStartAsync(Session(revision: 3));
        var checkin = Record(50);
        var chunk = Chunk(50, checkin.Id);
        // A revisão de quem grava não conta: a gravada passa a ser a esperada + 1.
        var next = Session(revision: 99) with { Step = 1, BlockStartedAt = T0.AddMinutes(50), CutAt = T0.AddMinutes(50) };

        (await Repo.UpdateAsync(3, next, chunk, checkin)).Should().BeTrue();

        (await Repo.GetAsync()).Should().Be(next with { Revision = 4 });
        (await Repo.ChunksAsync()).Should().Equal(chunk);
        Count($"SELECT COUNT(*) FROM checkins WHERE id = '{checkin.Id:D}' AND amount = 50 AND note = 'Capítulo 3'").Should().Be(1);
        Count($"SELECT COUNT(*) FROM sync_outbox WHERE record_id = '{checkin.Id:D}' AND kind = 'checkin'").Should().Be(1);
        Count("SELECT COUNT(*) FROM sync_outbox WHERE kind NOT IN ('goal', 'checkin')").Should().Be(0);
    }

    [Fact]
    public async Task Update_with_a_missing_goal_throws_and_writes_nothing()
    {
        await Repo.TryStartAsync(Session());
        var orphan = Record(50) with { GoalId = Guid.CreateVersion7() };

        var act = () => Repo.UpdateAsync(0, Session() with { Step = 1 }, Chunk(50, orphan.Id), orphan);

        await act.Should().ThrowAsync<GoalNotFoundException>();
        (await Repo.GetAsync()).Should().Be(Session());
        (await Repo.ChunksAsync()).Should().BeEmpty();
        Count("SELECT COUNT(*) FROM checkins").Should().Be(0);
    }

    [Fact]
    public async Task Update_to_null_deletes_session_and_chunks()
    {
        await Repo.TryStartAsync(Session());
        await Repo.UpdateAsync(0, Session(), Chunk(25), checkin: null);
        var checkin = Record(50);

        (await Repo.UpdateAsync(1, next: null, Chunk(50, checkin.Id), checkin)).Should().BeTrue();

        (await Repo.GetAsync()).Should().BeNull();
        (await Repo.ChunksAsync()).Should().BeEmpty();
        Count($"SELECT COUNT(*) FROM checkins WHERE id = '{checkin.Id:D}'").Should().Be(1);
    }

    [Fact]
    public async Task Update_keeps_the_alert_mark_when_the_step_does_not_change()
    {
        await Repo.TryStartAsync(Session());
        (await Repo.MarkAlertedAsync(0)).Should().BeTrue();

        // Quem leu a sessão antes do aviso grava o mesmo passo sem a marca: o aviso não toca de novo.
        (await Repo.UpdateAsync(0, Session() with { Task = "Ler" }, chunk: null, checkin: null)).Should().BeTrue();
        (await Repo.GetAsync())!.AlertedStep.Should().Be(0);

        // Num passo novo vale a marca de quem grava.
        (await Repo.UpdateAsync(1, Session() with { Step = 1 }, chunk: null, checkin: null)).Should().BeTrue();
        (await Repo.GetAsync())!.AlertedStep.Should().BeNull();
    }

    [Fact]
    public async Task Assign_chunk_is_done_once()
    {
        _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");
        await Repo.TryStartAsync(Session() with { GoalId = null });
        var chunk = Chunk(25);
        await Repo.UpdateAsync(0, Session() with { GoalId = null }, chunk, checkin: null);
        var first = Record(25);

        (await Repo.AssignChunkAsync(chunk.Id, first)).Should().BeTrue();
        (await Repo.AssignChunkAsync(chunk.Id, Record(25))).Should().BeFalse();
        (await Repo.AssignChunkAsync(Guid.CreateVersion7(), Record(25))).Should().BeFalse();

        (await Repo.ChunksAsync()).Should().Equal(chunk with { CheckinId = first.Id });
        Count("SELECT COUNT(*) FROM checkins").Should().Be(1);
        Count($"SELECT COUNT(*) FROM sync_outbox WHERE record_id = '{first.Id:D}' AND kind = 'checkin'").Should().Be(1);
    }

    [Fact]
    public async Task Assign_chunk_with_a_missing_goal_throws_and_leaves_the_chunk_free()
    {
        await Repo.TryStartAsync(Session() with { GoalId = null });
        var chunk = Chunk(25);
        await Repo.UpdateAsync(0, Session() with { GoalId = null }, chunk, checkin: null);

        var act = () => Repo.AssignChunkAsync(chunk.Id, Record(25) with { GoalId = Guid.CreateVersion7() });

        await act.Should().ThrowAsync<GoalNotFoundException>();
        (await Repo.ChunksAsync()).Should().Equal(chunk);
    }

    [Theory]
    [InlineData("plan", "'nada'")]
    [InlineData("plan", """'{"blocks":[],"repeat":1}'""")]
    [InlineData("started_at", "'ontem'")]
    [InlineData("goal_id", "'meta'")]
    [InlineData("step", "6")]
    [InlineData("carry_ms", "'muito'")]
    public async Task Invalid_session_row_is_discarded(string column, string value)
    {
        await Repo.TryStartAsync(Session());
        await Repo.UpdateAsync(0, Session(), Chunk(25), checkin: null);
        _db.Anchor.Execute($"UPDATE focus_session SET {column} = {value}");

        (await Repo.GetAsync()).Should().BeNull();

        (await Repo.ChunksAsync()).Should().BeEmpty();
        (await Timers.TryStartAsync(new ActiveTimer(_goal, T0))).Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_chunk_row_is_skipped()
    {
        await Repo.TryStartAsync(Session());
        var good = Chunk(25);
        await Repo.UpdateAsync(0, Session(), good, checkin: null);
        await Repo.UpdateAsync(1, Session(), Chunk(30), checkin: null);
        _db.Anchor.Execute("UPDATE focus_chunk SET ended_at = 'ontem' WHERE minutes = 30");

        (await Repo.ChunksAsync()).Should().Equal(good);
    }

    [Fact]
    public async Task Mark_alerted_only_for_the_current_step()
    {
        (await Repo.MarkAlertedAsync(0)).Should().BeFalse();
        await Repo.TryStartAsync(Session(revision: 2) with { Step = 1 });

        (await Repo.MarkAlertedAsync(0)).Should().BeFalse();
        (await Repo.GetAsync())!.AlertedStep.Should().BeNull();

        (await Repo.MarkAlertedAsync(1)).Should().BeTrue();
        (await Repo.GetAsync()).Should().Be(Session(revision: 2) with { Step = 1, AlertedStep = 1 });
    }

    [Fact]
    public async Task Start_after_clear_begins_without_old_chunks()
    {
        await Repo.TryStartAsync(Session());
        await Repo.UpdateAsync(0, Session(), Chunk(25), checkin: null);

        await Repo.ClearAsync();

        (await Repo.GetAsync()).Should().BeNull();
        (await Repo.ChunksAsync()).Should().BeEmpty();
        (await Repo.TryStartAsync(Session())).Should().BeTrue();
        (await Repo.ChunksAsync()).Should().BeEmpty();
    }
}
