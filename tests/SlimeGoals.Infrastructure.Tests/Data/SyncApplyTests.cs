using AwesomeAssertions;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Testing;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class SyncApplyTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Period = new(2026, 11, 2);
    private static readonly Guid Me = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private readonly TestDatabase _db = new();
    private SyncLocalStore Store => new(_db.Factory);
    private GoalRepository Goals => new(_db.Factory);
    private CheckinRepository Checkins => new(_db.Factory);

    public SyncApplyTests() => _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");

    public void Dispose() => _db.Dispose();

    private sealed record QueueLine(string Op, long BaseSeq, string Kind);

    private sealed record RecordLine(long Seq, string RecordKey, string Kind, long Confirmed);

    private sealed record PendingLine(string GoalId, long Seq, string Payload);

    private static Goal NewGoal() => new()
    {
        Id = Guid.CreateVersion7(), Title = "Água", Recurrence = Recurrence.Daily, Target = 8, Icon = "water",
        Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = T0, Unit = "copos",
    };

    private static Checkin NewCheckin(Guid goalId, int amount = 2, DateTimeOffset? doneAt = null, Compensation? compensation = null) => new()
    {
        Id = Guid.CreateVersion7(), GoalId = goalId, DoneAt = doneAt ?? T0, Amount = amount, UpdatedAt = T0, Compensation = compensation,
    };

    private static Compensation Compensates(params Guid[] seen) => new(Period, seen);

    private static string Key(Guid id) => "k:" + id;

    private static IncomingRecord In(Goal goal, long seq, Guid? device = null) => new(Key(goal.Id), seq,
        new SyncEnvelope(SyncCodec.FormatVersion, SyncRecordKind.Goal, goal.Id, device ?? Other, SyncCodec.Encode(goal)));

    private static IncomingRecord In(Checkin checkin, long seq, Guid? device = null) => new(Key(checkin.Id), seq,
        new SyncEnvelope(SyncCodec.FormatVersion, SyncRecordKind.Checkin, checkin.Id, device ?? Other, SyncCodec.Encode(checkin)));

    private static IncomingRecord Unreadable(Guid id, SyncRecordKind kind, long seq) => new(Key(id), seq,
        new SyncEnvelope(SyncCodec.FormatVersion, kind, id, Other, "{quebrado"));

    private static IncomingRecord Deleted(Guid id, long seq) => new(Key(id), seq, null);

    private static IncomingRecord Sealed(Guid id, long seq) => new(Key(id), seq, null, Unreadable: true);

    private Task RememberAsync(params Guid[] ids) => Store.RememberKeysAsync(ids.Select(id => new OutboxKey(id, Key(id))).ToList());

    private Task<IReadOnlyList<Guid>> ApplyAsync(long lastSeq, params IncomingRecord[] records) => Store.ApplyPageAsync(records, lastSeq, Me);

    private QueueLine? QueueRow(Guid id) => _db.Anchor.QuerySingleOrDefault<QueueLine>(
        "SELECT op AS Op, base_seq AS BaseSeq, kind AS Kind FROM sync_outbox WHERE record_id = @Id", new { Id = id.ToString("D") });

    private RecordLine? Record(Guid id) => _db.Anchor.QuerySingleOrDefault<RecordLine>(
        "SELECT seq AS Seq, record_key AS RecordKey, kind AS Kind, confirmed AS Confirmed FROM sync_records WHERE record_id = @Id",
        new { Id = id.ToString("D") });

    private long Count(string table) => _db.Anchor.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}");

    private long LastSeq => _db.Anchor.ExecuteScalar<long>("SELECT last_seq FROM sync_state");

    private bool RowExists(string table, Guid id) => _db.Anchor.ExecuteScalar<long>(
        $"SELECT COUNT(*) FROM {table} WHERE id = @Id", new { Id = id.ToString("D") }) == 1;

    private bool IsSuperseded(Guid id) => _db.Anchor.ExecuteScalar<long>(
        "SELECT superseded FROM checkins WHERE id = @Id", new { Id = id.ToString("D") }) == 1;

    private Task<int> DayTotalAsync(Guid goalId) => Checkins.SumAsync(goalId, T0.AddDays(-1), T0.AddDays(1));

    private void StartTimer(Guid goalId) => _db.Anchor.Execute(
        "INSERT INTO active_timer (id, goal_id, started_at) VALUES (1, @Id, '2026-11-02T12:00:00.0000000+00:00')",
        new { Id = goalId.ToString("D") });

    private void LogReminder(Guid goalId) => _db.Anchor.Execute(
        "INSERT INTO reminder_log (id, goal_id, fired_at) VALUES (@Log, @Id, '2026-11-02T12:00:00.0000000+00:00')",
        new { Log = Guid.NewGuid().ToString("D"), Id = goalId.ToString("D") });

    // Metas

    [Fact]
    public async Task New_goal_is_inserted_without_queue()
    {
        var goal = NewGoal();

        var deleted = await ApplyAsync(1, In(goal, 1));

        deleted.Should().BeEmpty();
        (await Goals.GetAsync(goal.Id)).Should().BeEquivalentTo(goal);
        Count("sync_outbox").Should().Be(0);
        Record(goal.Id).Should().Be(new RecordLine(1, Key(goal.Id), "goal", 1));
    }

    [Fact]
    public async Task Goal_without_pending_edit_is_replaced()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        var older = goal with { Title = "Antiga", UpdatedAt = T0.AddDays(-3) };

        await ApplyAsync(2, In(older, 2));

        (await Goals.GetAsync(goal.Id)).Should().BeEquivalentTo(older);
        Record(goal.Id)!.Seq.Should().Be(2);
        Count("sync_outbox").Should().Be(0);
    }

    [Fact]
    public async Task Goal_replacement_keeps_what_hangs_from_it()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));
        StartTimer(goal.Id);
        LogReminder(goal.Id);

        await ApplyAsync(3, In(goal with { Title = "Nova" }, 3));

        RowExists("checkins", checkin.Id).Should().BeTrue();
        Count("active_timer").Should().Be(1);
        Count("reminder_log").Should().Be(1);
    }

    [Fact]
    public async Task Pending_local_wins_keeps_queue_and_updates_base_seq()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        var local = goal with { Title = "Local", UpdatedAt = T0.AddMinutes(5) };
        await Goals.UpdateAsync(local);

        await ApplyAsync(2, In(goal with { Title = "Remota", UpdatedAt = T0.AddMinutes(1) }, 2));

        (await Goals.GetAsync(goal.Id))!.Title.Should().Be("Local");
        QueueRow(goal.Id).Should().Be(new QueueLine("upsert", 2, "goal"));
        Record(goal.Id)!.Seq.Should().Be(2);
    }

    [Fact]
    public async Task Pending_local_loses_drops_queue_and_applies_remote()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        await Goals.UpdateAsync(goal with { Title = "Local", UpdatedAt = T0.AddMinutes(1) });

        await ApplyAsync(2, In(goal with { Title = "Remota", UpdatedAt = T0.AddMinutes(5) }, 2));

        (await Goals.GetAsync(goal.Id))!.Title.Should().Be("Remota");
        QueueRow(goal.Id).Should().BeNull();
        Record(goal.Id)!.Seq.Should().Be(2);
    }

    [Fact]
    public async Task Own_version_coming_back_drops_queue()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);

        await ApplyAsync(4, In(goal, 4, Me));

        QueueRow(goal.Id).Should().BeNull();
        Record(goal.Id)!.Seq.Should().Be(4);
        (await Goals.GetAsync(goal.Id)).Should().BeEquivalentTo(goal);
    }

    // Registros

    [Fact]
    public async Task Checkin_is_inserted_once()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(1, In(goal, 1));

        await ApplyAsync(2, In(checkin, 2));
        await ApplyAsync(2, In(checkin, 2));

        Count("checkins").Should().Be(1);
        Count("sync_outbox").Should().Be(0);
        Record(checkin.Id).Should().Be(new RecordLine(2, Key(checkin.Id), "checkin", 1));
    }

    [Fact]
    public async Task Checkin_above_MaxAmount_is_accepted()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id, amount: 5_000_000);

        await ApplyAsync(2, In(goal, 1), In(checkin, 2));

        (await DayTotalAsync(goal.Id)).Should().Be(5_000_000);
    }

    [Fact]
    public async Task Own_checkin_coming_back_drops_queue()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        var checkin = NewCheckin(goal.Id);
        await Checkins.AddAsync(checkin);

        await ApplyAsync(2, In(checkin, 2, Me));

        Count("checkins").Should().Be(1);
        QueueRow(checkin.Id).Should().BeNull();
        Record(checkin.Id)!.Seq.Should().Be(2);
    }

    [Fact]
    public async Task Checkin_of_unknown_goal_goes_to_pending()
    {
        var checkin = NewCheckin(Guid.CreateVersion7());

        await ApplyAsync(5, In(checkin, 5));

        Count("checkins").Should().Be(0);
        _db.Anchor.QuerySingle<PendingLine>("SELECT goal_id AS GoalId, seq AS Seq, payload AS Payload FROM sync_pending")
            .Should().Be(new PendingLine(checkin.GoalId.ToString("D"), 5, SyncCodec.Encode(checkin)));
        Record(checkin.Id).Should().Be(new RecordLine(5, Key(checkin.Id), "checkin", 1));
        Count("sync_outbox").Should().Be(0);
    }

    [Fact]
    public async Task Pending_is_applied_when_goal_arrives()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id, amount: 3);
        await ApplyAsync(5, In(checkin, 5));

        await ApplyAsync(6, In(goal, 6));

        (await DayTotalAsync(goal.Id)).Should().Be(3);
        Count("sync_pending").Should().Be(0);
        Count("sync_outbox").Should().Be(0);
        Record(checkin.Id)!.Seq.Should().Be(5);
    }

    [Fact]
    public async Task Goals_are_applied_before_checkins_in_the_page()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);

        await ApplyAsync(2, In(checkin, 1), In(goal, 2));

        RowExists("checkins", checkin.Id).Should().BeTrue();
        Count("sync_pending").Should().Be(0);
    }

    // Exclusão

    [Fact]
    public async Task Goal_deletion_removes_rows_queue_records_pending_and_adds_tombstone()
    {
        var goal = NewGoal();
        var other = NewGoal();
        var checkin = NewCheckin(goal.Id);
        var kept = NewCheckin(other.Id);
        await ApplyAsync(4, In(goal, 1), In(checkin, 2), In(other, 3), In(kept, 4));
        var queued = NewCheckin(goal.Id);
        await Checkins.AddAsync(queued);
        var waiting = NewCheckin(goal.Id);
        _db.Anchor.Execute("INSERT INTO sync_pending (record_id, goal_id, seq, payload) VALUES (@R, @G, 3, '{}')",
            new { R = waiting.Id.ToString("D"), G = goal.Id.ToString("D") });
        _db.Anchor.Execute("INSERT INTO sync_records (record_id, kind, seq, record_key) VALUES (@R, 'checkin', 3, @K)",
            new { R = waiting.Id.ToString("D"), K = Key(waiting.Id) });
        await Goals.UpdateAsync(goal with { Title = "Editada" });
        StartTimer(goal.Id);
        LogReminder(goal.Id);

        var deleted = await ApplyAsync(9, Deleted(goal.Id, 9));

        deleted.Should().Equal(goal.Id);
        RowExists("goals", goal.Id).Should().BeFalse();
        RowExists("goals", other.Id).Should().BeTrue();
        RowExists("checkins", kept.Id).Should().BeTrue();
        Count("checkins").Should().Be(1);
        Count("active_timer").Should().Be(0);
        Count("reminder_log").Should().Be(0);
        Count("sync_outbox").Should().Be(0);
        Count("sync_pending").Should().Be(0);
        Record(goal.Id).Should().BeNull();
        Record(checkin.Id).Should().BeNull();
        Record(waiting.Id).Should().BeNull();
        Record(other.Id).Should().NotBeNull();
        _db.Anchor.ExecuteScalar<string>("SELECT goal_id FROM sync_tombstones").Should().Be(goal.Id.ToString("D"));
    }

    [Fact]
    public async Task Deletion_beats_pending_edit()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        await Goals.UpdateAsync(goal with { Title = "Local", UpdatedAt = T0.AddYears(1) });

        await ApplyAsync(2, Deleted(goal.Id, 2));

        RowExists("goals", goal.Id).Should().BeFalse();
        Count("sync_outbox").Should().Be(0);
    }

    [Fact]
    public async Task Unknown_key_deletion_is_ignored()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));

        var deleted = await ApplyAsync(2, Deleted(Guid.CreateVersion7(), 2));

        deleted.Should().BeEmpty();
        Count("goals").Should().Be(1);
        Count("sync_tombstones").Should().Be(0);
        LastSeq.Should().Be(2);
    }

    [Fact]
    public async Task Checkin_deletion_removes_row_or_pending()
    {
        var goal = NewGoal();
        var row = NewCheckin(goal.Id);
        var waiting = NewCheckin(Guid.CreateVersion7());
        await ApplyAsync(3, In(goal, 1), In(row, 2), In(waiting, 3));

        var deleted = await ApplyAsync(5, Deleted(row.Id, 4), Deleted(waiting.Id, 5));

        deleted.Should().BeEmpty();
        Count("checkins").Should().Be(0);
        Count("sync_pending").Should().Be(0);
        Record(row.Id).Should().BeNull();
        Record(waiting.Id).Should().BeNull();
        Count("goals").Should().Be(1);
    }

    [Fact]
    public async Task Checkin_deletion_takes_it_out_of_the_queue()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));
        _db.Anchor.Execute("INSERT INTO sync_outbox (record_id, kind, op, base_seq) VALUES (@Id, 'checkin', 'upsert', 2)",
            new { Id = checkin.Id.ToString("D") });

        await ApplyAsync(3, Deleted(checkin.Id, 3));

        QueueRow(checkin.Id).Should().BeNull();
    }

    [Fact]
    public async Task Accepted_elsewhere_deletion_of_queued_delete_clears_it()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));
        await Goals.DeleteAsync(goal.Id);
        QueueRow(goal.Id)!.Op.Should().Be("delete");
        QueueRow(checkin.Id)!.Op.Should().Be("delete");

        var deleted = await ApplyAsync(4, Deleted(goal.Id, 3), Deleted(checkin.Id, 4));

        deleted.Should().Equal(goal.Id);
        Count("sync_outbox").Should().Be(0);
        Count("sync_records").Should().Be(0);
        Count("sync_tombstones").Should().Be(1);
    }

    [Fact]
    public async Task Checkin_deletion_after_its_goal_in_the_page_is_ignored()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));

        var deleted = await ApplyAsync(4, Deleted(checkin.Id, 3), Deleted(goal.Id, 4));

        deleted.Should().Equal(goal.Id);
        Count("sync_records").Should().Be(0);
        Count("sync_outbox").Should().Be(0);
    }

    // Marcas de exclusão

    [Fact]
    public async Task Upsert_of_tombstoned_goal_is_discarded_and_queues_delete()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        _db.Anchor.Execute("INSERT INTO sync_tombstones (goal_id) VALUES (@Id)", new { Id = goal.Id.ToString("D") });

        var deleted = await ApplyAsync(8, In(goal, 7), In(checkin, 8));

        deleted.Should().BeEmpty();
        Count("goals").Should().Be(0);
        Count("checkins").Should().Be(0);
        Count("sync_pending").Should().Be(0);
        QueueRow(goal.Id).Should().Be(new QueueLine("delete", 7, "goal"));
        QueueRow(checkin.Id).Should().Be(new QueueLine("delete", 8, "checkin"));
        Record(goal.Id).Should().Be(new RecordLine(7, Key(goal.Id), "goal", 1));
        Record(checkin.Id).Should().Be(new RecordLine(8, Key(checkin.Id), "checkin", 1));
    }

    [Fact]
    public async Task Tombstoned_upsert_reuses_the_queue_row()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));
        await Goals.DeleteAsync(goal.Id);
        _db.Anchor.Execute("UPDATE sync_outbox SET op = 'upsert' WHERE record_id = @Id", new { Id = checkin.Id.ToString("D") });

        await ApplyAsync(6, In(goal with { Title = "Atrasada" }, 5), In(checkin, 6));

        Count("sync_outbox").Should().Be(2);
        QueueRow(goal.Id).Should().Be(new QueueLine("delete", 5, "goal"));
        QueueRow(checkin.Id).Should().Be(new QueueLine("delete", 6, "checkin"));
        Count("goals").Should().Be(0);
    }

    // Página

    [Fact]
    public async Task Page_advances_last_seq_in_same_commit()
    {
        await ApplyAsync(7, In(NewGoal(), 3));

        LastSeq.Should().Be(7);
        (await new SyncStateRepository(_db.Factory).GetAsync()).LastSeq.Should().Be(7);
    }

    [Fact]
    public async Task Failure_rolls_back_everything()
    {
        var goal = NewGoal();
        var good = NewCheckin(goal.Id);
        var bad = NewCheckin(goal.Id);
        _db.Anchor.Execute($"""
            CREATE TRIGGER fail_page BEFORE INSERT ON sync_records WHEN NEW.record_id = '{bad.Id:D}'
            BEGIN SELECT RAISE(ABORT, 'falha de teste'); END
            """);

        var apply = () => ApplyAsync(3, In(goal, 1), In(good, 2), In(bad, 3));

        await apply.Should().ThrowAsync<SqliteException>();
        Count("goals").Should().Be(0);
        Count("checkins").Should().Be(0);
        Count("sync_records").Should().Be(0);
        LastSeq.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_payload_is_traced_and_skipped()
    {
        var badGoal = Guid.CreateVersion7();
        var badCheckin = Guid.CreateVersion7();
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        using var trace = new TraceCapture();

        await ApplyAsync(4, Unreadable(badGoal, SyncRecordKind.Goal, 1), In(goal, 2),
            Unreadable(badCheckin, SyncRecordKind.Checkin, 3), In(checkin, 4));

        trace.Messages.Should().Contain(m => m.Contains(badGoal.ToString("D")));
        trace.Messages.Should().Contain(m => m.Contains(badCheckin.ToString("D")));
        RowExists("goals", goal.Id).Should().BeTrue();
        RowExists("checkins", checkin.Id).Should().BeTrue();
        Record(badGoal).Should().BeNull();
        Record(badCheckin).Should().BeNull();
        LastSeq.Should().Be(4);
    }

    [Fact]
    public async Task Payload_of_another_record_is_invalid()
    {
        var goal = NewGoal();
        var claimed = Guid.CreateVersion7();
        var record = new IncomingRecord(Key(claimed), 1,
            new SyncEnvelope(SyncCodec.FormatVersion, SyncRecordKind.Goal, claimed, Other, SyncCodec.Encode(goal)));
        using var trace = new TraceCapture();

        await ApplyAsync(1, record);

        Count("goals").Should().Be(0);
        trace.Messages.Should().Contain(m => m.Contains(claimed.ToString("D")));
        LastSeq.Should().Be(1);
    }

    [Fact]
    public async Task Goal_refused_by_the_table_is_traced_and_skipped()
    {
        var bad = NewGoal() with { Measure = Measure.Completion, Target = 5, Unit = null };
        var good = NewGoal();
        using var trace = new TraceCapture();

        await ApplyAsync(2, In(bad, 1), In(good, 2));

        RowExists("goals", bad.Id).Should().BeFalse();
        Record(bad.Id).Should().BeNull();
        RowExists("goals", good.Id).Should().BeTrue();
        trace.Messages.Should().Contain(m => m.Contains(bad.Id.ToString("D")));
        LastSeq.Should().Be(2);
    }

    [Fact]
    public async Task Invalid_local_date_loses_and_does_not_stop_the_page()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        _db.Anchor.Execute("UPDATE goals SET updated_at = 'zz'");

        await ApplyAsync(1, In(goal with { Title = "Remota" }, 1));

        (await Goals.GetAsync(goal.Id))!.Title.Should().Be("Remota");
        QueueRow(goal.Id).Should().BeNull();
    }

    [Fact]
    public async Task Invalid_payload_moves_seq_and_base_seq_of_queued_record()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        await Goals.UpdateAsync(goal with { Title = "Local", UpdatedAt = T0.AddMinutes(1) });

        await ApplyAsync(5, Unreadable(goal.Id, SyncRecordKind.Goal, 5));

        (await Goals.GetAsync(goal.Id))!.Title.Should().Be("Local");
        QueueRow(goal.Id).Should().Be(new QueueLine("upsert", 5, "goal"));
        Record(goal.Id)!.Seq.Should().Be(5);
    }

    [Fact]
    public async Task Goal_refused_by_the_table_moves_base_seq_of_queued_record()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        await Goals.UpdateAsync(goal with { Title = "Local", UpdatedAt = T0.AddMinutes(1) });
        var refused = goal with { Measure = Measure.Completion, Target = 5, Unit = null, UpdatedAt = T0.AddMinutes(9) };

        await ApplyAsync(5, In(refused, 5));

        (await Goals.GetAsync(goal.Id))!.Title.Should().Be("Local");
        QueueRow(goal.Id).Should().Be(new QueueLine("upsert", 5, "goal"));
        Record(goal.Id)!.Seq.Should().Be(5);
    }

    // Blob que não abriu

    [Fact]
    public async Task Sealed_record_is_not_a_deletion()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));

        var deleted = await ApplyAsync(6, Sealed(goal.Id, 5), Sealed(checkin.Id, 6));

        deleted.Should().BeEmpty();
        RowExists("goals", goal.Id).Should().BeTrue();
        RowExists("checkins", checkin.Id).Should().BeTrue();
        Count("sync_tombstones").Should().Be(0);
        Count("sync_outbox").Should().Be(0);
        Record(goal.Id).Should().Be(new RecordLine(5, Key(goal.Id), "goal", 1));
        Record(checkin.Id).Should().Be(new RecordLine(6, Key(checkin.Id), "checkin", 1));
        LastSeq.Should().Be(6);
    }

    [Fact]
    public async Task Sealed_record_moves_base_seq_of_queued_record()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        await Goals.UpdateAsync(goal with { Title = "Local", UpdatedAt = T0.AddMinutes(1) });

        await ApplyAsync(5, Sealed(goal.Id, 5));

        (await Goals.GetAsync(goal.Id))!.Title.Should().Be("Local");
        QueueRow(goal.Id).Should().Be(new QueueLine("upsert", 5, "goal"));
    }

    [Fact]
    public async Task Sealed_record_with_unknown_key_is_ignored()
    {
        var id = Guid.CreateVersion7();

        var deleted = await ApplyAsync(5, Sealed(id, 5));

        deleted.Should().BeEmpty();
        Record(id).Should().BeNull();
        Count("sync_outbox").Should().Be(0);
        LastSeq.Should().Be(5);
    }

    [Fact]
    public async Task Sealed_record_known_only_by_the_queue_gets_its_base_seq()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await RememberAsync(goal.Id);

        await ApplyAsync(5, Sealed(goal.Id, 5));

        QueueRow(goal.Id).Should().Be(new QueueLine("upsert", 5, "goal"));
        Record(goal.Id).Should().Be(new RecordLine(5, Key(goal.Id), "goal", 1));
    }

    [Fact]
    public async Task Sealed_record_survives_finish()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));
        await Store.BeginFullResyncAsync();
        await ApplyAsync(6, Sealed(goal.Id, 5), Sealed(checkin.Id, 6));

        var removed = await Store.FinishFullResyncAsync();

        removed.Should().BeEmpty();
        RowExists("goals", goal.Id).Should().BeTrue();
        RowExists("checkins", checkin.Id).Should().BeTrue();
        Record(goal.Id)!.Seq.Should().Be(5);
    }

    [Fact]
    public async Task Queued_record_with_sealed_copy_keeps_its_base_seq_after_finish()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        await Goals.UpdateAsync(goal with { Title = "Local", UpdatedAt = T0.AddMinutes(1) });
        await Store.BeginFullResyncAsync();
        await ApplyAsync(5, Sealed(goal.Id, 5));

        await Store.FinishFullResyncAsync();

        QueueRow(goal.Id).Should().Be(new QueueLine("upsert", 5, "goal"));
    }

    // Aceite perdido

    [Fact]
    public async Task Deletion_after_lost_ack_of_goal_is_applied_through_the_queue_key()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await Goals.AddAsync(goal);
        await Checkins.AddAsync(checkin);
        StartTimer(goal.Id);
        await RememberAsync(goal.Id, checkin.Id);

        var deleted = await ApplyAsync(8, Deleted(checkin.Id, 7), Deleted(goal.Id, 8));

        deleted.Should().Equal(goal.Id);
        RowExists("goals", goal.Id).Should().BeFalse();
        Count("checkins").Should().Be(0);
        Count("active_timer").Should().Be(0);
        Count("sync_outbox").Should().Be(0);
        Count("sync_records").Should().Be(0);
        _db.Anchor.ExecuteScalar<string>("SELECT goal_id FROM sync_tombstones").Should().Be(goal.Id.ToString("D"));
    }

    [Fact]
    public async Task Deletion_after_lost_ack_of_checkin_is_applied_through_the_queue_key()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        var checkin = NewCheckin(goal.Id);
        await Checkins.AddAsync(checkin);
        await RememberAsync(checkin.Id);

        var deleted = await ApplyAsync(7, Deleted(checkin.Id, 7));

        deleted.Should().BeEmpty();
        RowExists("checkins", checkin.Id).Should().BeFalse();
        RowExists("goals", goal.Id).Should().BeTrue();
        Count("sync_outbox").Should().Be(0);
        Count("sync_tombstones").Should().Be(0);
        Record(checkin.Id).Should().BeNull();
    }

    [Fact]
    public async Task Queued_record_without_remembered_key_is_not_found_by_deletion()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);

        var deleted = await ApplyAsync(7, Deleted(goal.Id, 7));

        deleted.Should().BeEmpty();
        RowExists("goals", goal.Id).Should().BeTrue();
        QueueRow(goal.Id).Should().NotBeNull();
    }

    // Compensações

    [Fact]
    public async Task Concurrent_compensations_mark_older_as_superseded()
    {
        var goal = NewGoal();
        var debt = NewCheckin(goal.Id, amount: -3, doneAt: T0.AddHours(-2));
        var older = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(1), compensation: Compensates());
        var newer = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(2), compensation: Compensates());

        await ApplyAsync(4, In(goal, 1), In(debt, 2), In(older, 3), In(newer, 4));

        IsSuperseded(older.Id).Should().BeTrue();
        IsSuperseded(newer.Id).Should().BeFalse();
        (await DayTotalAsync(goal.Id)).Should().Be(2);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("{}")]
    public async Task Corrupt_compensation_does_not_stop_the_recalculation(string seen)
    {
        var goal = NewGoal();
        var corrupt = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(1), compensation: Compensates());
        var older = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(2), compensation: Compensates());
        var newer = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(3), compensation: Compensates());
        await ApplyAsync(3, In(goal, 1), In(corrupt, 2), In(older, 3));
        _db.Anchor.Execute("UPDATE checkins SET compensation_seen = @Seen WHERE id = @Id", new { Seen = seen, Id = corrupt.Id.ToString("D") });

        await ApplyAsync(4, In(newer, 4));

        LastSeq.Should().Be(4);
        IsSuperseded(older.Id).Should().BeTrue();
        IsSuperseded(newer.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Arrival_order_does_not_change_superseded()
    {
        var goal = NewGoal();
        var older = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(1), compensation: Compensates());
        var newer = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(2), compensation: Compensates());
        await ApplyAsync(1, In(goal, 1));

        await ApplyAsync(2, In(newer, 2));
        await ApplyAsync(3, In(older, 3));

        IsSuperseded(older.Id).Should().BeTrue();
        IsSuperseded(newer.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Compensation_that_saw_the_other_counts_with_it()
    {
        var goal = NewGoal();
        var first = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(1), compensation: Compensates());
        var second = NewCheckin(goal.Id, amount: 1, doneAt: T0.AddMinutes(2), compensation: Compensates(first.Id));

        await ApplyAsync(3, In(goal, 1), In(first, 2), In(second, 3));

        IsSuperseded(first.Id).Should().BeFalse();
        IsSuperseded(second.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Removing_the_newer_compensation_brings_the_older_back()
    {
        var goal = NewGoal();
        var older = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(1), compensation: Compensates());
        var newer = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(2), compensation: Compensates());
        await ApplyAsync(3, In(goal, 1), In(older, 2), In(newer, 3));

        await ApplyAsync(4, Deleted(newer.Id, 4));

        IsSuperseded(older.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Pending_compensations_are_recalculated_when_goal_arrives()
    {
        var goal = NewGoal();
        var older = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(1), compensation: Compensates());
        var newer = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(2), compensation: Compensates());
        await ApplyAsync(2, In(older, 1), In(newer, 2));

        await ApplyAsync(3, In(goal, 3));

        IsSuperseded(older.Id).Should().BeTrue();
        IsSuperseded(newer.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Local_compensation_competes_with_the_remote_one()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        var local = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(1), compensation: Compensates());
        await Checkins.AddAsync(local);
        var remote = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(2), compensation: Compensates());

        await ApplyAsync(2, In(remote, 2));

        IsSuperseded(local.Id).Should().BeTrue();
        QueueRow(local.Id).Should().NotBeNull();
    }

    // Sincronização completa

    [Fact]
    public async Task Finish_removes_unconfirmed_goal_not_in_queue()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));
        StartTimer(goal.Id);
        await Store.BeginFullResyncAsync();

        var removed = await Store.FinishFullResyncAsync();

        removed.Should().Equal(goal.Id);
        Count("goals").Should().Be(0);
        Count("checkins").Should().Be(0);
        Count("active_timer").Should().Be(0);
        Count("sync_records").Should().Be(0);
        Count("sync_outbox").Should().Be(0);
        Count("sync_tombstones").Should().Be(0);
    }

    [Fact]
    public async Task Finish_keeps_confirmed_and_queued_records()
    {
        var confirmed = NewGoal();
        var queued = NewGoal();
        var confirmedCheckin = NewCheckin(confirmed.Id);
        await ApplyAsync(3, In(confirmed, 1), In(queued, 2), In(confirmedCheckin, 3));
        var edited = queued with { Title = "Local", UpdatedAt = T0.AddMinutes(1) };
        await Goals.UpdateAsync(edited);
        await Store.BeginFullResyncAsync();
        await ApplyAsync(4, In(confirmed, 3), In(confirmedCheckin, 4));

        var removed = await Store.FinishFullResyncAsync();

        removed.Should().BeEmpty();
        RowExists("goals", confirmed.Id).Should().BeTrue();
        RowExists("checkins", confirmedCheckin.Id).Should().BeTrue();
        (await Goals.GetAsync(queued.Id))!.Title.Should().Be("Local");
        QueueRow(queued.Id).Should().Be(new QueueLine("upsert", 0, "goal"));
        Record(confirmed.Id)!.Seq.Should().Be(3);
    }

    [Fact]
    public async Task Edit_after_finish_keeps_base_seq_zero()
    {
        var queued = NewGoal();
        await ApplyAsync(2, In(queued, 2));
        await Goals.UpdateAsync(queued with { Title = "Local", UpdatedAt = T0.AddMinutes(1) });
        await Store.BeginFullResyncAsync();
        await Store.FinishFullResyncAsync();

        await Goals.UpdateAsync(queued with { Title = "De novo", UpdatedAt = T0.AddMinutes(2) });

        QueueRow(queued.Id).Should().Be(new QueueLine("upsert", 0, "goal"));
    }

    [Fact]
    public async Task Finish_removes_unconfirmed_checkin_and_pending()
    {
        var goal = NewGoal();
        var gone = NewCheckin(goal.Id);
        var waiting = NewCheckin(Guid.CreateVersion7());
        await ApplyAsync(3, In(goal, 1), In(gone, 2), In(waiting, 3));
        await Store.BeginFullResyncAsync();
        await ApplyAsync(1, In(goal, 1));

        var removed = await Store.FinishFullResyncAsync();

        removed.Should().BeEmpty();
        RowExists("goals", goal.Id).Should().BeTrue();
        Count("checkins").Should().Be(0);
        Count("sync_pending").Should().Be(0);
        Record(gone.Id).Should().BeNull();
        Record(waiting.Id).Should().BeNull();
    }

    [Fact]
    public async Task Finish_recalculates_compensations_of_removed_checkins()
    {
        var goal = NewGoal();
        var older = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(1), compensation: Compensates());
        var newer = NewCheckin(goal.Id, amount: 5, doneAt: T0.AddMinutes(2), compensation: Compensates());
        await ApplyAsync(3, In(goal, 1), In(older, 2), In(newer, 3));
        await Store.BeginFullResyncAsync();
        await ApplyAsync(2, In(goal, 1), In(older, 2));

        await Store.FinishFullResyncAsync();

        RowExists("checkins", newer.Id).Should().BeFalse();
        IsSuperseded(older.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Record_discarded_by_tombstone_counts_as_confirmed()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        await Goals.DeleteAsync(goal.Id);
        await Store.BeginFullResyncAsync();
        await ApplyAsync(1, In(goal, 1));

        await Store.FinishFullResyncAsync();

        QueueRow(goal.Id).Should().Be(new QueueLine("delete", 1, "goal"));
        Record(goal.Id)!.Confirmed.Should().Be(1);
    }

    [Fact]
    public async Task Unreadable_record_is_not_removed_by_finish()
    {
        var goal = NewGoal();
        var checkin = NewCheckin(goal.Id);
        await ApplyAsync(2, In(goal, 1), In(checkin, 2));
        await Store.BeginFullResyncAsync();
        await ApplyAsync(2, Unreadable(goal.Id, SyncRecordKind.Goal, 1), Unreadable(checkin.Id, SyncRecordKind.Checkin, 2));

        var removed = await Store.FinishFullResyncAsync();

        removed.Should().BeEmpty();
        RowExists("goals", goal.Id).Should().BeTrue();
        RowExists("checkins", checkin.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Finish_outside_full_resync_does_nothing()
    {
        var goal = NewGoal();
        await ApplyAsync(1, In(goal, 1));
        _db.Anchor.Execute("UPDATE sync_records SET confirmed = 0");

        var removed = await Store.FinishFullResyncAsync();

        removed.Should().BeEmpty();
        RowExists("goals", goal.Id).Should().BeTrue();
        Record(goal.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task Finish_ends_the_full_resync()
    {
        await Store.BeginFullResyncAsync();

        await Store.FinishFullResyncAsync();

        _db.Anchor.ExecuteScalar<long>("SELECT full_resync FROM sync_state").Should().Be(0);
    }

    [Fact]
    public async Task Begin_zeroes_last_seq_and_survives_restart()
    {
        var goal = NewGoal();
        var gone = NewGoal();
        await ApplyAsync(2, In(goal, 1), In(gone, 2));

        await Store.BeginFullResyncAsync();

        LastSeq.Should().Be(0);
        _db.Anchor.ExecuteScalar<long>("SELECT full_resync FROM sync_state").Should().Be(1);
        Record(goal.Id)!.Confirmed.Should().Be(0);

        var restarted = new SyncLocalStore(_db.Factory);
        await restarted.ApplyPageAsync([In(goal, 1)], 1, Me);
        (await restarted.FinishFullResyncAsync()).Should().Equal(gone.Id);
        RowExists("goals", goal.Id).Should().BeTrue();
    }
}
