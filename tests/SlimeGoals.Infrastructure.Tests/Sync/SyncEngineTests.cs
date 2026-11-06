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

        public async Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default)
        {
            Calls.Add("pull");
            Pulls.Add(after);
            // Um laço infinito no motor vira falha de teste, não um teste que nunca termina.
            if (Pulls.Count > 50)
                throw new InvalidOperationException("Laço: pulls demais.");
            if (BeforePull is { } before)
                await before(Pulls.Count);
            return await inner.PullAsync(after, limit, full, ct);
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

    /// <summary>Servidor com defeito: responde sempre a mesma página.</summary>
    private sealed class FixedPage(SyncPullPage page) : ISyncTransport
    {
        public int Pulls { get; private set; }

        public Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default)
        {
            if (++Pulls > 50)
                throw new InvalidOperationException("Laço: pulls demais.");
            return Task.FromResult(page);
        }

        public Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Servidor com defeito: nada a baixar, e o envio só responde o que foi combinado.</summary>
    private sealed class FixedPush(params PushAccepted[] accepted) : ISyncTransport
    {
        public int Pushes { get; private set; }

        public Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default) =>
            Task.FromResult(new SyncPullPage([], HasMore: false));

        public Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default)
        {
            if (++Pushes > 50)
                throw new InvalidOperationException("Laço: envios demais.");
            return Task.FromResult(new PushResult(accepted, []));
        }
    }

    /// <summary>Servidor com defeito: aceita o lote e ainda devolve uma chave que ninguém enviou.</summary>
    private sealed class UnknownAccepted(ISyncTransport inner) : ISyncTransport
    {
        public Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default) =>
            inner.PullAsync(after, limit, full, ct);

        public async Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default)
        {
            var result = await inner.PushAsync(items, ct);
            return result with { Accepted = [new PushAccepted("desconhecida", 99), .. result.Accepted] };
        }
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

    private async Task SendManyFromOtherAsync(int count)
    {
        for (var i = 0; i < count; i++)
            await SendFromOtherAsync(NewGoal());
    }

    /// <summary>Limpa até o fim do servidor: os registros vivos ficam todos abaixo da limpeza.</summary>
    private void PurgeUpToTheEnd() => _server.PurgeBefore(_server.Records.Max(r => r.Seq));

    /// <summary>A limpeza nunca passa do maior <c>seq</c>: uma escrita depois da marca é o que a deixa abaixo da limpeza, e ela sai.</summary>
    private async Task PurgeMarksAsync()
    {
        await SendFromOtherAsync(NewGoal("Depois da exclusão"));
        PurgeUpToTheEnd();
    }

    private static FakeSyncServer.PullRequest Pull(long after, bool full) => new(after, full);

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
    public async Task Device_that_only_sent_reconciles_only_after_the_reset()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await Engine().RunCycleAsync(); // enviou; last_seq continua 0 até baixar a própria meta
        await DeleteFromOtherAsync(goal.Id);
        await PurgeMarksAsync();
        var before = _server.PullRequests.Count;

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        (await Goals.GetAsync(goal.Id)).Should().BeNull();
        _server.PullRequests.Skip(before).Should().Equal(Pull(0, full: false), Pull(0, full: true));
    }

    /// <summary>Resposta vazia sem aviso de limpeza não diz nada sobre os registros: quem decide apagar é o reset do servidor.</summary>
    [Fact]
    public async Task Device_that_only_sent_removes_nothing_from_an_empty_server()
    {
        var goals = new[] { NewGoal("Água"), NewGoal("Ler") };
        var checkins = new CheckinRepository(_db.Factory);
        foreach (var goal in goals)
        {
            await Goals.AddAsync(goal);
            await checkins.AddAsync(new Checkin
            {
                Id = Guid.CreateVersion7(), GoalId = goal.Id, DoneAt = T0, Amount = 1, UpdatedAt = T0,
            });
        }
        await Engine().RunCycleAsync();
        var empty = new FakeSyncServer();

        var result = await new SyncEngine(State, Store, empty, _crypto, _gate, _changes, _time).RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        empty.PullRequests.Should().Equal(Pull(0, full: false));
        (await Goals.ListAsync(includeArchived: true)).Should().HaveCount(2);
        _db.Anchor.ExecuteScalar<int>("SELECT COUNT(*) FROM checkins").Should().Be(2);
        (await Store.GetStatusAsync()).FullResync.Should().BeFalse();
        _gate.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Device_without_records_reads_everything_past_the_purge()
    {
        await SendFromOtherAsync(NewGoal());
        PurgeUpToTheEnd();

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        _server.PullRequests.Should().Equal(Pull(0, full: true));
    }

    [Fact]
    public async Task Queued_edit_of_device_that_only_sent_is_sent_again_with_base_zero()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await Engine().RunCycleAsync();
        await Goals.UpdateAsync(goal with { Title = "Edição na fila", UpdatedAt = T0.AddMinutes(1) });
        await DeleteFromOtherAsync(goal.Id);
        await PurgeMarksAsync();

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        (await Store.GetStatusAsync()).Pending.Should().Be(0);
    }

    [Fact]
    public async Task Device_that_only_sent_keeps_what_the_server_returns()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await Engine().RunCycleAsync();

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        (await Goals.GetAsync(goal.Id)).Should().NotBeNull();
        (await Store.GetStatusAsync()).FullResync.Should().BeFalse();
        _server.PullRequests.Should().Equal(Pull(0, full: true), Pull(0, full: false));
    }

    [Fact]
    public async Task Deletion_mark_finds_the_item_queued_on_enable()
    {
        var goal = NewGoal();
        await SendFromOtherAsync(goal);
        await DeleteFromOtherAsync(goal.Id);
        await Goals.AddAsync(goal); // cópia que ficou aqui enquanto a sincronização estava desligada

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        (await Goals.GetAsync(goal.Id)).Should().BeNull();
        (await Store.GetStatusAsync()).Pending.Should().Be(0);
    }

    [Fact]
    public async Task Queue_larger_than_a_batch_has_all_keys_before_the_first_pull()
    {
        var goals = Enumerable.Range(0, SyncEngine.PushBatch + 1).Select(_ => NewGoal()).ToList();
        foreach (var goal in goals)
            await Goals.AddAsync(goal);
        _transport.BeforePull = async _ => (await Store.ReadUnkeyedAsync(1)).Should().BeEmpty();

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
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
        var engine = new SyncEngine(State, Store, new FixedPush(), _crypto, _gate, _changes, _time);

        var result = await engine.RunCycleAsync();

        result.Should().Be(SyncCycleResult.Incomplete);
    }

    [Fact]
    public async Task Unknown_accepted_key_is_traced_and_the_others_are_confirmed()
    {
        using var trace = new TraceCapture();
        await Goals.AddAsync(NewGoal());
        var engine = new SyncEngine(State, Store, new UnknownAccepted(_transport), _crypto, _gate, _changes, _time);

        var result = await engine.RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        (await Store.GetStatusAsync()).Pending.Should().Be(0);
        trace.Messages.Should().Contain(m => m.Contains("desconhecida"));
    }

    [Fact]
    public async Task Batch_with_only_unknown_accepted_keys_stops_sending()
    {
        await Goals.AddAsync(NewGoal());
        var transport = new FixedPush(new PushAccepted("desconhecida", 1));
        var engine = new SyncEngine(State, Store, transport, _crypto, _gate, _changes, _time);

        var result = await engine.RunCycleAsync();

        result.Should().Be(SyncCycleResult.Incomplete);
        transport.Pushes.Should().Be(SyncEngine.MaxAttempts);
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
    public async Task Second_reset_in_a_cycle_is_a_failure()
    {
        await SyncTwoGoalsAsync();
        _transport.Pulls.Clear();
        _transport.BeforePull = _ => throw new SyncResetRequiredException();

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        _transport.Pulls.Should().Equal(2, 0);
        (await Store.GetStatusAsync()).LastError.Should().Be(new SyncResetRequiredException().Message);
    }

    [Fact]
    public async Task Page_with_more_but_no_records_is_a_failure_and_does_not_loop()
    {
        var transport = new FixedPage(new SyncPullPage([], HasMore: true));
        var engine = new SyncEngine(State, Store, transport, _crypto, _gate, _changes, _time);

        var result = await engine.RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        transport.Pulls.Should().Be(1);
    }

    [Fact]
    public async Task Page_with_more_that_does_not_advance_is_a_failure_and_does_not_loop()
    {
        var transport = new FixedPage(new SyncPullPage([new RemoteRecord("k:x", 1, null)], HasMore: true));
        var engine = new SyncEngine(State, Store, transport, _crypto, _gate, _changes, _time);

        var result = await engine.RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        transport.Pulls.Should().Be(2);
        (await State.GetAsync()).LastSeq.Should().Be(1);
    }

    [Fact]
    public async Task Last_page_behind_last_seq_does_not_move_it_back()
    {
        _db.Anchor.Execute("UPDATE sync_state SET last_seq = 5");
        var transport = new FixedPage(new SyncPullPage([new RemoteRecord("k:x", 3, null)], HasMore: false));
        var engine = new SyncEngine(State, Store, transport, _crypto, _gate, _changes, _time);

        var result = await engine.RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        (await State.GetAsync()).LastSeq.Should().Be(5);
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
        await PurgeMarksAsync();
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
    public async Task New_device_reads_everything_below_the_purge()
    {
        await SendManyFromOtherAsync(SyncEngine.PullLimit + 2);
        PurgeUpToTheEnd();

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        _server.PullRequests.Should().Equal(Pull(0, true), Pull(SyncEngine.PullLimit, true));
        (await State.GetAsync()).LastSeq.Should().Be(SyncEngine.PullLimit + 2);
    }

    [Fact]
    public async Task Device_behind_the_purge_reads_everything_below_it()
    {
        var (gone, kept) = await SyncTwoGoalsAsync();
        await DeleteFromOtherAsync(gone.Id);
        await SendManyFromOtherAsync(SyncEngine.PullLimit + 1);
        PurgeUpToTheEnd();
        var before = _server.PullRequests.Count;

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        // A primeira página traz a meta mantida (seq 2) e 199 novas (seq 4 em diante).
        _server.PullRequests.Skip(before).Should().Equal(Pull(2, false), Pull(0, true), Pull(SyncEngine.PullLimit + 2, true));
        (await Goals.GetAsync(gone.Id)).Should().BeNull();
        (await Goals.GetAsync(kept.Id)).Should().NotBeNull();
        (await Store.GetStatusAsync()).FullResync.Should().BeFalse();
    }

    [Fact]
    public async Task Reset_keeps_reading_in_full_for_the_rest_of_the_cycle()
    {
        var (gone, _) = await SyncTwoGoalsAsync();
        var clashing = NewGoal("Local", T0.AddHours(1));
        await Goals.AddAsync(clashing);
        await DeleteFromOtherAsync(gone.Id);
        PurgeUpToTheEnd();
        _transport.BeforePush = call => call == 1 ? SendFromOtherAsync(clashing with { UpdatedAt = T0 }) : Task.CompletedTask;
        _transport.Pushes.Clear();
        var before = _server.PullRequests.Count;

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Done);
        _server.PullRequests.Skip(before).Should().Equal(Pull(2, false), Pull(0, true), Pull(3, true));
    }

    [Fact]
    public async Task Interrupted_full_resync_finishes_next_cycle()
    {
        var (gone, kept) = await SyncTwoGoalsAsync();
        await DeleteFromOtherAsync(gone.Id);
        await SendManyFromOtherAsync(SyncEngine.PullLimit + 1);
        PurgeUpToTheEnd();
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
        _server.PullRequests[^1].Should().Be(Pull(stoppedAt, true));
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
    public async Task Failure_after_the_commit_still_raises_the_notice()
    {
        await SendFromOtherAsync(NewGoal());
        _gate.During = () => throw new InvalidOperationException("retirada falhou");

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        _notices.Should().Be(1);
    }

    /// <summary>Sem COMMIT, nada mudou: o aviso só cortaria a espera da falha para 5 s.</summary>
    [Fact]
    public async Task Failure_of_the_apply_raises_none()
    {
        await SendFromOtherAsync(NewGoal());
        _db.Anchor.Execute("CREATE TRIGGER no_goals BEFORE INSERT ON goals BEGIN SELECT RAISE(ABORT, 'recusada'); END");

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        _notices.Should().Be(0);
        (await State.GetAsync()).LastSeq.Should().Be(0);
    }

    [Fact]
    public async Task Failure_after_the_full_resync_removal_still_raises_the_notice()
    {
        var (gone, _) = await SyncTwoGoalsAsync();
        await DeleteFromOtherAsync(gone.Id);
        await PurgeMarksAsync();
        _notices = 0;
        var runs = 0;
        _gate.During = () =>
        {
            if (++runs == 2)
                throw new InvalidOperationException("retirada falhou");
        };

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        _notices.Should().Be(2, "uma pela página e uma pelo que saiu no fim");
    }

    [Fact]
    public async Task Failure_without_records_raises_none()
    {
        _gate.During = () => throw new InvalidOperationException("portão falhou");

        var result = await Engine().RunCycleAsync();

        result.Should().Be(SyncCycleResult.Failed);
        _notices.Should().Be(0);
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
