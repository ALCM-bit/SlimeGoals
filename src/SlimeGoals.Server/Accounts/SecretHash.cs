using System.Security.Cryptography;
using System.Text;

namespace SlimeGoals.Server.Accounts;

/// <summary>
/// Hash de segredos de 256 bits (auth_key, código de recuperação, tokens). SHA-256 basta porque o
/// segredo já é aleatório; um hash lento só abriria espaço para negação de serviço (decisão 1).
/// </summary>
public static class SecretHash
{
    /// <summary>Hash de um segredo em bytes.</summary>
    public static byte[] Of(byte[] secret) => SHA256.HashData(secret);

    /// <summary>Hash de um token em texto (UTF-8).</summary>
    public static byte[] Of(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>Comparação em tempo constante.</summary>
    public static bool Equal(byte[] left, byte[] right) => CryptographicOperations.FixedTimeEquals(left, right);

    /// <summary>256 bits aleatórios em base64url, sem preenchimento.</summary>
    public static string NewToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
