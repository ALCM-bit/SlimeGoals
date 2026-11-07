using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Sync;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Sync;

/// <summary>
/// Dono da sessão, do motor e do agendador (decisões 6 a 13 da 9.5a). Toda operação, o início e a parada passam por um
/// único portão (<c>_ops</c>) e rodam fora da thread de quem chama. Parar é sempre: cancelar o agendador, esperar o
/// ciclo em andamento e só então descartar agendador, sessão e cliente HTTP.
/// </summary>
/// <remarks>
/// <para>Sem sessão no cofre, nada de rede é criado (RS02): <paramref name="createHttp"/> só é chamado com uma sessão
/// aberta ou numa operação de conta pedida pela pessoa, e cada cliente criado é descartado pelo coordenador.</para>
/// <para><see cref="StatusChanged"/> nunca dispara com o portão na mão: um assinante pode chamar qualquer operação de
/// dentro do aviso sem travar. <see cref="GetStatusAsync"/> e <see cref="SyncNow"/> não passam pelo portão: só leem
/// uma foto imutável do estado, e a tela não espera um Argon2id para mostrar o status.</para>
/// </remarks>
/// <param name="createHttp">Cliente com <c>BaseAddress</c> no servidor pedido; o coordenador fica dono dele.</param>
public sealed class SyncCoordinator(ISecretVault vault, ISyncStateStore state, ISyncLocalStore store,
    IRemoteDeletionGate gate, DataChangeNotifier changes, TimeProvider time, Func<Uri, HttpClient> createHttp)
    : ISyncAccount, IAsyncDisposable
{
    private const string SignInFirst = "Entre na conta antes.";
    private const int FlushAttempts = 3;

    private readonly SemaphoreSlim _ops = new(1, 1);
    private volatile Snapshot _snapshot = new(AccountState.SignedOut, null, null);
    private volatile EngineRun? _run; // só muda com _ops na mão

    public event EventHandler? StatusChanged;

    public Task<AccountStatus> GetStatusAsync(CancellationToken ct = default) => Task.Run(async () =>
    {
        var snapshot = _snapshot;
        var local = await store.GetStatusAsync(ct).ConfigureAwait(false);
        return new AccountStatus(snapshot.State, snapshot.Email, snapshot.Server, local.LastSyncAt, local.Pending, local.LastError);
    }, ct);

    public Task StartAsync(CancellationToken ct = default) => RunOpAsync(async () =>
    {
        if (_run is null)
            await OpenAsync(ct).ConfigureAwait(false);
    }, ct);

    public Task StopAsync() => RunOpAsync(async () =>
    {
        if (_run is { } run)
            await StopRunAsync(run).ConfigureAwait(false);
    }, CancellationToken.None);

    public ValueTask DisposeAsync() => new(StopAsync());

    public Task<PendingRegistration> RegisterAsync(Uri server, string email, string password, CancellationToken ct = default) =>
        RunOpAsync(() => WithAccountAsync(RequireServer(server), a => a.RegisterAsync(email, password, ct)), ct);

    public Task ConfirmAsync(PendingRegistration pending, string code, string deviceName, CancellationToken ct = default) =>
        RunOpAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(pending);
            await WithAccountAsync(pending.Server, a => a.ConfirmAsync(pending, code, deviceName, ct)).ConfigureAwait(false);
            await OpenAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task LoginAsync(Uri server, string email, string password, string deviceName, CancellationToken ct = default) =>
        RunOpAsync(async () =>
        {
            await WithAccountAsync(RequireServer(server), a => a.LoginAsync(email, password, deviceName, ct)).ConfigureAwait(false);
            await OpenAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task<string> RecoverAsync(Uri server, string email, string recoveryCode, string newPassword, string deviceName,
        CancellationToken ct = default) => RunOpAsync(async () =>
        {
            var code = await WithAccountAsync(RequireServer(server),
                a => a.RecoverAsync(email, recoveryCode, newPassword, deviceName, ct)).ConfigureAwait(false);
            await OpenAsync(ct).ConfigureAwait(false);
            return code;
        }, ct);

    /// <summary>
    /// Só em <see cref="AccountState.SessionExpired"/>. O serviço de conta nasce com o servidor gravado na sessão do
    /// cofre: a senha nunca vai a outro endereço.
    /// </summary>
    public Task ReauthenticateAsync(string password, string deviceName, CancellationToken ct = default) => RunOpAsync(async () =>
    {
        if (_snapshot.State != AccountState.SessionExpired)
            throw new InvalidOperationException("A sessão deste aparelho não foi encerrada.");
        Uri server;
        using (var session = await AccountService.OpenSessionAsync(vault, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(SignInFirst))
        {
            server = session.Server;
        }

        await WithAccountAsync(server, a => a.ReauthenticateAsync(password, deviceName, ct)).ConfigureAwait(false);
        await OpenAsync(ct).ConfigureAwait(false);
    }, ct);

    public Task RetryVaultAsync(CancellationToken ct = default) => RunOpAsync(async () =>
    {
        if (_run is null && _snapshot.State == AccountState.VaultLocked)
            await OpenAsync(ct).ConfigureAwait(false);
    }, ct);

    public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default) =>
        RunOpAsync(() => RequireRun().Account.ChangePasswordAsync(currentPassword, newPassword, ct), ct);

    public Task<int> FlushAsync(CancellationToken ct = default) => RunOpAsync(async () =>
    {
        if (_run is { } run)
        {
            var result = await run.Engine.RunCycleAsync(ct).ConfigureAwait(false);
            // Skipped: o agendador está no meio de um ciclo. Espera ele e roda de novo, para a fila sair agora.
            for (var attempt = 1; result == SyncCycleResult.Skipped && attempt < FlushAttempts; attempt++)
            {
                await WaitQuietlyAsync(run.CurrentCycle).ConfigureAwait(false);
                result = await run.Engine.RunCycleAsync(ct).ConfigureAwait(false);
            }

            if (result == SyncCycleResult.Unauthorized)
                await ExpireCoreAsync(run).ConfigureAwait(false);
        }

        return (await store.GetStatusAsync(ct).ConfigureAwait(false)).Pending;
    }, ct);

    /// <summary>
    /// Para o motor e espera o ciclo antes de sair: nenhum pedido do ciclo sai depois de limpar a fila. Com o segredo
    /// ilegível (ou faltando), não há servidor nem token: só a limpeza local, sem rede.
    /// </summary>
    public Task SignOutAsync(CancellationToken ct = default) => RunOpAsync(async () =>
    {
        var run = _run;
        var hadRun = run is not null;
        if (run is not null)
            await StopRunAsync(run).ConfigureAwait(false);
        try
        {
            Uri? server = null;
            try
            {
                using var session = await AccountService.OpenSessionAsync(vault, ct).ConfigureAwait(false);
                server = session?.Server;
            }
            catch (InvalidDataException)
            {
            }

            if (server is not null)
            {
                await WithAccountAsync(server, a => a.SignOutAsync(ct)).ConfigureAwait(false);
            }
            else
            {
                await vault.DeleteAsync(SessionSecret.Name, ct).ConfigureAwait(false);
                await state.DisableAsync(ct).ConfigureAwait(false);
            }
        }
        catch when (hadRun)
        {
            // Não saiu: a sessão continua, e o motor volta a rodar.
            await OpenAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        SetState(AccountState.SignedOut);
    }, ct);

    public Task DeleteAccountAsync(string password, CancellationToken ct = default) => RunOpAsync(async () =>
    {
        var run = RequireRun();
        var server = run.Session.Server;
        await StopRunAsync(run).ConfigureAwait(false);
        try
        {
            await WithAccountAsync(server, a => a.DeleteAccountAsync(password, ct)).ConfigureAwait(false);
        }
        catch
        {
            await OpenAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        SetState(AccountState.SignedOut);
    }, ct);

    public Task<IReadOnlyList<DeviceDto>> ListDevicesAsync(CancellationToken ct = default) =>
        RunOpAsync(() => RequireRun().Account.ListDevicesAsync(ct), ct);

    public Task RevokeDeviceAsync(Guid id, CancellationToken ct = default) => RunOpAsync(async () =>
    {
        var run = RequireRun();
        if (id == run.Session.DeviceId)
            throw new InvalidOperationException("Para desconectar este aparelho, use Sair.");
        await run.Account.RevokeDeviceAsync(id, ct).ConfigureAwait(false);
    }, ct);

    public void SyncNow() => _run?.Scheduler.RequestNow();

    /// <summary>
    /// Portão e thread de cada operação: o corpo roda num <see cref="Task.Run(Func{Task})"/> com <c>_ops</c> na mão, e o
    /// aviso sai depois de soltar o portão, com sucesso ou falha.
    /// </summary>
    private async Task<T> RunOpAsync<T>(Func<Task<T>> op, CancellationToken ct)
    {
        try
        {
            return await Task.Run(async () =>
            {
                await _ops.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return await op().ConfigureAwait(false);
                }
                finally
                {
                    _ops.Release();
                }
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            RaiseStatusChanged();
        }
    }

    private Task RunOpAsync(Func<Task> op, CancellationToken ct) => RunOpAsync<bool>(async () =>
    {
        await op().ConfigureAwait(false);
        return true;
    }, ct);

    /// <summary>Um serviço de conta com cliente próprio, descartado no fim, com sucesso ou falha.</summary>
    private async Task<T> WithAccountAsync<T>(Uri server, Func<AccountService, Task<T>> action)
    {
        using var http = createHttp(server);
        return await action(new AccountService(new HttpAccountApi(http), vault, state, server)).ConfigureAwait(false);
    }

    private Task WithAccountAsync(Uri server, Func<AccountService, Task> action) => WithAccountAsync<bool>(server, async a =>
    {
        await action(a).ConfigureAwait(false);
        return true;
    });

    /// <summary>
    /// Decide o estado pelo estado local e pelo cofre e, havendo sessão, liga o motor. Com a sincronização desligada, o
    /// cofre nem é consultado: quem nunca entrou não vê pedido do chaveiro. Só o cancelamento de quem chama lança.
    /// </summary>
    private async Task OpenAsync(CancellationToken ct)
    {
        try
        {
            if (!(await state.GetAsync(ct).ConfigureAwait(false)).Enabled)
            {
                SetState(AccountState.SignedOut);
                return;
            }

            SyncSession? session;
            try
            {
                session = await AccountService.OpenSessionAsync(vault, ct).ConfigureAwait(false);
            }
            catch (SecretVaultUnavailableException)
            {
                SetState(AccountState.VaultLocked);
                return;
            }
            catch (InvalidDataException)
            {
                SetState(AccountState.SessionUnreadable);
                return;
            }

            if (session is null)
            {
                SetState(AccountState.SessionUnreadable);
                return;
            }

            StartRun(session);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Falha inesperada (banco, cofre): nada fica ligado, e "Tentar de novo" refaz a abertura.
            Trace.TraceError($"Não foi possível abrir a sessão de sincronização: {e.GetType().Name}");
            SetState(AccountState.VaultLocked);
        }
    }

    private void StartRun(SyncSession session)
    {
        HttpClient? http = null;
        SyncScheduler? scheduler = null;
        try
        {
            http = createHttp(session.Server);
            var engine = new SyncEngine(state, store, new HttpSyncTransport(http, session.Token), session.Crypto, gate, changes, time);
            var run = new EngineRun(session, http, new AccountService(new HttpAccountApi(http), vault, state, session.Server), engine);
            run.Scheduler = scheduler = new SyncScheduler(ct => RunCycle(run, ct), changes, time);
            _run = run;
            SetState(AccountState.SignedIn, session.Email, session.Server);
            // O primeiro ciclo nasce aqui; a reação a um token recusado espera o portão, que só solta depois daqui.
            scheduler.Start();
        }
        catch
        {
            _run = null;
            scheduler?.Dispose();
            http?.Dispose();
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// O ciclo do agendador: roda fora da thread de quem pediu (decisão 9) e fica guardado para a parada esperar. Depois
    /// de começar a parar, nenhum ciclo novo nasce.
    /// </summary>
    private Task<SyncCycleResult> RunCycle(EngineRun run, CancellationToken ct)
    {
        Task<SyncCycleResult> cycle;
        lock (run.Lock)
        {
            if (run.Stopping)
                return Task.FromResult(SyncCycleResult.Disabled);
            cycle = Task.Run(() => run.Engine.RunCycleAsync(ct), ct);
            run.Cycle = cycle;
        }

        _ = cycle.ContinueWith(t => AfterCycle(run, t), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return cycle;
    }

    /// <summary>
    /// Token recusado: a reação vai para outra tarefa, que espera o portão. Esperar aqui travaria uma parada que está
    /// com o portão na mão esperando justamente este ciclo.
    /// </summary>
    private void AfterCycle(EngineRun run, Task<SyncCycleResult> cycle)
    {
        if (cycle.IsCompletedSuccessfully && cycle.Result == SyncCycleResult.Unauthorized)
            _ = ExpireAsync(run);
        else
            RaiseStatusChanged();
    }

    private async Task ExpireAsync(EngineRun run)
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            // Outra operação já parou ou trocou o motor: nada a fazer.
            if (_run == run)
                await ExpireCoreAsync(run).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao encerrar a sessão recusada: {e.GetType().Name}");
        }
        finally
        {
            _ops.Release();
        }

        RaiseStatusChanged();
    }

    /// <summary>A fila, <c>last_seq</c> e o segredo ficam: entrar de novo envia o que estava esperando (decisão 6).</summary>
    private async Task ExpireCoreAsync(EngineRun run)
    {
        var email = run.Session.Email;
        var server = run.Session.Server;
        await StopRunAsync(run).ConfigureAwait(false);
        SetState(AccountState.SessionExpired, email, server);
    }

    /// <summary>Decisão 10: cancela, espera o ciclo (ignorando cancelamento e falha) e só então descarta.</summary>
    private async Task StopRunAsync(EngineRun run)
    {
        Task? cycle;
        lock (run.Lock)
        {
            run.Stopping = true;
            cycle = run.Cycle;
        }

        run.Scheduler.Stop();
        await WaitQuietlyAsync(cycle).ConfigureAwait(false);
        run.Scheduler.Dispose();
        run.Session.Dispose();
        run.Http.Dispose();
        if (_run == run)
            _run = null;
    }

    private static async Task WaitQuietlyAsync(Task? cycle)
    {
        if (cycle is null)
            return;
        try
        {
            await cycle.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cancelado ou falhou: o motor já gravou o que precisava; aqui só importa que terminou.
        }
    }

    private EngineRun RequireRun() => _run ?? throw new InvalidOperationException(SignInFirst);

    /// <summary>Só endereço absoluto http ou https, sem credenciais, consulta nem fragmento.</summary>
    private static Uri RequireServer(Uri server)
    {
        if (server is not { IsAbsoluteUri: true }
            || (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps)
            || server.UserInfo.Length > 0 || server.Query.Length > 0 || server.Fragment.Length > 0)
            throw new ArgumentException("Informe um endereço http:// ou https:// completo.", nameof(server));
        return server;
    }

    private void SetState(AccountState accountState, string? email = null, Uri? server = null) =>
        _snapshot = new Snapshot(accountState, email, server);

    /// <summary>Cada assinante isolado: a falha de um não chega aos outros nem a quem operou.</summary>
    private void RaiseStatusChanged()
    {
        if (StatusChanged is not { } handlers)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                Trace.TraceError($"Falha num assinante do status da conta: {e}");
            }
        }
    }

    private sealed record Snapshot(AccountState State, string? Email, Uri? Server);

    /// <summary>Um motor ligado: sessão, cliente, serviço de conta, motor e agendador, descartados juntos.</summary>
    private sealed class EngineRun(SyncSession session, HttpClient http, AccountService account, SyncEngine engine)
    {
        public object Lock { get; } = new();
        public SyncSession Session { get; } = session;
        public HttpClient Http { get; } = http;
        public AccountService Account { get; } = account;
        public SyncEngine Engine { get; } = engine;
        public SyncScheduler Scheduler { get; set; } = null!;

        /// <summary>Com <see cref="Lock"/>: a parada começou e nenhum ciclo novo nasce.</summary>
        public bool Stopping { get; set; }

        /// <summary>Com <see cref="Lock"/>: o último ciclo do agendador; os anteriores já terminaram.</summary>
        public Task? Cycle { get; set; }

        public Task? CurrentCycle
        {
            get
            {
                lock (Lock)
                    return Cycle;
            }
        }
    }
}
