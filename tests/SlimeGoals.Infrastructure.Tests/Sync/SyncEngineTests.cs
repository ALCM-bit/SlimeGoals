using Dapper;
using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Sync;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Testing;

namespace SlimeGoals.Infrastructure.Tests.Sync;

public class SyncEngineTests : IDisposable
{
    private const string UpdateApp = "Atualize o app para continuar sincronizando.";

    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Me = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private readonly TestDatabase _db = new();
    private readonly FakeSyncServer _server = new();
    private readonly ScriptedTransport _transport;
    private readonly PlainSyncCrypto _crypto = new();
    private readonly RecordingGate _gate = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly FakeTimeProvider _time = new(T0);
    private int _notices;

    public SyncEngineTests()
    {
        _transport = new ScriptedTransport(_server);
        _changes.Changed += (_, _) => _notices++;
        _db.Anchor.Execute("UPDATE sync_state SET enabled = 1, device_id = @Id", new { Id = Me.ToString("D") });
    }

    public void Dispose() => _db.Dispose();

    private SyncLocalStore Store => new(_db.Factory);
    private GoalRepository Goals => new(_db.Factory);
    private SyncStateRepository State => new(_db.Factory);

    private SyncEngine Engine(IRemoteDeletionGate? gate = null) =>
        new(State, Store, _transport, _crypto, gate ?? _gate, _changes, _time);

    /// <summary>Conta as chamadas em ordem e deixa um teste agir antes de cada uma.</summary>
    private sealed class ScriptedTransport(ISyncTransport inner) : ISyncTransport
    {
        public List<string> Calls { get; } = [];
        public List<long> Pulls { get; } = [];
        public List<IReadOnlyList<PushItem>> Pushes { get; } = [];
        public Func<int, Task>? BeforePull { get; set; }
        public Func<int, Task>? BeforePush { get; set; }

        public async Task<SyncPullPage> PullAsync(long after, int limit, CancellationToken ct = default)
        {
            Calls.Add("pull");
            Pulls.Add(after);
            if (BeforePull is { } before)
                await before(Pulls.Count);
            return await inner.PullAsync(after, limit, ct);
        }

        public async Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default)
        {
            Calls.Add("push");
            Pushes.Add(items);
            if (BeforePush is { } before)
                await before(Pushes.Count);
            return await inner.PushAsync(items, ct);
        }
    }

    private sealed class RecordingGate : IRemoteDeletionGate
    {
        public List<Guid> Deleted { get; } = [];
        public Action? During { get; set; }

        public async Task RunAsync(Func<CancellationToken, Task<IReadOnlyList<Guid>>> apply, CancellationToken ct = default)
        {
            Deleted.AddRange(await apply(ct));
            During?.Invoke();
        }
    }

    private sealed class FakeFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new("T", "B", "Feito!");
    }

    private sealed class EmptyPages : ISyncTransport
    {
        public Task<SyncPullPage> PullAsync(long after, int limit, CancellationToken ct = default) =>
            Task.FromResult(new SyncPullPage([], HasMore: true));

        public Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class SilentPush : ISyncTransport
    {
        public Task<SyncPullPage> PullAsync(long after, int limit, CancellationToken ct = default) =>
            Task.FromResult(new SyncPullPage([], HasMore: false));

        public Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default) =>
            Task.FromResult(new PushResult([], []));
    }

    private static Goal NewGoal(string title = "Água", DateTimeOffset? updatedAt = null) => new()
    {
        Id = Guid.CreateVersion7(), Title = title, Recurrence = Recurrence.Daily, Target = 8, Icon = "water",
        Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = updatedAt ?? T0, Unit = "copos",
    };

    private byte[] Sealed(Goal goal, int version = SyncCodec.FormatVersion) => _crypto.Seal(
        new SyncEnvelope(version, SyncRecordKind.Goal, goal.Id, Other, SyncCodec.Encode(goal)));

    /// <summary>Outro aparelho envia direto ao servidor.</summary>
    private async Task SendFromOtherAsync(Goal goal, long baseSeq = 0, int version = SyncCodec.FormatVersion)
    {
        var result = await _server.PushAsync([new(_crypto.RecordKey(goal.Id), baseSeq, Sealed(goal, version))]);
        result.Conflicts.Should().BeEmpty();
    }

    private long SeqOf(Guid id) => _server.Records.Single(r => r.RecordKey == _crypto.RecordKey(id)).Seq;

    private async Task DeleteFromOtherAsync(Guid id)
    {
        var result = await _server.PushAsync([new(_crypto.RecordKey(id), SeqOf(id), null)]);
        result.Conflicts.Should().BeEmpty();
    }

    private string TitleOnServer(Guid id)
    {
        var key = _crypto.RecordKey(id);
        var envelope = _crypto.Open(key, _server.Records.Single(r => r.RecordKey == key).Blob!);
        return SyncCodec.DecodeGoal(envelope.Payload).Title;
    }

    /// <summary>Envia as metas locais e baixa de volta: <c>last_seq</c> fica no fim do servidor.</summary>
    private async Task<(Goal Gone, Goal Kept)> SyncTwoGoalsAsync(IRemoteDeletionGate? gate = null)
    {
        var (gone, kept) = (NewGoal("Apagada"), NewGoal("Mantida"));
        await Goals.AddAsync(gone);
        await Goals.AddAsync(kept);
        (await Engine(gate).RunCycleAsync()).Should().Be(SyncCycleResult.Done);
        (await Engine(gate).RunCycleAsync()).Should().Be(SyncCycleResult.Done);
        (await State.GetAsync()).LastSeq.Should().Be(2);
        return (gone, kept);
    }

    [Fact]
    public async Task Disabled_does_nothing()
    {
        await Goals.AddAsync(NewGoal());
        _db.Anchor.Execute("UPDATE sync_state SET enabled = 0");

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Disabled);
        _transport.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Enabled_without_device_counts_as_disabled()
    {
        _db.Anchor.Execute("UPDATE sync_state SET device_id = NULL");

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Disabled);
        _transport.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Pulls_all_pages_then_pushes_in_batches()
    {
        for (var i = 0; i < SyncEngine.PullLimit + 1; i++)
            await SendFromOtherAsync(NewGoal());
        for (var i = 0; i < SyncEngine.PushBatch + 1; i++)
            await Goals.AddAsync(NewGoal());

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        _transport.Calls.Should().Equal("pull", "pull", "push", "push");
        _transport.Pulls.Should().Equal(0, SyncEngine.PullLimit);
        _transport.Pushes.Select(p => p.Count).Should().Equal(SyncEngine.PushBatch, 1);
        (await State.GetAsync()).LastSeq.Should().Be(SyncEngine.PullLimit + 1);
        (await Store.GetStatusAsync()).Pending.Should().Be(0);
    }

    [Fact]
    public async Task Remembers_the_keys_before_sending()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        string? keyWhenSent = null;
        _transport.BeforePush = _ =>
        {
            keyWhenSent = _db.Anchor.QuerySingle<string?>("SELECT record_key FROM sync_outbox");
            return Task.CompletedTask;
        };

        await Engine().RunCycleAsync();

        keyWhenSent.Should().Be(_crypto.RecordKey(goal.Id));
    }

    [Fact]
    public async Task Sends_the_device_and_the_format_version_in_the_envelope()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);

        await Engine().RunCycleAsync();

        var record = _server.Records.Should().ContainSingle().Subject;
        var envelope = _crypto.Open(record.RecordKey, record.Blob!);
        envelope.Should().Be(new SyncEnvelope(SyncCodec.FormatVersion, SyncRecordKind.Goal, goal.Id, Me, envelope.Payload));
        SyncCodec.DecodeGoal(envelope.Payload).Id.Should().Be(goal.Id);
    }

    [Fact]
    public async Task Queued_deletion_goes_without_a_blob()
    {
        var (gone, _) = await SyncTwoGoalsAsync();
        await Goals.DeleteAsync(gone.Id);

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        _server.Records.Single(r => r.RecordKey == _crypto.RecordKey(gone.Id)).Blob.Should().BeNull();
        (await Store.GetStatusAsync()).Pending.Should().Be(0);
    }

    [Fact]
    public async Task Conflict_pulls_and_resends_the_winner()
    {
        var local = NewGoal("Local", T0.AddHours(1));
        await Goals.AddAsync(local);
        _transport.BeforePush = call => call == 1 ? SendFromOtherAsync(local with { Title = "Remota", UpdatedAt = T0 }) : Task.CompletedTask;

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        _transport.Calls.Should().Equal("pull", "push", "pull", "push");
        TitleOnServer(local.Id).Should().Be("Local");
        (await Store.GetStatusAsync()).Pending.Should().Be(0);
    }

    [Fact]
    public async Task Conflict_confirms_the_accepted_items_of_the_batch()
    {
        var (clashing, accepted) = (NewGoal("Local", T0.AddHours(1)), NewGoal("Aceita"));
        await Goals.AddAsync(clashing);
        await Goals.AddAsync(accepted);
        _transport.BeforePush = call => call == 1 ? SendFromOtherAsync(clashing with { UpdatedAt = T0 }) : Task.CompletedTask;

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        _transport.Pushes.Select(p => p.Count).Should().Equal(2, 1);
        _transport.Pushes[1].Single().RecordKey.Should().Be(_crypto.RecordKey(clashing.Id));
    }

    [Fact]
    public async Task Stops_after_three_attempts_and_returns_Incomplete()
    {
        var local = NewGoal("Local", T0.AddHours(1));
        await Goals.AddAsync(local);
        await Store.RecordCycleAsync(T0.AddDays(-1), "erro antigo");
        var version = local with { Title = "Remota", UpdatedAt = T0 };
        _transport.BeforePush = _ => SendFromOtherAsync(version, _server.Records.SingleOrDefault()?.Seq ?? 0);

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Incomplete);
        _transport.Pulls.Should().HaveCount(SyncEngine.MaxAttempts);
        _transport.Pushes.Should().HaveCount(SyncEngine.MaxAttempts);
        var status = await Store.GetStatusAsync();
        status.LastError.Should().Be("erro antigo");
        status.LastSyncAt.Should().Be(T0.AddDays(-1));
        status.Pending.Should().Be(1);
    }

    [Fact]
    public async Task Batch_without_answer_stops_sending()
    {
        await Goals.AddAsync(NewGoal());
        var engine = new SyncEngine(State, Store, new SilentPush(), _crypto, _gate, _changes, _time);

        var result = await engine.RunCycleAsync();

        result.Should().Be(SyncCycleResult.Incomplete);
    }

    [Fact]
    public async Task Overlapping_cycle_returns_Skipped()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _transport.BeforePull = async _ =>
        {
            entered.SetResult();
            await release.Task;
        };
        var engine = Engine();

        var first = engine.RunCycleAsync();
        await entered.Task;
        var second = await engine.RunCycleAsync();
        release.SetResult();

        second.Should().Be(SyncCycleResult.Skipped);
        (await first).Should().Be(SyncCycleResult.Done);
        _transport.Pulls.Should().ContainSingle();
    }

    [Fact]
    public async Task Network_failure_returns_Failed_and_records_error()
    {
        using var trace = new TraceCapture();
        _server.FailNext(new HttpRequestException("sem rede"));

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        var status = await Store.GetStatusAsync();
        status.LastError.Should().Be("sem rede");
        status.LastSyncAt.Should().BeNull();
        trace.Messages.Should().Contain(m => m.Contains("sem rede"));
    }

    [Fact]
    public async Task Failure_to_record_the_error_does_not_throw()
    {
        _db.Anchor.Execute("DROP TABLE sync_state");

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
    }

    [Fact]
    public async Task Timeout_without_cancellation_is_a_failure()
    {
        _server.FailNext(new TaskCanceledException("tempo esgotado"));

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
    }

    [Fact]
    public async Task Reset_from_zero_is_a_failure_and_does_not_loop()
    {
        _server.FailNext(new SyncResetRequiredException());

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        _transport.Pulls.Should().ContainSingle();
    }

    [Fact]
    public async Task Page_with_more_but_no_records_is_a_failure_and_does_not_loop()
    {
        var engine = new SyncEngine(State, Store, new EmptyPages(), _crypto, _gate, _changes, _time);

        var result = await engine.RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
    }

    [Fact]
    public async Task Success_clears_error_and_records_date()
    {
        await Store.RecordCycleAsync(null, "erro antigo");

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        var status = await Store.GetStatusAsync();
        status.LastError.Should().BeNull();
        status.LastSyncAt.Should().Be(T0);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        _transport.BeforePull = _ =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        };
        var engine = Engine();

        var act = () => engine.RunCycleAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await Store.GetStatusAsync()).LastError.Should().BeNull();

        _transport.BeforePull = null;
        (await engine.RunCycleAsync()).Should().Be(SyncCycleResult.Done);
    }

    [Fact]
    public async Task Unknown_version_returns_Paused_keeps_last_seq_and_records_error()
    {
        var (readable, future) = (NewGoal("Legível"), NewGoal("Futura"));
        await SendFromOtherAsync(readable);
        await SendFromOtherAsync(future, version: SyncCodec.FormatVersion + 1);
        await Goals.AddAsync(NewGoal("Na fila"));

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Paused);
        var state = await State.GetAsync();
        state.LastSeq.Should().Be(0);
        state.Enabled.Should().BeTrue();
        (await Store.GetStatusAsync()).LastError.Should().Be(UpdateApp);
        (await Goals.GetAsync(readable.Id)).Should().BeNull();
        _transport.Pushes.Should().BeEmpty();
    }

    [Fact]
    public async Task Unreadable_blob_is_traced_and_skipped()
    {
        using var trace = new TraceCapture();
        var (corrupted, wrongKey, good) = (Guid.NewGuid(), Guid.NewGuid(), NewGoal("Boa"));
        await _server.PushAsync([
            new(_crypto.RecordKey(corrupted), 0, [1, 2, 3]),
            new(_crypto.RecordKey(wrongKey), 0, Sealed(good)),
        ]);
        await SendFromOtherAsync(good);

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        (await Goals.GetAsync(good.Id)).Should().NotBeNull();
        (await State.GetAsync()).LastSeq.Should().Be(3);
        trace.Messages.Should().Contain(m => m.Contains(_crypto.RecordKey(corrupted)));
        trace.Messages.Should().Contain(m => m.Contains(_crypto.RecordKey(wrongKey)));
    }

    [Fact]
    public async Task Reset_runs_full_resync_and_removes_what_did_not_return()
    {
        var (gone, kept) = await SyncTwoGoalsAsync();
        await DeleteFromOtherAsync(gone.Id);
        _server.PurgeBefore(4);
        _transport.Pulls.Clear();
        _notices = 0;

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        _transport.Pulls.Should().Equal(2, 0);
        (await Goals.GetAsync(gone.Id)).Should().BeNull();
        (await Goals.GetAsync(kept.Id)).Should().NotBeNull();
        _gate.Deleted.Should().Equal(gone.Id);
        _notices.Should().Be(2, "uma pela página e uma pelo que saiu no fim");
        (await Store.GetStatusAsync()).Pending.Should().Be(0);
    }

    [Fact]
    public async Task Interrupted_full_resync_finishes_next_cycle()
    {
        var (gone, kept) = await SyncTwoGoalsAsync();
        await DeleteFromOtherAsync(gone.Id);
        for (var i = 0; i < SyncEngine.PullLimit + 1; i++)
            await SendFromOtherAsync(NewGoal());
        _server.PurgeBefore(4);
        _transport.Pulls.Clear();
        _transport.BeforePull = call => call == 3 ? throw new HttpRequestException("sem rede") : Task.CompletedTask;

        (await Engine().RunCycleAsync()).Should().Be(SyncCycleResult.Failed);

        // A primeira página traz a meta mantida (seq 2) e 199 novas (seq 4 em diante).
        const int stoppedAt = SyncEngine.PullLimit + 2;
        _transport.Pulls.Should().Equal(2, 0, stoppedAt);
        (await Goals.GetAsync(gone.Id)).Should().NotBeNull();

        _transport.Pulls.Clear();
        _transport.BeforePull = null;
        (await Engine().RunCycleAsync()).Should().Be(SyncCycleResult.Done);

        _transport.Pulls.Should().Equal(stoppedAt);
        (await Goals.GetAsync(gone.Id)).Should().BeNull();
        (await Goals.GetAsync(kept.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Page_raises_one_change_notice()
    {
        for (var i = 0; i < 3; i++)
            await SendFromOtherAsync(NewGoal());
        // Um aviso de dentro da aplicação, como a retirada de lembretes, se junta ao da página. O fim do ciclo não avisa.
        var runs = 0;
        _gate.During = () =>
        {
            if (++runs == 1)
                _changes.NotifyChanged();
        };

        await Engine().RunCycleAsync();

        _notices.Should().Be(1);
    }

    [Fact]
    public async Task Empty_page_raises_none()
    {
        await Engine().RunCycleAsync();

        _notices.Should().Be(0);
        (await State.GetAsync()).LastSeq.Should().Be(0);
    }

    [Fact]
    public async Task Remote_deletion_runs_through_the_gate()
    {
        var log = new InMemoryReminderLogRepository();
        var notifications = new FakeNotificationService();
        var reminders = new ReminderService(new InMemoryGoalRepository(), new InMemoryCheckinRepository(),
            new InMemoryTimerRepository(), log, notifications, new FakeFormatter(), _time);
        var (gone, kept) = await SyncTwoGoalsAsync(reminders);
        var pending = new ReminderLogEntry(Guid.NewGuid(), gone.Id, T0);
        await log.AddAsync(pending);
        await log.AddAsync(new ReminderLogEntry(Guid.NewGuid(), kept.Id, T0));
        await DeleteFromOtherAsync(gone.Id);

        var result = await Engine(reminders).RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        (await Goals.GetAsync(gone.Id)).Should().BeNull();
        notifications.Withdrawn.Should().Equal(pending.Id);
    }
}
