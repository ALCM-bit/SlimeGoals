using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;
using SlimeGoals.Core.Validation;
using SlimeGoals.Sync.Crypto;
using SlimeGoals.Testing;

namespace SlimeGoals.Infrastructure.Tests.Sync;

/// <summary>Aparelhos simulados contra um servidor falso (spec de sincronização §9): depois de sincronizar, todos terminam iguais.</summary>
public sealed class ConvergenceTests : IDisposable
{
    private const int MaxRounds = 30;
    private const int Seeds = 50;
    private const int Operations = 30;

    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly FakeSyncServer _server = new();
    private readonly List<SimulatedDevice> _devices = [];

    public void Dispose()
    {
        foreach (var device in _devices)
            device.Dispose();
    }

    private static Guid DeviceId(int number) => Guid.Parse($"00000000-0000-0000-0000-{number:x12}");

    private async Task<SimulatedDevice> DeviceAsync(TimeSpan skew = default, bool enable = true)
    {
        var number = _devices.Count + 1;
        var device = new SimulatedDevice($"aparelho {number}", DeviceId(number), _server, T0 + skew);
        _devices.Add(device);
        if (enable)
            await device.EnableAsync();
        return device;
    }

    private void Tick(TimeSpan by)
    {
        foreach (var device in _devices)
            device.Time.Advance(by);
    }

    private Task SettleAsync() => SettleAsync(_server, _devices);

    /// <summary>Roda ciclos em todos, em rodízio, até uma rodada não mudar nada. Não assentar é defeito.</summary>
    private static async Task SettleAsync(FakeSyncServer server, IReadOnlyList<SimulatedDevice> devices)
    {
        for (var round = 0; round < MaxRounds; round++)
        {
            var results = new List<SyncCycleResult>();
            foreach (var device in devices)
                results.Add(await device.SyncAsync());

            var latest = server.Records.Select(r => r.Seq).DefaultIfEmpty(0).Max();
            if (results.All(r => r == SyncCycleResult.Done)
                && devices.All(d => d.Pending == 0 && d.LastSeq >= latest && !d.FullResync))
                return;
        }

        var state = devices.Select(d => $"{d.Name}: seq {d.LastSeq}, fila [{d.Queue}], erro {d.LastError ?? "nenhum"}");
        throw new InvalidOperationException($"Não assentou em {MaxRounds} rodadas.\n{string.Join("\n", state)}");
    }

    private async Task ConvergeAsync()
    {
        await SettleAsync();
        ShouldBeEqual(_devices);
    }

    private static void ShouldBeEqual(IReadOnlyList<SimulatedDevice> devices)
    {
        var expected = devices[0].Snapshot().Lines;
        foreach (var device in devices.Skip(1))
            device.Snapshot().Lines.Should().Equal(expected, $"{device.Name} tem de terminar igual a {devices[0].Name}");
    }

    /// <summary>Envelopes que ainda têm conteúdo no servidor.</summary>
    private static List<SyncEnvelope> LiveBlobs(FakeSyncServer server)
    {
        var crypto = new PlainSyncCrypto();
        return server.Records.Where(r => r.Blob is not null).Select(r => crypto.Open(r.RecordKey, r.Blob!)).ToList();
    }

    /// <summary>O id da meta dona do conteúdo, venha ele de uma meta ou de um registro.</summary>
    private static Guid OwnerGoal(SyncEnvelope envelope) =>
        envelope.Kind == SyncRecordKind.Goal ? envelope.RecordId : SyncCodec.DecodeCheckin(envelope.Payload).GoalId;

    private void ServerShouldHoldNothingOf(Guid goalId) =>
        LiveBlobs(_server).Where(e => OwnerGoal(e) == goalId).Should().BeEmpty("excluir apaga o conteúdo no servidor (RS05)");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Offline_edits_on_two_devices_keep_the_newest(bool newestSyncsFirst)
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await ConvergeAsync();

        await a.EditGoal(goal.Id, "Mais velha");
        Tick(Minute);
        await b.EditGoal(goal.Id, "Mais nova");
        await (newestSyncsFirst ? b : a).SyncAsync();
        await (newestSyncsFirst ? a : b).SyncAsync();

        await ConvergeAsync();
        a.Snapshot().Goals.Should().ContainSingle().Which.Title.Should().Be("Mais nova");
    }

    /// <summary>A cripto de verdade, com a mesma chave de dados nos dois: o servidor só vê blobs cifrados e chaves HMAC.</summary>
    [Fact]
    public async Task Devices_converge_with_real_crypto()
    {
        var dataKey = AesGcmSyncCrypto.NewDataKey();
        using var cryptoA = new AesGcmSyncCrypto(dataKey);
        using var cryptoB = new AesGcmSyncCrypto(dataKey);
        var devices = new List<SimulatedDevice>
        {
            new("aparelho A", DeviceId(1), _server, T0, cryptoA),
            new("aparelho B", DeviceId(2), _server, T0, cryptoB),
        };
        _devices.AddRange(devices);
        foreach (var device in devices)
            await device.EnableAsync();
        var (a, b) = (devices[0], devices[1]);

        var goal = await a.CreateGoal("Beber água");
        await a.Adjust(goal.Id, 3);
        await SettleAsync(_server, devices);
        await b.EditGoal(goal.Id, "Beber mais água");
        await b.Adjust(goal.Id, 2);
        await SettleAsync(_server, devices);

        ShouldBeEqual(devices);
        a.Snapshot().Goals.Should().ContainSingle().Which.Title.Should().Be("Beber mais água");
        (await a.ShownTotal(goal.Id)).Should().Be(5);
        _server.Records.Should().NotBeEmpty().And.OnlyContain(r => r.RecordKey.Length == 43);
        var clear = System.Text.Encoding.UTF8.GetBytes("água");
        _server.Records.Select(r => r.Blob!).Where(blob => blob.AsSpan().IndexOf(clear) >= 0).Should().BeEmpty("o servidor não vê o título");
    }

    [Fact]
    public async Task Late_clock_edit_after_seeing_newer_wins()
    {
        var a = await DeviceAsync();
        var late = await DeviceAsync(skew: -TimeSpan.FromHours(3));
        var goal = await a.CreateGoal("Ler");
        await ConvergeAsync();
        await a.EditGoal(goal.Id, "Vista");
        await ConvergeAsync();

        Tick(Minute);
        await late.EditGoal(goal.Id, "Depois de ver");

        await ConvergeAsync();
        a.Snapshot().Goals.Should().ContainSingle().Which.Title.Should().Be("Depois de ver");
    }

    /// <summary>Dois relógios atrasados dão a mesma data às duas edições: a regra do 1 ms cria o empate.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tie_goes_to_the_larger_device_id(bool largerSyncsFirst)
    {
        var a = await DeviceAsync();
        var smaller = await DeviceAsync(skew: -TimeSpan.FromHours(3));
        var larger = await DeviceAsync(skew: -TimeSpan.FromHours(3));
        var goal = await a.CreateGoal("Ler");
        await ConvergeAsync();

        (await smaller.EditGoal(goal.Id, "Menor")).UpdatedAt.Should().Be((await larger.EditGoal(goal.Id, "Maior")).UpdatedAt);
        await (largerSyncsFirst ? larger : smaller).SyncAsync();
        await (largerSyncsFirst ? smaller : larger).SyncAsync();

        await ConvergeAsync();
        a.Snapshot().Goals.Should().ContainSingle().Which.Title.Should().Be("Maior");
    }

    /// <summary>Consequência aceita 2 (spec §3): sem um ver o outro, o relógio adiantado vence a edição mais recente.</summary>
    [Fact]
    public async Task Fast_clock_beats_the_most_recent_edit()
    {
        var a = await DeviceAsync();
        var fast = await DeviceAsync(skew: TimeSpan.FromHours(3));
        var goal = await a.CreateGoal("Ler");
        await ConvergeAsync();

        await fast.EditGoal(goal.Id, "Relógio adiantado");
        Tick(Minute);
        await a.EditGoal(goal.Id, "Mais recente de verdade");

        await ConvergeAsync();
        a.Snapshot().Goals.Should().ContainSingle().Which.Title.Should().Be("Relógio adiantado");
    }

    [Fact]
    public async Task Same_goal_marked_on_two_devices_shows_one()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Vacina", Measure.Completion, target: 1);
        await ConvergeAsync();

        await a.Adjust(goal.Id, 1);
        await b.Adjust(goal.Id, 1);

        await ConvergeAsync();
        foreach (var device in _devices)
        {
            device.RealTotal(goal.Id).Should().Be(2);
            (await device.ShownTotal(goal.Id)).Should().Be(1);
        }
    }

    /// <summary>Exemplo da spec §3: com total real −3, somar 2 em cada aparelho grava 5 em todos, e o total é 2, não 7.</summary>
    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public async Task Concurrent_compensations_total_two_not_seven(int count, bool newestSyncsFirst)
    {
        var devices = new List<SimulatedDevice>();
        for (var i = 0; i < count; i++)
            devices.Add(await DeviceAsync());
        var goal = await devices[0].CreateGoal("Ler");
        await devices[0].Adjust(goal.Id, 3);
        await ConvergeAsync();
        await devices[0].Adjust(goal.Id, -3);
        await devices[1].Adjust(goal.Id, -3);
        await ConvergeAsync();
        devices[0].RealTotal(goal.Id).Should().Be(-3);

        foreach (var device in devices)
        {
            Tick(Minute);
            await device.Adjust(goal.Id, 2);
        }
        foreach (var device in newestSyncsFirst ? Enumerable.Reverse(devices) : devices)
            await device.SyncAsync();

        await ConvergeAsync();
        foreach (var device in devices)
        {
            device.RealTotal(goal.Id).Should().Be(2);
            (await device.ShownTotal(goal.Id)).Should().Be(2);
        }
        var compensations = devices[0].Snapshot().Checkins.Where(c => c.Amount == 5).OrderBy(c => c.DoneAt).ToList();
        compensations.Should().HaveCount(count);
        compensations.Select(c => c.Superseded).Should().Equal([.. Enumerable.Repeat(true, count - 1), false]);
    }

    [Fact]
    public async Task Compensation_after_seeing_the_other_counts()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 3);
        await ConvergeAsync();
        await a.Adjust(goal.Id, -3);
        await b.Adjust(goal.Id, -3);
        await ConvergeAsync();
        await a.Adjust(goal.Id, 1); // compensa: grava 4
        await ConvergeAsync();
        await a.Adjust(goal.Id, -1);
        await b.Adjust(goal.Id, -1);
        await ConvergeAsync();
        a.RealTotal(goal.Id).Should().Be(-1);

        Tick(Minute);
        await b.Adjust(goal.Id, 2); // viu a de a: grava 3, e as duas contam

        await ConvergeAsync();
        var compensations = a.Snapshot().Checkins.Where(c => c.Period is not null).ToList();
        compensations.Should().HaveCount(2);
        compensations.Should().OnlyContain(c => !c.Superseded);
        a.RealTotal(goal.Id).Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_beats_offline_edit(bool editSyncsFirst)
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 1);
        await ConvergeAsync();

        await a.DeleteGoal(goal.Id);
        Tick(Minute);
        await b.EditGoal(goal.Id, "Edição mais nova que a exclusão");
        await (editSyncsFirst ? b : a).SyncAsync();
        await (editSyncsFirst ? a : b).SyncAsync();

        await ConvergeAsync();
        a.Snapshot().Lines.Should().BeEmpty();
        ServerShouldHoldNothingOf(goal.Id);
    }

    [Fact]
    public async Task Remote_deletion_withdraws_the_pending_reminder()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await ConvergeAsync();
        var pending = await b.AddPendingReminder(goal.Id);

        await a.DeleteGoal(goal.Id);

        await ConvergeAsync();
        b.Notifications.Withdrawn.Should().Equal(pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkin_added_offline_after_remote_deletion_disappears(bool checkinSyncsFirst)
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await ConvergeAsync();

        await a.DeleteGoal(goal.Id);
        Tick(Minute);
        await b.Adjust(goal.Id, 2);
        await (checkinSyncsFirst ? b : a).SyncAsync();
        await (checkinSyncsFirst ? a : b).SyncAsync();

        await ConvergeAsync();
        b.Snapshot().Lines.Should().BeEmpty();
        ServerShouldHoldNothingOf(goal.Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Deletion_with_send_in_flight_reaches_the_server(bool seenBefore, bool ackLost)
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        if (seenBefore)
            await ConvergeAsync();
        await a.EditGoal(goal.Id, "Em envio");
        await a.Adjust(goal.Id, 2);

        a.DuringNextPush = () => a.DeleteGoal(goal.Id);
        if (ackLost)
            _server.LoseNextAck();
        await a.SyncAsync();
        a.Snapshot().Lines.Should().BeEmpty();
        LiveBlobs(_server).Should().NotBeEmpty("o envio em andamento chegou ao servidor");

        await ConvergeAsync();
        b.Snapshot().Lines.Should().BeEmpty();
        ServerShouldHoldNothingOf(goal.Id);
    }

    [Fact]
    public async Task Deletion_after_lost_ack_reaches_the_server()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 2);
        _server.LoseNextAck();
        (await a.SyncAsync()).Should().Be(SyncCycleResult.Failed);

        await a.DeleteGoal(goal.Id);

        await ConvergeAsync();
        b.Snapshot().Lines.Should().BeEmpty();
        ServerShouldHoldNothingOf(goal.Id);
    }

    /// <summary>A edição feita com o envio no ar não foi enviada: a fila tem de guardá-la, e a cópia que volta no <c>pull</c> não pode apagá-la.</summary>
    [Fact]
    public async Task Edit_during_send_is_not_lost()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await ConvergeAsync();
        await a.EditGoal(goal.Id, "Em envio");
        a.DuringNextPush = () => a.EditGoal(goal.Id, "Feita durante o envio");

        await a.SyncAsync();

        await ConvergeAsync();
        b.Snapshot().Goals.Should().ContainSingle().Which.Title.Should().Be("Feita durante o envio");
    }

    [Fact]
    public async Task Lost_ack_does_not_duplicate()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 2);
        _server.LoseNextAck();
        (await a.SyncAsync()).Should().Be(SyncCycleResult.Failed);
        await a.EditGoal(goal.Id, "Depois do aceite perdido");

        await ConvergeAsync();
        b.Snapshot().Goals.Should().ContainSingle().Which.Title.Should().Be("Depois do aceite perdido");
        b.Snapshot().Checkins.Should().ContainSingle().Which.Amount.Should().Be(2);
        _server.Records.Should().HaveCount(2);
    }

    [Fact]
    public async Task New_device_receives_everything()
    {
        var a = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 2);
        await a.SyncAsync();
        await a.EditGoal(goal.Id, "Editada depois do registro"); // a meta passa para depois do registro no servidor
        await a.SyncAsync();

        var fresh = await DeviceAsync();
        fresh.PageSize = 1;

        await ConvergeAsync();
        fresh.RealTotal(goal.Id).Should().Be(2);
    }

    [Fact]
    public async Task Joining_with_data_on_both_devices_unites_everything()
    {
        var a = await DeviceAsync(enable: false);
        var b = await DeviceAsync(enable: false);
        var first = await a.CreateGoal("Ler");
        await a.Adjust(first.Id, 1);
        var second = await b.CreateGoal("Ler");
        await b.Adjust(second.Id, 2);

        await a.EnableAsync();
        await b.EnableAsync();

        await ConvergeAsync();
        a.Snapshot().Goals.Select(g => g.Id).Should().BeEquivalentTo([first.Id, second.Id], "une tudo, sem deduplicar");
        a.Snapshot().Checkins.Should().HaveCount(2);
    }

    [Fact]
    public async Task Disable_then_enable_does_not_duplicate()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 2);
        await ConvergeAsync();
        var records = _server.Records.Count;

        await a.DisableAsync();
        await a.EnableAsync();

        await ConvergeAsync();
        b.Snapshot().Goals.Should().ContainSingle();
        b.Snapshot().Checkins.Should().ContainSingle();
        _server.Records.Should().HaveCount(records);
    }

    [Fact]
    public async Task Deletion_made_while_disabled_reaches_the_device_on_enable()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        var gone = await a.CreateGoal("Excluída");
        await a.Adjust(gone.Id, 2);
        var kept = await a.CreateGoal("Fica");
        await ConvergeAsync();

        await a.DisableAsync();
        await b.DeleteGoal(gone.Id);
        await b.SyncAsync();
        await a.EnableAsync();

        await ConvergeAsync();
        a.Snapshot().Goals.Should().ContainSingle().Which.Id.Should().Be(kept.Id);
        a.Snapshot().Checkins.Should().BeEmpty();
        ServerShouldHoldNothingOf(gone.Id);
    }

    /// <summary>Protege o próprio teste: falha da ação não pode passar por queda de rede, que o motor engole.</summary>
    [Fact]
    public async Task Failure_of_the_action_during_send_reaches_the_test()
    {
        var a = await DeviceAsync();
        await a.CreateGoal("Ler");
        a.DuringNextPush = () => throw new FormatException("Erro do teste.");

        var act = () => a.SyncAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithInnerException<FormatException>();
    }

    [Fact]
    public async Task Device_behind_the_purge_runs_full_resync()
    {
        var a = await DeviceAsync();
        var behind = await DeviceAsync();
        var gone = await a.CreateGoal("Excluída");
        await a.Adjust(gone.Id, 1);
        var kept = await a.CreateGoal("Fica");
        await a.Adjust(kept.Id, 2);
        await ConvergeAsync();

        await a.DeleteGoal(gone.Id);
        await a.EditGoal(kept.Id, "Edição mais velha");
        await a.SyncAsync();
        Tick(Minute);
        await behind.EditGoal(kept.Id, "Edição local que espera envio");
        _server.PurgeBefore(_server.Records.Max(r => r.Seq));
        var before = _server.PullRequests.Count;

        await ConvergeAsync();
        _server.PullRequests.Skip(before).Should().Contain(p => p.Full && p.After == 0);
        var goal = behind.Snapshot().Goals.Should().ContainSingle().Which;
        goal.Id.Should().Be(kept.Id);
        goal.Title.Should().Be("Edição local que espera envio");
        behind.Snapshot().Checkins.Should().ContainSingle().Which.Amount.Should().Be(2);
    }

    /// <summary>
    /// Quem só enviou continua com <c>last_seq</c> 0 até baixar a própria meta, e pede sem <c>full</c>. É o 410 que o leva a
    /// ler tudo: a marca limpa não chegaria nunca, e a fila ficaria em conflito para sempre.
    /// </summary>
    [Theory]
    [InlineData("nada")]
    [InlineData("edição")]
    [InlineData("exclusão")]
    public async Task Device_that_only_sent_falls_behind_the_purge(string localChange)
    {
        var sender = await DeviceAsync();
        var b = await DeviceAsync();
        var goal = await sender.CreateGoal("Ler");
        await sender.SyncAsync();
        sender.LastSeq.Should().Be(0);
        await b.SyncAsync();
        await b.DeleteGoal(goal.Id);
        await b.SyncAsync();
        await b.SyncAsync();
        await b.CreateGoal("Depois"); // a limpeza não passa do maior seq: sem outra escrita, a marca ficaria
        await b.SyncAsync();
        _server.PurgeBefore(_server.Records.Max(r => r.Seq));
        _server.Records.Should().OnlyContain(r => r.Blob != null);

        if (localChange == "edição")
            await sender.EditGoal(goal.Id, "Edição local que espera envio");
        else if (localChange == "exclusão")
            await sender.DeleteGoal(goal.Id);

        await ConvergeAsync();
        sender.Snapshot().Goals.Should().ContainSingle().Which.Title.Should().Be("Depois");
        ServerShouldHoldNothingOf(goal.Id);
    }

    [Fact]
    public async Task Offline_edit_of_goal_deleted_behind_the_purge_does_not_return()
    {
        var a = await DeviceAsync();
        var behind = await DeviceAsync();
        var gone = await a.CreateGoal("Excluída");
        await a.Adjust(gone.Id, 1);
        await ConvergeAsync();
        await behind.EditGoal(gone.Id, "Edição local que espera envio");

        await a.DeleteGoal(gone.Id);
        await a.SyncAsync();
        await a.CreateGoal("Depois");
        await a.SyncAsync();
        _server.PurgeBefore(_server.Records.Max(r => r.Seq));

        await ConvergeAsync();
        behind.Snapshot().Goals.Should().NotContain(g => g.Id == gone.Id);
        ServerShouldHoldNothingOf(gone.Id);
    }

    /// <summary>
    /// A última escrita da conta foi uma exclusão, e a limpeza passou por ela. Se o aparelho terminasse abaixo da limpeza, todo
    /// ciclo daria 410, leria tudo e avisaria mudança, e o agendador rodaria de novo em 5 s, para sempre, sempre com Done.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Purged_last_deletion_does_not_keep_resyncing(bool afterReset)
    {
        var a = await DeviceAsync();
        var behind = afterReset ? await DeviceAsync() : null;
        var kept = await a.CreateGoal("Fica");
        var first = await a.CreateGoal("Excluída antes");
        var last = await a.CreateGoal("Excluída por último");
        await ConvergeAsync();

        await a.DeleteGoal(first.Id);
        await a.SyncAsync();
        await a.DeleteGoal(last.Id);
        await a.SyncAsync();
        _server.PurgeBefore(long.MaxValue);
        var top = _server.Records[^1].Seq;

        var device = behind ?? await DeviceAsync();
        var before = _server.PullRequests.Count;
        using var scheduler = device.NewScheduler();
        scheduler.Start();
        var resynced = _server.PullRequests.Count;
        _server.PullRequests.Skip(before).Should().Contain(p => p.Full && p.After == 0);

        for (var i = 0; i < 10; i++)
            device.Time.Advance(SyncScheduler.ChangeDelay);

        device.ScheduledCycles.Should().Be(2, "o aviso da leitura completa pede um ciclo, e esse não muda nada");
        device.Time.Advance(SyncScheduler.Period);
        device.ScheduledCycles.Should().Be(3, "depois disso só resta o ciclo de 15 min");
        _server.PullRequests.Skip(resynced).Should().OnlyContain(p => !p.Full && p.After == top, "nenhum outro 410");
        device.LastSeq.Should().Be(top);
        device.LastError.Should().BeNull();
        device.Snapshot().Goals.Should().ContainSingle().Which.Id.Should().Be(kept.Id);
        _server.Records.Where(r => r.Blob is null).Should().ContainSingle().Which.Seq.Should().Be(top, "só a marca mais nova sobrevive à limpeza");
    }

    private sealed class Coverage
    {
        public int Superseded;
        public int Compensations;
        public int LostAcks;
        public int DeletionsThenEdits;
        public int InFlightDeletions;
        public int Races;
        public int Purges;
        public int Reentries;
    }

    /// <summary>Tudo o que a sorteada precisa para operar e verificar no fim.</summary>
    private sealed class Run(FakeSyncServer server, List<SimulatedDevice> devices, Random random, Coverage coverage, bool withResets)
    {
        public FakeSyncServer Server => server;
        public List<SimulatedDevice> Devices => devices;
        public Random Random => random;
        public Coverage Coverage => coverage;
        public bool WithResets => withResets;
        public HashSet<Guid> Created { get; } = [];
        public HashSet<Guid> Deleted { get; } = [];
        public Dictionary<Guid, Guid> Checkins { get; } = [];
        public Dictionary<Guid, (DateTimeOffset At, Guid Device, string Row)> Winners { get; } = [];
        public List<SimulatedDevice> Late { get; } = [];
        public List<string> Log { get; } = [];
        public int NextTitle { get; set; }
        public long PurgedBefore { get; set; }
        public bool Reentered { get; set; }

        /// <summary>Quem excluiu a meta e ainda guarda a marca.</summary>
        public Dictionary<Guid, HashSet<SimulatedDevice>> Deleters { get; } = [];

        /// <summary>Metas com exclusão ainda não aceita quando quem excluiu saiu: a exclusão se perdeu com a fila.</summary>
        public HashSet<Guid> LostDeletions { get; } = [];

        /// <summary>Metas cuja marca o servidor já limpou.</summary>
        public HashSet<Guid> PurgedMarks { get; } = [];

        /// <summary>Únicas metas excluídas que podem voltar (spec §7): nenhum autor da exclusão guarda a marca, e ela se perdeu ou foi limpa.</summary>
        public HashSet<Guid> MayReturn() => [.. LostDeletions.Concat(PurgedMarks).Where(id => Deleters[id].Count == 0)];

        public void DeletedBy(SimulatedDevice device, Guid id)
        {
            Deleted.Add(id);
            LostDeletions.Remove(id);
            PurgedMarks.Remove(id);
            if (!Deleters.TryGetValue(id, out var deleters))
                Deleters[id] = deleters = [];
            deleters.Add(device);
        }

        /// <summary>Sair apaga a fila e as marcas: o que este aparelho excluiu e o servidor ainda guarda vivo fica sem exclusão.</summary>
        public void Leaving(SimulatedDevice device)
        {
            Reentered = true;
            var live = LiveBlobs(server).Where(e => e.Kind == SyncRecordKind.Goal).Select(e => e.RecordId).ToHashSet();
            foreach (var (id, deleters) in Deleters)
            {
                if (deleters.Remove(device) && live.Contains(id))
                    LostDeletions.Add(id);
            }
        }

        public SimulatedDevice Pick() => devices[random.Next(devices.Count)];

        public SimulatedDevice Other(SimulatedDevice device)
        {
            var others = devices.Where(d => d != device).ToList();
            return others[random.Next(others.Count)];
        }

        public Guid? PickGoal(SimulatedDevice device)
        {
            var goals = device.Snapshot().Goals;
            return goals.Count == 0 ? null : goals[random.Next(goals.Count)].Id;
        }

        public string Title() => $"t{++NextTitle}";

        /// <summary>
        /// Quem vence no fim é a maior data de todas as edições; empate, o maior id de aparelho (spec §3).
        /// Guarda a linha inteira de quem escreveu: cada aparelho baixa de volta o que enviou, então coluna que não viaja
        /// fica igual e errada em todos, e comparar os aparelhos entre si não veria.
        /// </summary>
        public void Saw(Goal goal, SimulatedDevice device)
        {
            if (!Winners.TryGetValue(goal.Id, out var best) || SyncMerge.LocalWins(goal.UpdatedAt, device.Id, best.At, best.Device))
                Winners[goal.Id] = (goal.UpdatedAt, device.Id, device.Snapshot().Goals.Single(g => g.Id == goal.Id).Row);
        }
    }

    [Fact]
    public async Task Random_operations_converge()
    {
        var coverage = new Coverage();
        var failures = new List<string>();
        for (var seed = 1; seed <= Seeds; seed++)
            await RunSeedAsync(seed, coverage, failures, withResets: false);

        failures.Should().BeEmpty();
        coverage.Superseded.Should().BePositive("a propriedade precisa passar por compensações concorrentes");
        coverage.LostAcks.Should().BePositive();
        coverage.DeletionsThenEdits.Should().BePositive();
        coverage.InFlightDeletions.Should().BePositive();
        coverage.Races.Should().BePositive();
    }

    /// <summary>A mesma propriedade, com limpeza de marcas e quem sai e volta. Só pode voltar a meta cujos autores da exclusão saíram com ela perdida ou limpa (spec §7).</summary>
    [Fact]
    public async Task Random_operations_with_purge_and_reentry_converge()
    {
        var coverage = new Coverage();
        var failures = new List<string>();
        for (var seed = 1; seed <= Seeds; seed++)
            await RunSeedAsync(seed, coverage, failures, withResets: true);

        failures.Should().BeEmpty();
        coverage.Purges.Should().BePositive();
        coverage.Reentries.Should().BePositive();
    }

    private static async Task RunSeedAsync(int seed, Coverage coverage, List<string> failures, bool withResets)
    {
        var random = new Random(seed);
        var server = new FakeSyncServer();
        var devices = new List<SimulatedDevice>();
        var run = new Run(server, devices, random, coverage, withResets);
        try
        {
            var count = random.Next(2, 4);
            for (var i = 1; i <= count; i++)
            {
                // Relógios divergentes: de 36 h atrasado a 36 h adiantado, cruzando dias e até a semana.
                var skew = i == 1 ? TimeSpan.Zero : TimeSpan.FromMinutes(random.Next(-36 * 60, 36 * 60));
                var device = new SimulatedDevice($"aparelho {i}", DeviceId(i), server, T0 + skew);
                devices.Add(device);
                device.PageSize = random.Next(3) == 0 ? random.Next(1, 4) : null;
                run.Log.Add($"{device.Name}: relógio {skew}, página {device.PageSize?.ToString() ?? "padrão"}");
                // O primeiro entra de saída; os outros às vezes só no meio, com o que já tinham.
                if (i == 1 || random.Next(3) != 0)
                    await device.EnableAsync();
                else
                    run.Late.Add(device);
            }

            for (var i = 0; i < Operations; i++)
            {
                var elapsed = TimeSpan.FromMilliseconds(random.Next(1, 120_000));
                foreach (var device in devices)
                    device.Time.Advance(elapsed);
                if (run.Late.Count > 0 && random.Next(8) == 0)
                    await EnterLateAsync(run, run.Late[0]);
                await OperateAsync(run);
            }

            while (run.Late.Count > 0)
                await EnterLateAsync(run, run.Late[0]);
            await SettleAsync(server, [.. devices.OrderBy(_ => random.Next())]);
            await VerifyAsync(run, seed);
        }
        catch (Exception e)
        {
            failures.Add($"semente {seed}: {e.Message}\n{string.Join("\n", run.Log)}");
        }
        finally
        {
            foreach (var device in devices)
                device.Dispose();
        }
    }

    private static async Task EnterLateAsync(Run run, SimulatedDevice device)
    {
        run.Late.Remove(device);
        await device.EnableAsync();
        run.Log.Add($"{device.Name}: entra pela primeira vez");
    }

    private static async Task OperateAsync(Run run)
    {
        var device = run.Pick();
        var roll = run.Random.Next(100);
        var goal = run.PickGoal(device);
        try
        {
            if (run.WithResets && roll < 6 && !run.Late.Contains(device))
                await ReenterAsync(run, device);
            else if (run.WithResets && roll < 10)
                await PurgeAsync(run);
            else if (goal is not { } id || roll < 12)
                await CreateAsync(run, device);
            else if (roll < 30)
                await EditAsync(run, device, id);
            else if (roll < 55)
                await AdjustAsync(run, device, id);
            else if (roll < 60)
                await CompensateConcurrentlyAsync(run, id);
            else if (roll < 66)
                await DeleteAsync(run, device, id);
            else if (roll < 74)
                await DeleteThenEditElsewhereAsync(run, device, id);
            else if (roll < 79)
                await DeleteDuringSendAsync(run, device, id);
            else if (roll < 85)
                await RaceAsync(run, device, id);
            else if (roll < 89)
                await EditDuringSendAsync(run, device, id);
            else if (roll < 95)
                await SyncAsync(run, device);
            else if (roll < 98)
                await LoseAckAsync(run, device);
            else
            {
                run.Server.FailNext(new IOException("Rede caiu."));
                await SyncAsync(run, device);
            }
        }
        catch (Exception e) when (e is GoalStateException or GoalValidationException)
        {
            run.Log.Add($"{device.Name}: recusado ({e.Message})");
        }
    }

    private static async Task CreateAsync(Run run, SimulatedDevice device)
    {
        var goal = run.Random.Next(3) == 0
            ? await device.CreateGoal(run.Title(), Measure.Completion, target: 1)
            : await device.CreateGoal(run.Title());
        run.Created.Add(goal.Id);
        run.Saw(goal, device);
        run.Log.Add($"{device.Name}: cria {goal.Id}");
    }

    /// <summary>Cada edição muda o título e mais um campo sorteado, para coluna que não viaja aparecer como diferença.</summary>
    private static async Task EditAsync(Run run, SimulatedDevice device, Guid id)
    {
        if (run.Random.Next(10) == 0)
        {
            run.Saw(await device.ToggleArchive(id), device);
            run.Log.Add($"{device.Name}: arquiva ou desarquiva {id}");
            return;
        }

        var field = run.Random.Next(7);
        var number = run.Random.Next(1, 28);
        var colors = Enum.GetValues<GoalColor>();
        run.Saw(await device.EditGoal(id, run.Title(), draft => field switch
        {
            0 when draft.Measure == Measure.Quantity => draft with { Target = number },
            1 => draft with { Icon = $"icone{number}" },
            2 => draft with { Color = colors[number % colors.Length] },
            3 => draft with { ReminderEveryMinutes = 30 + number, ActiveFrom = new TimeOnly(7, number), ActiveUntil = new TimeOnly(21, number) },
            4 => draft with { StartDate = new DateOnly(2026, 10, number), EndDate = new DateOnly(2027, 1, number) },
            5 => draft with { Recurrence = number % 2 == 0 ? Recurrence.Daily : Recurrence.Weekly },
            6 when draft.Measure == Measure.Quantity => draft with { Unit = $"u{number}" },
            _ => draft,
        }), device);
        run.Log.Add($"{device.Name}: edita {id} (campo {field})");
    }

    /// <summary>A meta pode já ter saído ou estar arquivada; recusa ali é uso normal, qualquer outra falha derruba o teste.</summary>
    private static async Task AllowingRefusalAsync(Run run, SimulatedDevice device, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception e) when (e is GoalStateException or GoalValidationException or GoalNotFoundException)
        {
            run.Log.Add($"{device.Name}: recusado ({e.Message})");
        }
    }

    /// <summary>Metade de ±1, que também vale em metas de conclusão.</summary>
    private static Task AdjustAsync(Run run, SimulatedDevice device, Guid id)
    {
        var size = run.Random.Next(2) == 0 ? 1 : run.Random.Next(2, 4);
        return AdjustAsync(run, device, id, run.Random.Next(2) == 0 ? size : -size);
    }

    private static async Task AdjustAsync(Run run, SimulatedDevice device, Guid id, int amount)
    {
        var before = device.Snapshot().Checkins.Select(c => c.Id).ToHashSet();
        await device.Adjust(id, amount, run.Random.Next(3) == 0 ? run.Title() : null);
        var added = device.Snapshot().Checkins.Single(c => !before.Contains(c.Id));
        run.Checkins[added.Id] = id;
        if (added.Period is not null)
            run.Coverage.Compensations++;
        run.Log.Add($"{device.Name}: ajusta {id} em {amount} (grava {added.Amount})");
    }

    /// <summary>Quem tem a meta tira tudo sem ver os outros; juntos, o total fica negativo, e cada um soma de novo às cegas.</summary>
    private static async Task CompensateConcurrentlyAsync(Run run, Guid id)
    {
        var holders = run.Devices.Where(d => d.Snapshot().Goals.Any(g => g.Id == id)).ToList();
        foreach (var holder in holders)
            await SyncAsync(run, holder);
        foreach (var holder in holders.Where(d => d.Snapshot().Goals.Any(g => g.Id == id)))
        {
            if (await holder.ShownTotal(id) == 0)
                await AdjustAsync(run, holder, id, 1);
        }
        foreach (var holder in run.Devices)
            await SyncAsync(run, holder);
        foreach (var holder in run.Devices.Where(d => d.Snapshot().Goals.Any(g => g.Id == id)))
        {
            if (await holder.ShownTotal(id) is > 0 and var total)
                await AdjustAsync(run, holder, id, -total);
        }
        foreach (var holder in run.Devices)
            await SyncAsync(run, holder);
        foreach (var holder in run.Devices.Where(d => d.Snapshot().Goals.Any(g => g.Id == id)))
        {
            run.Devices.ForEach(d => d.Time.Advance(TimeSpan.FromMilliseconds(run.Random.Next(1, 5_000))));
            await AdjustAsync(run, holder, id, 1);
        }
    }

    private static async Task DeleteAsync(Run run, SimulatedDevice device, Guid id)
    {
        await device.DeleteGoal(id);
        run.DeletedBy(device, id);
        run.Log.Add($"{device.Name}: exclui {id}");
    }

    /// <summary>A exclusão ainda não chegou ao outro aparelho, que continua editando e registrando.</summary>
    private static async Task DeleteThenEditElsewhereAsync(Run run, SimulatedDevice device, Guid id)
    {
        await DeleteAsync(run, device, id);
        var other = run.Other(device);
        if (other.Snapshot().Goals.All(g => g.Id != id))
            return;

        run.Coverage.DeletionsThenEdits++;
        await EditAsync(run, other, id);
        await AdjustAsync(run, other, id);
        if (run.Random.Next(2) == 0)
            await SyncAsync(run, other);
    }

    private static async Task DeleteDuringSendAsync(Run run, SimulatedDevice device, Guid id)
    {
        if (device.Pending == 0)
            await EditAsync(run, device, id);
        var loseAck = run.Random.Next(2) == 0;
        device.DuringNextPush = async () =>
        {
            if (device.Snapshot().Goals.All(g => g.Id != id))
                return; // o que acabou de baixar já a tirou
            await DeleteAsync(run, device, id);
            run.Coverage.InFlightDeletions++;
            if (loseAck)
                LoseAck(run);
        };
        await SyncAsync(run, device);
        device.DuringNextPush = null;
    }

    /// <summary>Entre baixar e enviar, outro aparelho edita a mesma meta e chega antes: é o caminho do conflito.</summary>
    private static async Task RaceAsync(Run run, SimulatedDevice device, Guid id)
    {
        var other = run.Other(device);
        await EditAsync(run, device, id);
        device.DuringNextPush = async () =>
        {
            run.Coverage.Races++;
            await AllowingRefusalAsync(run, other, () => EditAsync(run, other, id));
            await SyncAsync(run, other);
        };
        await SyncAsync(run, device);
        device.DuringNextPush = null;
    }

    /// <summary>A pessoa segue usando o app com o envio no ar.</summary>
    private static async Task EditDuringSendAsync(Run run, SimulatedDevice device, Guid id)
    {
        await EditAsync(run, device, id);
        device.DuringNextPush = () => AllowingRefusalAsync(run, device, async () =>
        {
            await EditAsync(run, device, id);
            if (run.Random.Next(2) == 0)
                await AdjustAsync(run, device, id, 1);
        });
        await SyncAsync(run, device);
        device.DuringNextPush = null;
    }

    /// <summary>Sai com ou sem a fila enviada, mexe sem conta e entra de novo.</summary>
    private static async Task ReenterAsync(Run run, SimulatedDevice device)
    {
        run.Coverage.Reentries++;
        if (run.Random.Next(2) == 0)
            await SyncAsync(run, device);
        run.Leaving(device);
        await device.DisableAsync();
        run.Log.Add($"{device.Name}: sai");
        // Uma recusa aqui não pode deixar o aparelho de fora para sempre.
        if (run.PickGoal(device) is { } id)
            await AllowingRefusalAsync(run, device, () => EditAsync(run, device, id));
        if (run.Random.Next(2) == 0)
            await CreateAsync(run, device);
        await device.EnableAsync();
        run.Log.Add($"{device.Name}: entra");
    }

    private static Task PurgeAsync(Run run)
    {
        var records = run.Server.Records;
        if (records.Count == 0)
            return Task.CompletedTask;
        run.Coverage.Purges++;
        // A limpeza segue o tempo, nunca volta para trás, e não passa do maior seq da conta.
        var seq = Math.Max(run.PurgedBefore, Math.Min(records[run.Random.Next(records.Count)].Seq + 1, records[^1].Seq));
        run.PurgedBefore = seq;
        var crypto = new PlainSyncCrypto();
        var marks = records.Where(r => r.Blob is null && r.Seq < seq).Select(r => r.RecordKey).ToHashSet();
        run.PurgedMarks.UnionWith(run.Deleted.Where(id => marks.Contains(crypto.RecordKey(id))));
        run.Server.PurgeBefore(seq);
        run.Log.Add($"limpa marcas abaixo de {seq}");
        return Task.CompletedTask;
    }

    private static async Task SyncAsync(Run run, SimulatedDevice device)
    {
        var result = await device.SyncAsync();
        run.Log.Add($"{device.Name}: sincroniza, {result}");
    }

    /// <summary>Só vale com o envio no ar: pedido antes, o aceite perdido poderia sobrar para o envio de outro aparelho, ou para nenhum.</summary>
    private static void LoseAck(Run run)
    {
        run.Server.LoseNextAck();
        run.Coverage.LostAcks++;
    }

    private static async Task LoseAckAsync(Run run, SimulatedDevice device)
    {
        device.DuringNextPush = () =>
        {
            LoseAck(run);
            return Task.CompletedTask;
        };
        await SyncAsync(run, device);
        device.DuringNextPush = null;
    }

    private static async Task VerifyAsync(Run run, int seed)
    {
        var devices = run.Devices;
        ShouldBeEqual(devices);
        var snapshot = devices[0].Snapshot();
        run.Coverage.Superseded += snapshot.Checkins.Count(c => c.Superseded);

        var blobs = LiveBlobs(run.Server);
        var mayReturn = run.MayReturn();
        var gone = run.Deleted.Except(mayReturn).ToHashSet();
        snapshot.Goals.Select(g => g.Id).Intersect(gone).Should().BeEmpty("meta excluída não volta");
        snapshot.Checkins.Select(c => c.GoalId).Intersect(gone).Should().BeEmpty("registro de meta excluída não volta");

        var live = run.Created.Except(run.Deleted).ToHashSet();
        snapshot.Goals.Select(g => g.Id).Where(live.Contains).Should().BeEquivalentTo(live, "nenhuma meta viva se perde");
        var liveCheckins = run.Checkins.Where(c => live.Contains(c.Value)).Select(c => c.Key).ToList();
        snapshot.Checkins.Where(c => live.Contains(c.GoalId)).Select(c => c.Id).Should()
            .BeEquivalentTo(liveCheckins, "nenhum registro de meta viva se perde");
        foreach (var goal in snapshot.Goals.Where(g => live.Contains(g.Id)))
            goal.Row.Should().Be(run.Winners[goal.Id].Row, "a edição vencedora chega inteira");

        // Questão em aberto, para decisão de quem escreve a spec: se todos os que sabem da exclusão saem antes de enviar o delete
        // de um registro que chegou depois dela, o conteúdo dele sobra no servidor, sem dono. A §7 fala de metas que voltam, não
        // dessa sobra, e a RS05 pede o contrário. O filtro não diz que isso está certo: só impede que a execução com saída falhe
        // por isso. Sem saída, nenhuma sobra é tolerada.
        var goals = snapshot.Goals.Select(g => g.Id).ToHashSet();
        var owned = run.Reentered ? blobs.Where(b => goals.Contains(OwnerGoal(b))).ToList() : blobs;
        owned.Select(OwnerGoal).Intersect(gone).Should().BeEmpty("excluir apaga o conteúdo no servidor (RS05)");
        owned.Select(b => b.RecordId).Should().BeEquivalentTo(
            goals.Concat(snapshot.Checkins.Select(c => c.Id)), "o servidor guarda só o que está vivo");
        blobs.Where(b => b.Kind == SyncRecordKind.Goal).Select(b => b.RecordId).Should().BeEquivalentTo(goals);

        CompensationsShouldFollowTheRule(snapshot);

        using var fresh = new SimulatedDevice("aparelho novo", DeviceId(99), run.Server, T0);
        fresh.PageSize = 2;
        await fresh.EnableAsync();
        await SettleAsync(run.Server, [fresh]);
        fresh.Snapshot().Lines.Should().Equal(snapshot.Lines, $"um aparelho novo recebe o mesmo (semente {seed})");
    }

    /// <summary>Todos iguais ainda pode ser todos errados: <c>superseded</c> tem de ser o que a regra calcula para o grupo inteiro.</summary>
    private static void CompensationsShouldFollowTheRule(DeviceSnapshot snapshot)
    {
        foreach (var group in snapshot.Checkins.Where(c => c.Period is not null).GroupBy(c => (c.GoalId, c.Period)))
        {
            var entries = group.Select(c => new CompensationEntry(c.Id, DateTimeOffset.Parse(c.DoneAt, System.Globalization.CultureInfo.InvariantCulture),
                System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(c.Seen ?? "[]")!));
            var superseded = CompensationRules.Superseded(entries);
            group.Where(c => c.Superseded).Select(c => c.Id).Should().BeEquivalentTo(superseded);
        }
    }
}
