using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Infrastructure.Tests;
using SlimeGoals.Sync;
using SlimeGoals.Sync.Account;
using SlimeGoals.Testing;

namespace SlimeGoals.Server.Tests.Account;

/// <summary>
/// O coordenador contra o servidor de verdade: cada aparelho tem banco SQLite, cofre, relógio falso e rede próprios. O
/// relógio falso só anda quando o teste manda, então o único ciclo automático é o que o agendador roda ao ligar; o
/// resto vem de <see cref="ISyncAccount.SyncNow"/> ou <see cref="ISyncAccount.FlushAsync"/>. Ninguém dorme: quem
/// espera um ciclo espera o <see cref="ISyncAccount.StatusChanged"/>.
/// </summary>
public sealed class SyncCoordinatorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Password = "senha Pwqzx ação longa";
    private const string NewPassword = "outra senha Mnbvc maçã";
    private const string Expired = "A sessão deste aparelho foi encerrada. Entre de novo.";

    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

    private readonly ServerFactory _factory = new(postgres);
    private readonly List<Device> _devices = [];
    private readonly string _email = $"user{Guid.NewGuid():N}@exemplo.com";

    private Uri Server => _factory.Server.BaseAddress;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var device in _devices)
            await device.DisposeAsync();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Start_without_session_is_SignedOut_and_makes_no_request()
    {
        var a = NewDevice();
        await a.Goals.CreateAsync(Draft("Ler"));

        await a.Account.StartAsync();
        a.Account.SyncNow();

        var status = await a.Account.GetStatusAsync();
        status.State.Should().Be(AccountState.SignedOut);
        status.Email.Should().BeNull();
        (await a.Account.FlushAsync()).Should().Be(0);
        a.Net.ClientsCreated.Should().Be(0);
        a.Net.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_starts_syncing_and_reports_status()
    {
        await CreateAccountAsync();
        var a = NewDevice();
        await a.Goals.CreateAsync(Draft("Ler"));

        await a.Account.LoginAsync(Server, _email, Password, "pc");
        await WaitUntilAsync(a.Account, s => s.LastSyncAt is not null);

        var status = await a.Account.GetStatusAsync();
        status.State.Should().Be(AccountState.SignedIn);
        status.Email.Should().Be(_email);
        status.Server.Should().Be(Server);
        status.LastSyncAt.Should().Be(T0);
        status.Pending.Should().Be(0);
        status.LastError.Should().BeNull();
        a.Net.Calls.Should().Contain(c => c.StartsWith("POST /sync/push", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Start_with_session_resumes_sync()
    {
        var a = NewDevice();
        await SignUpAsync(a);
        await WaitUntilAsync(a.Account, s => s.LastSyncAt is not null);
        await a.Account.StopAsync();

        var b = NewDevice();
        await b.Account.LoginAsync(Server, _email, Password, "celular");
        await b.Goals.CreateAsync(Draft("Correr"));
        (await b.Account.FlushAsync()).Should().Be(0);

        // Outro coordenador sobre o mesmo banco e cofre, como o app aberto de novo.
        await a.ReplaceAccountAsync();
        await a.Account.StartAsync();
        await WaitUntilAsync(a.Account, _ => a.GoalTitles().Contains("Correr"));

        (await a.Account.GetStatusAsync()).State.Should().Be(AccountState.SignedIn);
    }

    [Fact]
    public async Task Start_with_locked_vault_is_VaultLocked_and_retry_recovers()
    {
        var a = NewDevice();
        await SignUpAsync(a);
        await a.ReplaceAccountAsync();
        a.Vault.Available = false;
        var clientsBefore = a.Net.ClientsCreated;

        await a.Account.StartAsync();

        var status = await a.Account.GetStatusAsync();
        status.State.Should().Be(AccountState.VaultLocked);
        status.Email.Should().BeNull();
        a.Net.ClientsCreated.Should().Be(clientsBefore, "sem cofre não há sessão, e sem sessão não há rede");

        a.Vault.Available = true;
        await a.Account.RetryVaultAsync();

        (await a.Account.GetStatusAsync()).State.Should().Be(AccountState.SignedIn);
        (await a.Account.FlushAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Start_with_unreadable_secret_is_SessionUnreadable_and_sign_out_clears()
    {
        var a = NewDevice();
        await a.Goals.CreateAsync(Draft("Ler"));
        await a.State.EnableAsync();
        await a.Vault.SetAsync("sync-session", "{ não é json"u8.ToArray());

        await a.Account.StartAsync();
        (await a.Account.GetStatusAsync()).State.Should().Be(AccountState.SessionUnreadable);

        await a.Account.SignOutAsync();

        var status = await a.Account.GetStatusAsync();
        status.State.Should().Be(AccountState.SignedOut);
        status.Pending.Should().Be(0);
        a.Vault.Names.Should().BeEmpty();
        (await a.State.GetAsync()).Enabled.Should().BeFalse();
        a.Net.ClientsCreated.Should().Be(0, "sem segredo legível não há servidor a quem falar");
    }

    [Fact]
    public async Task Revocation_moves_to_SessionExpired_and_reauth_sends_the_queue()
    {
        var a = NewDevice();
        await SignUpAsync(a);
        var goal = await a.Goals.CreateAsync(Draft("Ler"));
        (await a.Account.FlushAsync()).Should().Be(0);

        var b = NewDevice();
        await b.Account.LoginAsync(Server, _email, Password, "celular");
        (await b.Account.FlushAsync()).Should().Be(0);
        b.GoalTitles().Should().Equal("Ler");

        // B troca a senha: o token de A morre. A não sabe e edita offline.
        await b.Account.ChangePasswordAsync(Password, NewPassword);
        a.Time.Advance(TimeSpan.FromSeconds(1)); // só o ciclo pedido abaixo; o aviso de mudança espera 5 s
        await a.Goals.UpdateAsync(goal.Id, Draft("Ler mais"));
        (await a.Account.GetStatusAsync()).Pending.Should().Be(1);

        a.Account.SyncNow();
        await WaitUntilAsync(a.Account, s => s.State == AccountState.SessionExpired);

        var expired = await a.Account.GetStatusAsync();
        expired.Pending.Should().Be(1, "a fila sobrevive à revogação");
        expired.LastError.Should().Be(Expired);
        expired.Email.Should().Be(_email);
        expired.Server.Should().Be(Server);
        (await a.State.GetAsync()).Enabled.Should().BeTrue();
        var seq = (await a.State.GetAsync()).LastSeq;
        seq.Should().BeGreaterThan(0);
        (await a.Account.FlushAsync()).Should().Be(1, "sem motor, só conta a fila");

        a.Net.Calls.Clear();
        await a.Account.ReauthenticateAsync(NewPassword, "pc");
        (await a.Account.GetStatusAsync()).State.Should().Be(AccountState.SignedIn);
        (await a.Account.FlushAsync()).Should().Be(0);
        a.Net.Calls.Should().Contain(c => c.StartsWith($"GET /sync/pull?after={seq}&", StringComparison.Ordinal));
        a.Net.Calls.Should().NotContain(c => c.Contains("after=0&", StringComparison.Ordinal), "entrar de novo não baixa tudo");

        (await b.Account.FlushAsync()).Should().Be(0);
        b.GoalTitles().Should().Equal("Ler mais");
        a.GoalTitles().Should().Equal("Ler mais");
        (await a.Account.ListDevicesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task SignOut_waits_for_the_running_cycle_before_disabling()
    {
        var a = NewDevice();
        await a.Goals.CreateAsync(Draft("Ler"));
        var hold = a.Net.HoldNextPull();

        await SignUpAsync(a); // o primeiro ciclo fica preso no pull, com a meta na fila
        await hold.Held.Task.WaitAsync(WaitLimit);
        (await a.Account.GetStatusAsync()).Pending.Should().Be(1);

        var signOut = a.Account.SignOutAsync();
        await hold.Cancelled.Task.WaitAsync(WaitLimit);
        // A espera só dá tempo para um coordenador errado avançar; o certo nunca termina antes do Finish.
        (await Task.WhenAny(signOut, Task.Delay(TimeSpan.FromMilliseconds(500)))).Should().NotBeSameAs(signOut,
            "sair espera o ciclo cancelado terminar");
        a.Net.Events.Should().NotContain("disable");
        a.Net.Calls.Should().NotContain(c => c.StartsWith("POST /auth/logout", StringComparison.Ordinal));

        hold.Finish.SetResult();
        await signOut.WaitAsync(WaitLimit);

        a.Net.Events.Should().Equal("pull cancelado terminou", "disable");
        (await a.Account.GetStatusAsync()).Should().Match<AccountStatus>(s => s.State == AccountState.SignedOut && s.Pending == 0);
        a.Vault.Names.Should().BeEmpty();
        a.Net.ClientsDisposed.Should().Be(a.Net.ClientsCreated);

        // Depois de sair, nada mais vai à rede, nem pedido, nem relógio, nem mudança de dado.
        var calls = a.Net.Calls.Count;
        a.Account.SyncNow();
        await a.Goals.CreateAsync(Draft("Correr"));
        a.Time.Advance(TimeSpan.FromHours(1));
        a.Net.Calls.Should().HaveCount(calls);
        (await a.Account.GetStatusAsync()).Pending.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_logins_create_one_device()
    {
        await CreateAccountAsync();
        var a = NewDevice();

        var first = a.Account.LoginAsync(Server, _email, Password, "pc");
        var second = a.Account.LoginAsync(Server, _email, Password, "pc");
        var outcomes = await Task.WhenAll(Outcome(first), Outcome(second));

        outcomes.Count(e => e is null).Should().Be(1);
        outcomes.Single(e => e is not null).Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Saia da conta antes de entrar em outra.");
        (await a.Account.ListDevicesAsync()).Should().ContainSingle().Which.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Flush_reports_pending_left_when_server_is_down()
    {
        var a = NewDevice();
        await SignUpAsync(a);
        (await a.Account.FlushAsync()).Should().Be(0);

        a.Net.Down = true;
        var goal = await a.Goals.CreateAsync(Draft("Ler"));
        await a.Checkins.AdjustAsync(goal.Id, 2);

        (await a.Account.FlushAsync()).Should().Be(2);
        var status = await a.Account.GetStatusAsync();
        status.State.Should().Be(AccountState.SignedIn);
        status.LastError.Should().Be("Sem conexão com o servidor.");
    }

    [Fact]
    public async Task Revoking_the_current_device_is_refused()
    {
        var a = NewDevice();
        await FluentActions.Awaiting(() => a.Account.ListDevicesAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Entre na conta antes.");
        await FluentActions.Awaiting(() => a.Account.RevokeDeviceAsync(Guid.NewGuid()))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Entre na conta antes.");

        await SignUpAsync(a);
        var current = (await a.Account.ListDevicesAsync()).Single(d => d.IsCurrent);

        await FluentActions.Awaiting(() => a.Account.RevokeDeviceAsync(current.Id))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Para desconectar este aparelho, use Sair.");
        (await a.Account.ListDevicesAsync()).Should().ContainSingle(d => d.Id == current.Id);
        (await a.Account.GetStatusAsync()).State.Should().Be(AccountState.SignedIn);
    }

    [Fact]
    public async Task Failed_login_leaves_nothing_on()
    {
        await CreateAccountAsync();
        var a = NewDevice();

        await FluentActions.Awaiting(() => a.Account.LoginAsync(Server, _email, "senha errada e longa", "pc"))
            .Should().ThrowAsync<AccountApiException>();

        (await a.Account.GetStatusAsync()).State.Should().Be(AccountState.SignedOut);
        a.Vault.Names.Should().BeEmpty();
        (await a.State.GetAsync()).Enabled.Should().BeFalse();
        a.Net.ClientsCreated.Should().Be(1);
        a.Net.ClientsDisposed.Should().Be(1, "o cliente da operação que falhou também é descartado");

        // Sem agendador: nem pedido, nem mudança de dado, nem relógio levam a um pedido.
        var calls = a.Net.Calls.Count;
        a.Account.SyncNow();
        await a.Goals.CreateAsync(Draft("Ler"));
        a.Time.Advance(TimeSpan.FromHours(1));
        a.Net.Calls.Should().HaveCount(calls);
        a.Net.Calls.Should().NotContain(c => c.Contains("/sync/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Account_operations_run_off_the_caller_thread()
    {
        await CreateAccountAsync();
        var a = NewDevice();
        var context = new UiContext();

        Task login;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            login = a.Account.LoginAsync(Server, _email, Password, "pc");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await login.WaitAsync(WaitLimit);
        context.Uses.Should().Be(0, "nada da operação volta à thread da interface");
        (await a.Account.GetStatusAsync()).State.Should().Be(AccountState.SignedIn);
    }

    private Device NewDevice()
    {
        var device = new Device(_factory);
        _devices.Add(device);
        return device;
    }

    /// <summary>Cadastra e confirma pelo coordenador do aparelho, que fica com a conta ligada.</summary>
    private async Task SignUpAsync(Device device)
    {
        using var pending = await device.Account.RegisterAsync(Server, _email, Password);
        await device.Account.ConfirmAsync(pending, _factory.Email.LastToken, "pc");
    }

    /// <summary>Conta pronta no servidor, sem aparelho nenhum ligado a ela.</summary>
    private async Task CreateAccountAsync()
    {
        var owner = NewDevice();
        await SignUpAsync(owner);
        await owner.Account.SignOutAsync();
    }

    private static async Task<Exception?> Outcome(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    /// <summary>Reconfere a condição a cada aviso do coordenador; o limite só existe para o teste falhar em vez de travar.</summary>
    private static async Task WaitUntilAsync(ISyncAccount account, Func<AccountStatus, bool> condition)
    {
        using var limit = new CancellationTokenSource(WaitLimit);
        while (true)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, EventArgs e) => signal.TrySetResult();
            account.StatusChanged += OnChanged;
            try
            {
                if (condition(await account.GetStatusAsync()))
                    return;
                await signal.Task.WaitAsync(limit.Token);
            }
            finally
            {
                account.StatusChanged -= OnChanged;
            }
        }
    }

    private static GoalDraft Draft(string title) => new(title, Recurrence.Weekly, 10, "book", GoalColor.Purple, 60,
        new TimeOnly(8, 0), new TimeOnly(22, 0), WeekDays.All, null, null, Measure.Quantity, "vezes");

    /// <summary>Um aparelho: banco, cofre, relógio, rede e o coordenador, que pode ser trocado como num app aberto de novo.</summary>
    private sealed class Device : IAsyncDisposable
    {
        private readonly TestDatabase _db = new();
        private readonly ServerFactory _factory;
        private readonly DataChangeNotifier _changes = new();
        private readonly ReminderService _reminders;
        private readonly SyncLocalStore _store;

        public Device(ServerFactory factory)
        {
            _factory = factory;
            Time = new FakeTimeProvider(T0);
            State = new RecordingState(new SyncStateRepository(_db.Factory), Net);
            _store = new SyncLocalStore(_db.Factory);
            var goals = new GoalRepository(_db.Factory);
            var checkins = new CheckinRepository(_db.Factory);
            var timers = new TimerRepository(_db.Factory);
            _reminders = new ReminderService(goals, checkins, timers, new ReminderLogRepository(_db.Factory),
                new FakeNotificationService(), new PlainFormatter(), Time);
            Goals = new GoalService(goals, checkins, timers, Time, _changes, _reminders);
            Checkins = new CheckinService(goals, checkins, Time, _changes);
            Account = NewCoordinator();
        }

        public FakeTimeProvider Time { get; }
        public InMemorySecretVault Vault { get; } = new();
        public Network Net { get; } = new();
        public ISyncStateStore State { get; }
        public GoalService Goals { get; }
        public CheckinService Checkins { get; }
        public SyncCoordinator Account { get; private set; }

        public IReadOnlyList<string> GoalTitles() => _db.Anchor.Query<string>("SELECT title FROM goals ORDER BY title").ToList();

        /// <summary>Descarta o coordenador e cria outro sobre o mesmo banco e cofre.</summary>
        public async Task ReplaceAccountAsync()
        {
            await Account.DisposeAsync();
            Account = NewCoordinator();
        }

        public async ValueTask DisposeAsync()
        {
            await Account.DisposeAsync();
            _db.Dispose();
        }

        private SyncCoordinator NewCoordinator() => new(Vault, State, _store, _reminders, _changes, Time, server =>
        {
            Interlocked.Increment(ref Net.ClientsCreated);
            return _factory.CreateDefaultClient(server, new NetworkHandler(Net));
        });
    }

    /// <summary>A rede de um aparelho: o que saiu, a queda simulada e um pull que fica preso até o teste soltar.</summary>
    private sealed class Network
    {
        private PullHold? _hold;

        public int ClientsCreated;
        public int ClientsDisposed;

        /// <summary>Cada pedido como "MÉTODO caminho?consulta", na ordem em que saiu.</summary>
        public ConcurrentQueue<string> Calls { get; } = new();

        /// <summary>Marcos que o teste precisa pôr em ordem: o fim do pull preso e o desligar.</summary>
        public ConcurrentQueue<string> Events { get; } = new();

        public volatile bool Down;

        public PullHold HoldNextPull() => _hold = new PullHold();

        public PullHold? TakeHold() => Interlocked.Exchange(ref _hold, null);
    }

    /// <summary>
    /// O pull preso: <see cref="Held"/> quando chega, <see cref="Cancelled"/> quando o ciclo é cancelado, e só termina
    /// (cancelado) quando o teste completa <see cref="Finish"/>.
    /// </summary>
    private sealed class PullHold
    {
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class NetworkHandler(Network net) : DelegatingHandler
    {
        private int _disposed;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            net.Calls.Enqueue($"{request.Method} {request.RequestUri!.PathAndQuery}");
            if (net.Down)
                throw new HttpRequestException("Servidor fora do ar (simulado).");
            if (request.RequestUri.AbsolutePath == "/sync/pull" && net.TakeHold() is { } hold)
            {
                hold.Held.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    hold.Cancelled.TrySetResult();
                    await hold.Finish.Task;
                    net.Events.Enqueue("pull cancelado terminou");
                    throw;
                }
            }
            return await base.SendAsync(request, ct);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Increment(ref net.ClientsDisposed);
            base.Dispose(disposing);
        }
    }

    /// <summary>O estado de verdade, anotando quando desliga.</summary>
    private sealed class RecordingState(ISyncStateStore inner, Network net) : ISyncStateStore
    {
        public Task<SyncState> GetAsync(CancellationToken ct = default) => inner.GetAsync(ct);

        public Task EnableAsync(CancellationToken ct = default) => inner.EnableAsync(ct);

        public async Task DisableAsync(CancellationToken ct = default)
        {
            net.Events.Enqueue("disable");
            await inner.DisableAsync(ct);
        }
    }

    /// <summary>Thread de interface que conta cada uso; nada da operação pode passar por aqui.</summary>
    private sealed class UiContext : SynchronizationContext
    {
        private int _uses;

        public int Uses => Volatile.Read(ref _uses);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _uses);
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _uses);
            d(state);
        }
    }

    private sealed class PlainFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new(goal.Title, "", "Feito!");
    }
}
