using System.Net;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SlimeGoals.Core.Sync;
using SlimeGoals.Infrastructure.Tests.Sync;
using SlimeGoals.Server.Sync;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync;
using SlimeGoals.Testing;

namespace SlimeGoals.Server.Tests.Sync;

/// <summary>O motor de sincronização do app, com aparelhos simulados, contra o servidor de verdade e o PostgreSQL.</summary>
public sealed class EngineOnRealServerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int MaxRounds = 30;

    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly ServerFactory _factory = new(postgres);
    private readonly List<SimulatedDevice> _devices = [];
    private TestAccount _account = null!;

    public async ValueTask InitializeAsync() => _account = await TestAccount.CreateAsync(_factory);

    public async ValueTask DisposeAsync()
    {
        foreach (var device in _devices)
            device.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>Tudo que o servidor respondeu a um aparelho, na ordem.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        public List<(Uri Uri, HttpStatusCode Status)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            Calls.Add((request.RequestUri!, response.StatusCode));
            return response;
        }
    }

    private sealed record Device(SimulatedDevice Sim, TestDevice Login, RecordingHandler Calls);

    private async Task<Device> DeviceAsync(bool enable = true, int? pageSize = null)
    {
        var number = _devices.Count + 1;
        var login = number == 1 ? _account.First : await _account.LoginAsync($"aparelho {number}");
        var calls = new RecordingHandler();
        var http = _factory.CreateDefaultClient(calls);
        var transport = new HttpSyncTransport(http, _ => ValueTask.FromResult<string?>(login.Token));
        var sim = new SimulatedDevice($"aparelho {number}", Guid.Parse($"00000000-0000-0000-0000-{number:x12}"), transport, T0);
        sim.PageSize = pageSize;
        _devices.Add(sim);
        if (enable)
            await sim.EnableAsync();
        return new Device(sim, login, calls);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<long> LatestAsync()
    {
        await using var connection = await OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT next_seq - 1 FROM accounts WHERE email = @Email", new { _account.Email });
    }

    /// <summary>Roda ciclos em todos, em rodízio, até uma rodada não mudar nada. Não assentar é defeito.</summary>
    private async Task SettleAsync()
    {
        for (var round = 0; round < MaxRounds; round++)
        {
            var results = new List<SyncCycleResult>();
            foreach (var device in _devices)
                results.Add(await device.SyncAsync());

            var latest = await LatestAsync();
            if (results.All(r => r == SyncCycleResult.Done)
                && _devices.All(d => d.Pending == 0 && d.LastSeq >= latest && !d.FullResync))
                return;
        }

        var state = _devices.Select(d => $"{d.Name}: seq {d.LastSeq}, fila [{d.Queue}], erro {d.LastError ?? "nenhum"}");
        throw new InvalidOperationException($"Não assentou em {MaxRounds} rodadas.\n{string.Join("\n", state)}");
    }

    private void ShouldBeEqual()
    {
        var expected = _devices[0].Snapshot().Lines;
        foreach (var device in _devices.Skip(1))
            device.Snapshot().Lines.Should().Equal(expected, $"{device.Name} tem de terminar igual a {_devices[0].Name}");
    }

    [Fact]
    public async Task Devices_converge_after_editing_offline()
    {
        var a = (await DeviceAsync()).Sim;
        var b = (await DeviceAsync()).Sim;
        var c = (await DeviceAsync()).Sim;
        var shared = await a.CreateGoal("Ler");
        await SettleAsync();

        // Cada ponta edita a sua meta e registra, sem falar com o servidor.
        b.Time.Advance(TimeSpan.FromMinutes(1));
        await b.EditGoal(shared.Id, "Ler mais");
        await b.Adjust(shared.Id, 2);
        await c.CreateGoal("Correr");
        a.Time.Advance(TimeSpan.FromMinutes(2));
        await a.Adjust(shared.Id, 3);
        await a.CreateGoal("Meditar");

        await SettleAsync();

        ShouldBeEqual();
        a.Snapshot().Goals.Should().HaveCount(3);
        a.Snapshot().Checkins.Should().HaveCount(2);
        a.Snapshot().Goals.Should().Contain(g => g.Title == "Ler mais");
    }

    [Fact]
    public async Task New_device_receives_everything()
    {
        var a = (await DeviceAsync()).Sim;
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 4);
        await a.CreateGoal("Correr");
        await SettleAsync();

        var fresh = (await DeviceAsync(pageSize: 2)).Sim;
        await SettleAsync();

        ShouldBeEqual();
        fresh.Snapshot().Goals.Should().HaveCount(2);
        fresh.Snapshot().Checkins.Should().HaveCount(1);
    }

    [Fact]
    public async Task Deletion_beats_a_concurrent_edit_and_erases_the_blob()
    {
        var a = (await DeviceAsync()).Sim;
        var b = (await DeviceAsync()).Sim;
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 1);
        await SettleAsync();

        b.Time.Advance(TimeSpan.FromMinutes(5));
        await b.EditGoal(goal.Id, "Ler outra coisa");
        await b.Adjust(goal.Id, 2);
        a.Time.Advance(TimeSpan.FromMinutes(1));
        await a.DeleteGoal(goal.Id);

        await SettleAsync();

        ShouldBeEqual();
        a.Snapshot().Goals.Should().BeEmpty();
        a.Snapshot().Checkins.Should().BeEmpty();
        await using var connection = await OpenAsync();
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM records WHERE deleted_at IS NOT NULL")).Should().BeGreaterThan(0);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM records WHERE blob IS NOT NULL")).Should().Be(0,
            "excluir apaga o conteúdo no servidor (RS05)");
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM records WHERE (blob IS NULL) <> (deleted_at IS NOT NULL)")).Should().Be(0);
    }

    [Fact]
    public async Task Conflict_in_a_batch_resolves_in_the_next_cycle()
    {
        var a = (await DeviceAsync()).Sim;
        var b = (await DeviceAsync()).Sim;
        var goal = await a.CreateGoal("Ler");
        await SettleAsync();

        // As duas pontas editam a mesma meta, e criam registros no mesmo lote; o envio da segunda tem base velha.
        a.Time.Advance(TimeSpan.FromMinutes(1));
        await a.EditGoal(goal.Id, "Ler A");
        await a.Adjust(goal.Id, 1);
        b.Time.Advance(TimeSpan.FromMinutes(2));
        await b.EditGoal(goal.Id, "Ler B");
        await b.Adjust(goal.Id, 5);

        (await a.SyncAsync()).Should().Be(SyncCycleResult.Done);
        (await b.SyncAsync()).Should().NotBe(SyncCycleResult.Failed, "um conflito não é falha");
        (await b.SyncAsync()).Should().Be(SyncCycleResult.Done);

        await SettleAsync();

        ShouldBeEqual();
        a.Snapshot().Goals.Single().Title.Should().Be("Ler B", "a edição mais nova vence");
        a.Snapshot().Checkins.Should().HaveCount(2);
    }

    [Fact]
    public async Task Device_behind_the_purge_resyncs_and_stops_asking()
    {
        var a = (await DeviceAsync()).Sim;
        var behind = await DeviceAsync();
        var keep = await a.CreateGoal("Fica");
        var gone = await a.CreateGoal("Some");
        await a.Adjust(gone.Id, 1);
        await SettleAsync();

        // Só o primeiro aparelho vê a exclusão; o outro fica para trás do ponto da limpeza.
        await a.DeleteGoal(gone.Id);
        a.Time.Advance(TimeSpan.FromMinutes(1));
        await a.Adjust(keep.Id, 1);
        (await a.SyncAsync()).Should().Be(SyncCycleResult.Done);
        await using (var connection = await OpenAsync())
        {
            await connection.ExecuteAsync("UPDATE records SET deleted_at = @old WHERE deleted_at IS NOT NULL",
                new { old = _factory.Time.GetUtcNow().AddDays(-181) });
        }
        await _factory.Services.GetRequiredService<RecordPurger>().RunAsync(CancellationToken.None);

        behind.Calls.Calls.Clear();
        (await behind.Sim.SyncAsync()).Should().Be(SyncCycleResult.Done);
        behind.Calls.Calls.Count(c => c.Status == HttpStatusCode.Gone).Should().Be(1, "o 410 vem uma vez só");

        await SettleAsync();
        ShouldBeEqual();
        behind.Sim.Snapshot().Goals.Should().ContainSingle(g => g.Title == "Fica");

        behind.Calls.Calls.Clear();
        a.Time.Advance(TimeSpan.FromMinutes(1));
        await a.Adjust(keep.Id, 1);
        await SettleAsync();
        await behind.Sim.SyncAsync();

        behind.Calls.Calls.Should().NotBeEmpty();
        behind.Calls.Calls.Select(c => c.Uri.Query).Should().NotContain(q => q.Contains("full=1"));
        behind.Calls.Calls.Should().NotContain(c => c.Status == HttpStatusCode.Gone);
        ShouldBeEqual();
    }

    [Fact]
    public async Task Empty_account_removes_nothing_from_a_device_that_only_sent()
    {
        var a = (await DeviceAsync()).Sim;
        var goal = await a.CreateGoal("Ler");
        await a.Adjust(goal.Id, 2);
        await SettleAsync();
        var before = a.Snapshot().Lines;

        await using (var connection = await OpenAsync())
        {
            await connection.ExecuteAsync("DELETE FROM records");
        }

        await SettleAsync();

        a.Snapshot().Lines.Should().Equal(before);
        a.Pending.Should().Be(0);
    }

    [Fact]
    public async Task Revoked_device_fails_the_cycle_and_keeps_its_queue()
    {
        var a = await DeviceAsync();
        var b = await DeviceAsync();
        await a.Sim.CreateGoal("Ler");
        await SettleAsync();

        var revoke = await a.Login.Client.DeleteAsync($"/devices/{b.Login.DeviceId}");
        revoke.EnsureSuccessStatusCode();

        await b.Sim.CreateGoal("Correr");
        var queue = b.Sim.Queue;
        queue.Should().NotBeEmpty();

        (await b.Sim.SyncAsync()).Should().Be(SyncCycleResult.Failed);

        b.Sim.LastError.Should().NotBeNullOrEmpty();
        b.Sim.Queue.Should().Be(queue);
        b.Sim.Pending.Should().BeGreaterThan(0);
    }
}
