using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Sync;

/// <summary>Estado da conta neste aparelho (decisão 7 da 9.5a).</summary>
public enum AccountState
{
    /// <summary>Sem conta: nenhuma chamada de rede.</summary>
    SignedOut,

    /// <summary>Sessão aberta e motor ligado.</summary>
    SignedIn,

    /// <summary>O servidor recusou o token: o motor parou, mas a fila e o estado continuam para entrar de novo.</summary>
    SessionExpired,

    /// <summary>O cofre não abriu: a sessão pode existir, mas não dá para ler.</summary>
    VaultLocked,

    /// <summary>A sincronização está ligada, mas o segredo do cofre falta ou está corrompido: só sair funciona.</summary>
    SessionUnreadable,
}

/// <summary>O que a tela mostra da conta e da sincronização.</summary>
/// <param name="Email">E-mail da sessão, quando há uma legível.</param>
/// <param name="Server">Servidor da sessão, quando há uma legível.</param>
/// <param name="Pending">Itens na fila esperando envio.</param>
/// <param name="LastError">Texto de <c>last_error</c>, já pronto para a pessoa.</param>
public sealed record AccountStatus(AccountState State, string? Email, Uri? Server, DateTimeOffset? LastSyncAt,
    int Pending, string? LastError);

/// <summary>
/// A conta e a sincronização para a tela: uma operação por vez, nenhuma na thread de quem chama. Os erros de conta
/// chegam como as exceções de <see cref="AccountService"/>; os do ciclo ficam só em <see cref="AccountStatus.LastError"/>.
/// </summary>
public interface ISyncAccount
{
    /// <summary>Disparado depois de cada ciclo, de cada operação e de cada mudança de estado, em qualquer thread.</summary>
    event EventHandler? StatusChanged;

    Task<AccountStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Lê o estado e o cofre e, havendo sessão, liga o motor. Só o cancelamento de quem chama lança.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Para o motor e espera o ciclo em andamento terminar. A conta continua como está.</summary>
    Task StopAsync();

    Task<PendingRegistration> RegisterAsync(Uri server, string email, string password, CancellationToken ct = default);

    Task ConfirmAsync(PendingRegistration pending, string code, string deviceName, CancellationToken ct = default);

    Task LoginAsync(Uri server, string email, string password, string deviceName, CancellationToken ct = default);

    /// <summary>Devolve o código de recuperação já formatado.</summary>
    Task<string> RecoverAsync(Uri server, string email, string recoveryCode, string newPassword, string deviceName, CancellationToken ct = default);

    /// <summary>Entra de novo depois de <see cref="AccountState.SessionExpired"/>, sem perder a fila (decisão 6).</summary>
    Task ReauthenticateAsync(string password, string deviceName, CancellationToken ct = default);

    /// <summary>Em <see cref="AccountState.VaultLocked"/>, tenta abrir o cofre de novo.</summary>
    Task RetryVaultAsync(CancellationToken ct = default);

    Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default);

    /// <summary>Roda um ciclo agora (com o motor ligado) e devolve quantos itens ficaram na fila (decisão 11).</summary>
    Task<int> FlushAsync(CancellationToken ct = default);

    /// <summary>Para o motor, espera o ciclo e só então sai, limpando a fila (decisão 10).</summary>
    Task SignOutAsync(CancellationToken ct = default);

    Task DeleteAccountAsync(string password, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceDto>> ListDevicesAsync(CancellationToken ct = default);

    /// <summary>Remove outro aparelho; o atual sai por <see cref="SignOutAsync"/> (decisão 12).</summary>
    Task RevokeDeviceAsync(Guid id, CancellationToken ct = default);

    /// <summary>Pede um ciclo já, sem esperar. Sem motor ligado, não faz nada.</summary>
    void SyncNow();
}
