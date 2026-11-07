using SlimeGoals.Core.Sync;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Sync.Account;

/// <summary>
/// Sessão aberta a partir do cofre: a cripto do motor e o token para o <see cref="HttpSyncTransport"/>.
/// <see cref="Dispose"/> zera as subchaves da cripto; depois disso, o provedor de token devolve nulo.
/// </summary>
public sealed class SyncSession : IDisposable
{
    private readonly AesGcmSyncCrypto _crypto;
    private readonly string _token;
    private bool _disposed;

    internal SyncSession(Uri server, string email, Guid deviceId, string token, AesGcmSyncCrypto crypto)
    {
        Server = server;
        Email = email;
        DeviceId = deviceId;
        _token = token;
        _crypto = crypto;
        Token = _ => ValueTask.FromResult(_disposed ? null : _token);
    }

    /// <summary>Servidor em que a sessão foi aberta.</summary>
    public Uri Server { get; }

    public string Email { get; }

    /// <summary>Id do aparelho no servidor (não o <c>sync_state.device_id</c> do envelope).</summary>
    public Guid DeviceId { get; }

    /// <summary>Cripto dos registros, com a chave de dados da conta.</summary>
    public ISyncCrypto Crypto
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _crypto;
        }
    }

    /// <summary>Provedor de token no formato do <see cref="HttpSyncTransport"/>.</summary>
    public Func<CancellationToken, ValueTask<string?>> Token { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _crypto.Dispose();
    }
}
