using System.Text;
using System.Text.RegularExpressions;

namespace SlimeGoals.Sync.Vault;

/// <summary>
/// Formato do arquivo de um segredo cifrado pelo cofre do Android: <c>versão 1 (1 byte) ‖ nonce (12) ‖ cifra ‖ tag (16)</c>.
/// A cifra e a tag ficam num bloco só, como o GCM do Java as devolve. O dado autenticado leva o nome do segredo, então
/// um arquivo copiado para outro nome não abre. A chave fica no Keystore; aqui só o formato, testável fora do Android.
/// </summary>
public static partial class SealedSecretFormat
{
    public const byte Version = 1;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    private const int MinimumSize = 1 + NonceSize + TagSize;

    /// <summary>Nome usável como arquivo: letras minúsculas, dígitos e hífen, de 1 a 64 caracteres.</summary>
    public static bool IsValidName(string name) => SimpleName().IsMatch(name);

    /// <summary>O dado autenticado do segredo <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">Nome fora de <see cref="IsValidName"/>.</exception>
    public static byte[] AssociatedData(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException("Nome de segredo inválido.", nameof(name));
        return Encoding.UTF8.GetBytes("slimegoals/v1/vault/" + name);
    }

    /// <exception cref="ArgumentException">Nonce sem 12 bytes ou cifra menor que a tag.</exception>
    public static byte[] Pack(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipherAndTag)
    {
        if (nonce.Length != NonceSize)
            throw new ArgumentException($"O nonce deve ter {NonceSize} bytes.", nameof(nonce));
        if (cipherAndTag.Length < TagSize)
            throw new ArgumentException($"A cifra deve ter pelo menos os {TagSize} bytes da tag.", nameof(cipherAndTag));

        var blob = new byte[1 + NonceSize + cipherAndTag.Length];
        blob[0] = Version;
        nonce.CopyTo(blob.AsSpan(1));
        cipherAndTag.CopyTo(blob.AsSpan(1 + NonceSize));
        return blob;
    }

    /// <summary>Separa as partes; falso para versão desconhecida ou arquivo curto demais.</summary>
    public static bool TryUnpack(ReadOnlySpan<byte> blob, out byte[] nonce, out byte[] cipherAndTag)
    {
        if (blob.Length < MinimumSize || blob[0] != Version)
        {
            nonce = [];
            cipherAndTag = [];
            return false;
        }

        nonce = blob.Slice(1, NonceSize).ToArray();
        cipherAndTag = blob[(1 + NonceSize)..].ToArray();
        return true;
    }

    [GeneratedRegex("^[a-z0-9-]{1,64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SimpleName();
}
