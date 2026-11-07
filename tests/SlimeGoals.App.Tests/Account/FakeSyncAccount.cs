using SlimeGoals.Sync;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.App.Tests.Account;

/// <summary>Falso da conta: grava cada chamada e devolve o que o teste mandar.</summary>
public sealed class FakeSyncAccount : ISyncAccount
{
    public List<string> Calls { get; } = [];

    public AccountStatus Status { get; set; } = new(AccountState.SignedOut, null, null, null, 0, null);

    /// <summary>Lançada pela próxima operação (e pelas seguintes, até o teste limpar).</summary>
    public Exception? Failure { get; set; }

    /// <summary>Segura as operações até o teste soltar.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public PendingRegistration Pending { get; } = new(new Uri("http://127.0.0.1:8080/"), "ana@exemplo.com",
        "ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23", new byte[32], new byte[32]);

    public string RecoveredCode { get; set; } = "NOVO-CODI-GODE-RECU-PERA-CAO0-12";

    public int FlushResult { get; set; }

    public IReadOnlyList<DeviceDto> Devices { get; set; } = [];

    public Uri? LastServer { get; private set; }
    public string? LastEmail { get; private set; }
    public string? LastPassword { get; private set; }
    public string? LastNewPassword { get; private set; }
    public string? LastCode { get; private set; }
    public string? LastDeviceName { get; private set; }
    public PendingRegistration? LastPending { get; private set; }
    public Guid? LastRevoked { get; private set; }

    public event EventHandler? StatusChanged;

    public void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private async Task OpAsync(string name)
    {
        Calls.Add(name);
        if (Gate is { } gate)
            await gate.Task;
        if (Failure is { } failure)
            throw failure;
    }

    public Task<AccountStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(Status);

    public Task StartAsync(CancellationToken ct = default) => OpAsync("start");

    public Task StopAsync() => OpAsync("stop");

    public async Task<PendingRegistration> RegisterAsync(Uri server, string email, string password, CancellationToken ct = default)
    {
        (LastServer, LastEmail, LastPassword) = (server, email, password);
        await OpAsync("register");
        return Pending;
    }

    public async Task ConfirmAsync(PendingRegistration pending, string code, string deviceName, CancellationToken ct = default)
    {
        (LastPending, LastCode, LastDeviceName) = (pending, code, deviceName);
        await OpAsync("confirm");
    }

    public async Task LoginAsync(Uri server, string email, string password, string deviceName, CancellationToken ct = default)
    {
        (LastServer, LastEmail, LastPassword, LastDeviceName) = (server, email, password, deviceName);
        await OpAsync("login");
    }

    public async Task<string> RecoverAsync(Uri server, string email, string recoveryCode, string newPassword, string deviceName,
        CancellationToken ct = default)
    {
        (LastServer, LastEmail, LastCode, LastNewPassword, LastDeviceName) = (server, email, recoveryCode, newPassword, deviceName);
        await OpAsync("recover");
        return RecoveredCode;
    }

    public async Task ReauthenticateAsync(string password, string deviceName, CancellationToken ct = default)
    {
        (LastPassword, LastDeviceName) = (password, deviceName);
        await OpAsync("reauthenticate");
    }

    public Task RetryVaultAsync(CancellationToken ct = default) => OpAsync("retry-vault");

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        (LastPassword, LastNewPassword) = (currentPassword, newPassword);
        await OpAsync("change-password");
    }

    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        await OpAsync("flush");
        return FlushResult;
    }

    public Task SignOutAsync(CancellationToken ct = default) => OpAsync("sign-out");

    public async Task DeleteAccountAsync(string password, CancellationToken ct = default)
    {
        LastPassword = password;
        await OpAsync("delete-account");
    }

    public async Task<IReadOnlyList<DeviceDto>> ListDevicesAsync(CancellationToken ct = default)
    {
        await OpAsync("list-devices");
        return Devices;
    }

    public async Task RevokeDeviceAsync(Guid id, CancellationToken ct = default)
    {
        LastRevoked = id;
        await OpAsync("revoke-device");
    }

    public void SyncNow() => Calls.Add("sync-now");
}
