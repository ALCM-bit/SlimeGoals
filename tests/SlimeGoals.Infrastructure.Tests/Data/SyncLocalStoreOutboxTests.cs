using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class SyncLocalStoreOutboxTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private SyncLocalStore Store => new(_db.Factory);
    private GoalRepository Goals => new(_db.Factory);
    private CheckinRepository Checkins => new(_db.Factory);

    public SyncLocalStoreOutboxTests() => _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");

    public void Dispose() => _db.Dispose();

    private sealed record QueueLine(string Op, long BaseSeq, long Revision);

    private sealed record RecordLine(long Seq, string RecordKey);

    private static Goal NewGoal() => new()
    {
        Id = Guid.CreateVersion7(), Title = "Água", Recurrence = Recurrence.Daily, Target = 8, Icon = "water",
        Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = T0, Unit = "copos",
    };

    private void QueueDelete(Guid id, string kind, long baseSeq) => _db.Anchor.Execute(
        "INSERT INTO sync_outbox (record_id, kind, op, base_seq) VALUES (@Id, @Kind, 'delete', @Seq)",
        new { Id = id.ToString("D"), Kind = kind, Seq = baseSeq });

    private QueueLine? QueueRow(Guid id) => _db.Anchor.QuerySingleOrDefault<QueueLine>(
        "SELECT op AS Op, base_seq AS BaseSeq, revision AS Revision FROM sync_outbox WHERE record_id = @Id",
        new { Id = id.ToString("D") });

    private RecordLine? Record(Guid id) => _db.Anchor.QuerySingleOrDefault<RecordLine>(
        "SELECT seq AS Seq, record_key AS RecordKey FROM sync_records WHERE record_id = @Id",
        new { Id = id.ToString("D") });

    [Fact]
    public async Task Reads_in_position_order_with_current_payload()
    {
        var a = NewGoal();
        var b = NewGoal();
        var checkin = new Checkin { Id = Guid.CreateVersion7(), GoalId = a.Id, DoneAt = T0, Amount = 2, UpdatedAt = T0 };
        await Goals.AddAsync(a);
        await Goals.AddAsync(b);
        await Checkins.AddAsync(checkin);
        await Goals.UpdateAsync(a with { Title = "Nova" });

        var items = await Store.ReadOutboxAsync(10);

        items.Select(i => i.RecordId).Should().Equal(a.Id, b.Id, checkin.Id);
        items[0].Kind.Should().Be(SyncRecordKind.Goal);
        items[2].Kind.Should().Be(SyncRecordKind.Checkin);
        items[0].IsDelete.Should().BeFalse();
        items[0].Revision.Should().Be(2);
        SyncCodec.DecodeGoal(items[0].Payload!).Title.Should().Be("Nova");
        SyncCodec.DecodeCheckin(items[2].Payload!).Amount.Should().Be(2);

        (await Store.ReadOutboxAsync(2)).Select(i => i.RecordId).Should().Equal(a.Id, b.Id);
    }

    [Fact]
    public async Task Reads_only_the_items_without_a_remembered_key_up_to_the_limit()
    {
        var keyed = NewGoal();
        var first = NewGoal();
        var second = NewGoal();
        foreach (var goal in new[] { keyed, first, second })
            await Goals.AddAsync(goal);
        await Store.RememberKeysAsync([new OutboxKey(keyed.Id, "k1")]);

        (await Store.ReadUnkeyedAsync(10)).Should().Equal(first.Id, second.Id);
        (await Store.ReadUnkeyedAsync(1)).Should().Equal(first.Id);
    }

    [Fact]
    public async Task Remembering_keys_keeps_the_item_as_it_was()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        var before = QueueRow(goal.Id);

        await Store.RememberKeysAsync([new OutboxKey(goal.Id, "k1"), new OutboxKey(Guid.CreateVersion7(), "k2")]);

        QueueRow(goal.Id).Should().Be(before);
        _db.Anchor.Query<string>("SELECT record_key FROM sync_outbox").Should().Equal("k1");
    }

    [Fact]
    public async Task Delete_item_has_no_payload()
    {
        var id = Guid.CreateVersion7();
        QueueDelete(id, "goal", 7);

        var item = (await Store.ReadOutboxAsync(5)).Should().ContainSingle().Subject;

        item.IsDelete.Should().BeTrue();
        item.Payload.Should().BeNull();
        item.BaseSeq.Should().Be(7);
        item.RecordId.Should().Be(id);
    }

    [Fact]
    public async Task Invalid_row_leaves_the_queue_with_trace()
    {
        var bad = NewGoal();
        var good = NewGoal();
        await Goals.AddAsync(bad);
        await Goals.AddAsync(good);
        _db.Anchor.Execute("UPDATE goals SET active_from = 'zz' WHERE id = @Id", new { Id = bad.Id.ToString("D") });
        using var trace = new TraceCapture();

        var items = await Store.ReadOutboxAsync(1);

        items.Select(i => i.RecordId).Should().Equal(good.Id);
        QueueRow(bad.Id).Should().BeNull();
        trace.Messages.Should().Contain(m => m.Contains(bad.Id.ToString("D")));
    }

    [Fact]
    public async Task Item_whose_row_vanished_leaves_the_queue()
    {
        var gone = NewGoal();
        var kept = NewGoal();
        await Goals.AddAsync(gone);
        await Goals.AddAsync(kept);
        _db.Anchor.Execute("DELETE FROM goals WHERE id = @Id", new { Id = gone.Id.ToString("D") });

        var items = await Store.ReadOutboxAsync(1);

        items.Select(i => i.RecordId).Should().Equal(kept.Id);
        QueueRow(gone.Id).Should().BeNull();
    }

    [Fact]
    public async Task Accepted_upsert_leaves_queue_and_records_seq_and_key()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        var item = (await Store.ReadOutboxAsync(1)).Single();

        await Store.ConfirmPushAsync([new PushConfirmation(goal.Id, SyncRecordKind.Goal, false, "key-1", 5, item.Revision)]);

        QueueRow(goal.Id).Should().BeNull();
        Record(goal.Id).Should().Be(new RecordLine(5, "key-1"));
    }

    [Fact]
    public async Task Accepted_item_with_newer_revision_stays_with_new_base_seq()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        var item = (await Store.ReadOutboxAsync(1)).Single();
        await Goals.UpdateAsync(goal with { Title = "Mudou" });

        await Store.ConfirmPushAsync([new PushConfirmation(goal.Id, SyncRecordKind.Goal, false, "key-1", 9, item.Revision)]);

        QueueRow(goal.Id).Should().Be(new QueueLine("upsert", 9, item.Revision + 1));
        Record(goal.Id).Should().Be(new RecordLine(9, "key-1"));
    }

    [Fact]
    public async Task Accepted_upsert_without_queue_row_still_records_seq()
    {
        var id = Guid.CreateVersion7();

        await Store.ConfirmPushAsync([new PushConfirmation(id, SyncRecordKind.Goal, false, "key-1", 3, 1)]);

        Record(id).Should().Be(new RecordLine(3, "key-1"));
    }

    [Fact]
    public async Task Accepted_upsert_with_delete_in_queue_updates_base_seq()
    {
        var id = Guid.CreateVersion7();
        QueueDelete(id, "goal", 1);

        await Store.ConfirmPushAsync([new PushConfirmation(id, SyncRecordKind.Goal, false, "key-1", 4, 1)]);

        QueueRow(id).Should().Be(new QueueLine("delete", 4, 1));
        Record(id).Should().Be(new RecordLine(4, "key-1"));
    }

    [Fact]
    public async Task Accepted_delete_leaves_queue_and_clears_sync_records()
    {
        var id = Guid.CreateVersion7();
        QueueDelete(id, "goal", 2);
        _db.Anchor.Execute("INSERT INTO sync_records (record_id, kind, seq, record_key) VALUES (@Id, 'goal', 2, 'k')",
            new { Id = id.ToString("D") });

        await Store.ConfirmPushAsync([new PushConfirmation(id, SyncRecordKind.Goal, true, "k", 3, 1)]);

        QueueRow(id).Should().BeNull();
        Record(id).Should().BeNull();
    }

    [Fact]
    public async Task Confirmation_is_atomic()
    {
        var a = NewGoal();
        var b = NewGoal();
        await Goals.AddAsync(a);
        await Goals.AddAsync(b);
        _db.Anchor.Execute($"""
            CREATE TRIGGER fail_record BEFORE INSERT ON sync_records WHEN NEW.record_id = '{b.Id.ToString("D")}'
            BEGIN SELECT RAISE(ABORT, 'teste'); END;
            """);

        var act = () => Store.ConfirmPushAsync([
            new PushConfirmation(a.Id, SyncRecordKind.Goal, false, "ka", 1, 1),
            new PushConfirmation(b.Id, SyncRecordKind.Goal, false, "kb", 2, 1),
        ]);

        await act.Should().ThrowAsync<Exception>();
        QueueRow(a.Id).Should().NotBeNull();
        Record(a.Id).Should().BeNull();
    }

    [Fact]
    public async Task Status_tells_when_the_device_has_records()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        (await Store.GetStatusAsync()).HasRecords.Should().BeFalse("o que só está na fila o servidor ainda não conhece");

        var item = (await Store.ReadOutboxAsync(1)).Single();
        await Store.ConfirmPushAsync([new PushConfirmation(goal.Id, SyncRecordKind.Goal, false, "k1", 1, item.Revision)]);

        (await Store.GetStatusAsync()).HasRecords.Should().BeTrue();
    }

    [Fact]
    public async Task Record_cycle_saves_date_and_error()
    {
        var at = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

        await Store.RecordCycleAsync(at, null);
        (await Store.GetStatusAsync()).Should().Be(new SyncStatus(at, null, 0, false, false));

        await Store.RecordCycleAsync(null, "falhou");
        (await Store.GetStatusAsync()).Should().Be(new SyncStatus(at, "falhou", 0, false, false));
    }

    [Fact]
    public async Task Status_shows_a_full_resync_until_it_finishes()
    {
        await Store.BeginFullResyncAsync();
        (await Store.GetStatusAsync()).FullResync.Should().BeTrue();

        await Store.FinishFullResyncAsync();
        (await Store.GetStatusAsync()).FullResync.Should().BeFalse();
    }

    [Fact]
    public async Task Status_counts_pending()
    {
        await Goals.AddAsync(NewGoal());
        await Goals.AddAsync(NewGoal());

        (await Store.GetStatusAsync()).Pending.Should().Be(2);
    }

    [Fact]
    public void Di_resolves_sync_local_store()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"slimegoals-di-{Guid.NewGuid():N}");
        try
        {
            using var provider = new ServiceCollection()
                .AddSlimeGoalsInfrastructure(Path.Combine(dir, "slimegoals.db"))
                .BuildServiceProvider();
            provider.GetRequiredService<ISyncLocalStore>().Should().BeOfType<SyncLocalStore>();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
