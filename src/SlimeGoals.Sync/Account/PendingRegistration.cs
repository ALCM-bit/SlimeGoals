using System.Security.Cryptography;

namespace SlimeGoals.Sync.Account;

/// <summary>
/// Cadastro feito e ainda não confirmado (decisão 2). Só existe na memória: fechar o app antes de confirmar significa
/// cadastrar de novo. Guarda as chaves para a confirmação entrar sem um segundo Argon2id; <see cref="Dispose"/> zera todas.
/// </summary>
public sealed class PendingRegistration : IDisposable
{
    private readonly byte[] _authKey;
    private readonly byte[] _wrapKey;
    private bool _disposed;

    internal PendingRegistration(string email, string recoveryCode, byte[] authKey, byte[] wrapKey)
    {
        Email = email;
        RecoveryCode = recoveryCode;
        _authKey = authKey;
        _wrapKey = wrapKey;
    }

    /// <summary>E-mail já normalizado.</summary>
    public string Email { get; }

    /// <summary>Código de recuperação para mostrar à pessoa, como <c>XXXX-XXXX-…-XX</c>.</summary>
    public string RecoveryCode { get; }

    internal byte[] AuthKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _authKey;
        }
    }

    internal byte[] WrapKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _wrapKey;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_authKey);
        CryptographicOperations.ZeroMemory(_wrapKey);
    }
}
