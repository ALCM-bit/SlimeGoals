using System.Security.Cryptography;
using System.Text;

namespace SlimeGoals.Sync.Crypto;

/// <summary>Os <c>info</c> do HKDF e a expansão em chaves de 32 bytes, sem sal (Sincronizacao_v1 §6).</summary>
internal static class SyncKdf
{
    public const int KeySize = 32;

    public static ReadOnlySpan<byte> AuthInfo => "slimegoals/v1/auth"u8;
    public static ReadOnlySpan<byte> WrapInfo => "slimegoals/v1/wrap"u8;
    public static ReadOnlySpan<byte> RecoveryAuthInfo => "slimegoals/v1/recovery-auth"u8;
    public static ReadOnlySpan<byte> RecoveryWrapInfo => "slimegoals/v1/recovery-wrap"u8;

    /// <summary>HKDF-SHA256 de <paramref name="ikm"/> com o <paramref name="info"/> dado, sem sal.</summary>
    public static byte[] Derive(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> info)
    {
        var output = new byte[KeySize];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, output, salt: ReadOnlySpan<byte>.Empty, info);
        return output;
    }

    /// <summary>Senha em NFKC, para que acento composto e decomposto deem a mesma chave.</summary>
    public static string NormalizePassword(string password) => password.Normalize(NormalizationForm.FormKC);
}
