using System.Security.Cryptography;

namespace SlimeGoals.Sync.Crypto;

/// <summary>Qual chave embrulha a chave de dados; entra no dado autenticado para impedir a troca de embrulhos.</summary>
public enum WrapPurpose
{
    Password,
    Recovery,
}

/// <summary>
/// Embrulho da chave de dados com AES-256-GCM: <c>versão 1 (1) ‖ nonce (12) ‖ cifra (32) ‖ tag (16)</c>, 61 bytes.
/// </summary>
public static class KeyWrap
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = SyncKdf.KeySize;
    private const int WrappedSize = 1 + NonceSize + KeySize + TagSize;

    public static byte[] Wrap(ReadOnlySpan<byte> dataKey, ReadOnlySpan<byte> wrapKey, WrapPurpose purpose)
    {
        RequireKey(dataKey, nameof(dataKey));
        RequireKey(wrapKey, nameof(wrapKey));

        var wrapped = new byte[WrappedSize];
        wrapped[0] = Version;
        var nonce = wrapped.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(wrapKey, TagSize);
        aes.Encrypt(nonce, dataKey, wrapped.AsSpan(1 + NonceSize, KeySize), wrapped.AsSpan(1 + NonceSize + KeySize), Aad(purpose));
        return wrapped;
    }

    /// <exception cref="CryptographicException">Chave errada, embrulho adulterado, de outro propósito ou de outra versão.</exception>
    public static byte[] Unwrap(byte[] wrapped, ReadOnlySpan<byte> wrapKey, WrapPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(wrapped);
        RequireKey(wrapKey, nameof(wrapKey));
        if (wrapped.Length != WrappedSize || wrapped[0] != Version)
            throw new CryptographicException("O embrulho da chave de dados tem formato desconhecido.");

        var dataKey = new byte[KeySize];
        try
        {
            using var aes = new AesGcm(wrapKey, TagSize);
            aes.Decrypt(
                wrapped.AsSpan(1, NonceSize),
                wrapped.AsSpan(1 + NonceSize, KeySize),
                wrapped.AsSpan(1 + NonceSize + KeySize),
                dataKey,
                Aad(purpose));
            return dataKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(dataKey);
            throw;
        }
    }

    private static ReadOnlySpan<byte> Aad(WrapPurpose purpose) => purpose switch
    {
        WrapPurpose.Password => "slimegoals/v1/wrap-password"u8,
        WrapPurpose.Recovery => "slimegoals/v1/wrap-recovery"u8,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };

    private static void RequireKey(ReadOnlySpan<byte> key, string name)
    {
        if (key.Length != KeySize)
            throw new ArgumentException("A chave deve ter 32 bytes.", name);
    }
}
