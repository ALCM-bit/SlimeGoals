using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;
using SlimeGoals.Core.Validation;
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
        _server.PurgeBefore(_server.Records.Max(r => r.Seq) + 1);

        await ConvergeAsync();
        _server.PullRequests.Should().Contain(p => p.Full && p.After == 0);
        var goal = behind.Snapshot().Goals.Should().ContainSingle().Which;
        goal.Id.Should().Be(kept.Id);
        goal.Title.Should().Be("Edição local que espera envio");
        behind.Snapshot().Checkins.Should().ContainSingle().Which.Amount.Should().Be(2);
    }

    /// <summary>
    /// Quem só enviou continua com <c>last_seq</c> 0 até baixar a própria meta, e <c>after</c> 0 nunca recebe 410.
    /// A marca limpa não chegaria nunca, e a fila ficaria em conflito para sempre.
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
        _server.PurgeBefore(_server.Records.Max(r => r.Seq) + 1);

        if (localChange == "edição")
            await sender.EditGoal(goal.Id, "Edição local que espera envio");
        else if (localChange == "exclusão")
            await sender.DeleteGoal(goal.Id);

        await ConvergeAsync();
        sender.Snapshot().Lines.Should().BeEmpty();
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
        _server.PurgeBefore(_server.Records.Max(r => r.Seq) + 1);

        await ConvergeAsync();
        behind.Snapshot().Goals.Should().NotContain(g => g.Id == gone.Id);
        ServerShouldHoldNothingOf(gone.Id);
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
        public Dictionary<Guid, (DateTimeOffset At, Guid Device, string Title)> Winners { get; } = [];
        public List<SimulatedDevice> Late { get; } = [];
        public List<string> Log { get; } = [];
        public int NextTitle { get; set; }
        public long PurgedBefore { get; set; }

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

        /// <summary>Quem vence no fim é a maior data de todas as edições; empate, o maior id de aparelho (spec §3).</summary>
        public void Saw(Goal goal, SimulatedDevice device)
        {
            if (!Winners.TryGetValue(goal.Id, out var best) || SyncMerge.LocalWins(goal.UpdatedAt, device.Id, best.At, best.Device))
                Winners[goal.Id] = (goal.UpdatedAt, device.Id, goal.Title);
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

    /// <summary>A mesma propriedade, com limpeza de marcas e quem sai e volta. Sair apaga as marcas locais, então meta excluída pode voltar (spec §7).</summary>
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

    private static async Task EditAsync(Run run, SimulatedDevice device, Guid id)
    {
        run.Saw(await device.EditGoal(id, run.Title()), device);
        run.Log.Add($"{device.Name}: edita {id}");
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
        await device.Adjust(id, amount);
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
        run.Deleted.Add(id);
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
        device.DuringNextPush = () => DeleteAsync(run, device, id);
        run.Coverage.InFlightDeletions++;
        if (run.Random.Next(2) == 0)
        {
            run.Server.LoseNextAck();
            run.Coverage.LostAcks++;
        }
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
            if (other.Snapshot().Goals.Any(g => g.Id == id))
                await EditAsync(run, other, id);
            await SyncAsync(run, other);
        };
        await SyncAsync(run, device);
        device.DuringNextPush = null;
    }

    /// <summary>A pessoa segue usando o app com o envio no ar.</summary>
    private static async Task EditDuringSendAsync(Run run, SimulatedDevice device, Guid id)
    {
        await EditAsync(run, device, id);
        device.DuringNextPush = async () =>
        {
            await EditAsync(run, device, id);
            if (run.Random.Next(2) == 0)
                await AdjustAsync(run, device, id, 1);
        };
        await SyncAsync(run, device);
        device.DuringNextPush = null;
    }

    /// <summary>Sai com ou sem a fila enviada, mexe sem conta e entra de novo.</summary>
    private static async Task ReenterAsync(Run run, SimulatedDevice device)
    {
        run.Coverage.Reentries++;
        if (run.Random.Next(2) == 0)
            await SyncAsync(run, device);
        await device.DisableAsync();
        run.Log.Add($"{device.Name}: sai");
        if (run.PickGoal(device) is { } id)
            await EditAsync(run, device, id);
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
        // A limpeza segue o tempo: nunca volta para trás.
        var seq = Math.Max(run.PurgedBefore, records[run.Random.Next(records.Count)].Seq + 1);
        run.PurgedBefore = seq;
        run.Server.PurgeBefore(seq);
        run.Log.Add($"limpa marcas abaixo de {seq}");
        return Task.CompletedTask;
    }

    private static async Task SyncAsync(Run run, SimulatedDevice device)
    {
        var result = await device.SyncAsync();
        run.Log.Add($"{device.Name}: sincroniza, {result}");
    }

    private static async Task LoseAckAsync(Run run, SimulatedDevice device)
    {
        run.Server.LoseNextAck();
        run.Coverage.LostAcks++;
        await SyncAsync(run, device);
    }

    private static async Task VerifyAsync(Run run, int seed)
    {
        var devices = run.Devices;
        ShouldBeEqual(devices);
        var snapshot = devices[0].Snapshot();
        run.Coverage.Superseded += snapshot.Checkins.Count(c => c.Superseded);

        var blobs = LiveBlobs(run.Server);
        if (!run.WithResets)
        {
            snapshot.Goals.Select(g => g.Id).Intersect(run.Deleted).Should().BeEmpty("meta excluída não volta");
            snapshot.Checkins.Select(c => c.GoalId).Intersect(run.Deleted).Should().BeEmpty("registro de meta excluída não volta");
            blobs.Select(OwnerGoal).Intersect(run.Deleted).Should().BeEmpty("excluir apaga o conteúdo no servidor (RS05)");

            var liveGoals = run.Created.Except(run.Deleted).ToList();
            snapshot.Goals.Select(g => g.Id).Should().BeEquivalentTo(liveGoals, "nenhuma meta viva se perde");
            var liveCheckins = run.Checkins.Where(c => !run.Deleted.Contains(c.Value)).Select(c => c.Key).ToList();
            snapshot.Checkins.Select(c => c.Id).Should().BeEquivalentTo(liveCheckins, "nenhum registro de meta viva se perde");
            foreach (var goal in snapshot.Goals)
                goal.Title.Should().Be(run.Winners[goal.Id].Title, "a edição vencedora não se perde");
        }

        // Quem sai perde as marcas (spec §7): o registro de meta excluída pode sobrar no servidor, sem dono em nenhum aparelho.
        var goals = snapshot.Goals.Select(g => g.Id).ToHashSet();
        var owned = run.WithResets ? blobs.Where(b => goals.Contains(OwnerGoal(b))).ToList() : blobs;
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
