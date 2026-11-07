using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SlimeGoals.Sync.Crypto;

/// <summary>
/// Chaves derivadas da senha: a <see cref="AuthKey"/> vai ao servidor; a <see cref="WrapKey"/> nunca sai do aparelho.
/// <see cref="Dispose"/> zera as duas.
/// </summary>
public sealed class PasswordKeys : IDisposable
{
    private const int MinLength = 10;
    private const int MaxLength = 1024;

    private PasswordKeys(byte[] authKey, byte[] wrapKey)
    {
        AuthKey = authKey;
        WrapKey = wrapKey;
    }

    /// <summary>Prova de posse da senha enviada ao servidor.</summary>
    public byte[] AuthKey { get; }

    /// <summary>Abre o embrulho da chave de dados; nunca sai do aparelho.</summary>
    public byte[] WrapKey { get; }

    /// <summary>
    /// <c>mestre = Argon2id(senha em NFKC, sal)</c>; depois, <c>auth_key</c> e <c>wrap_key</c> por HKDF.
    /// </summary>
    /// <exception cref="KdfParamsException">Parâmetros fora dos limites; nada é derivado.</exception>
    public static PasswordKeys Derive(string password, ReadOnlySpan<byte> salt, KdfParams p)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(p);
        p.EnsureWithinLimits();

        var passwordBytes = Encoding.UTF8.GetBytes(SyncKdf.NormalizePassword(password));
        byte[]? master = null;
        try
        {
            using (var argon = new Argon2id(passwordBytes))
            {
                argon.Salt = salt.ToArray();
                argon.MemorySize = p.MemoryKiB;
                argon.Iterations = p.Iterations;
                argon.DegreeOfParallelism = p.Parallelism;
                master = argon.GetBytes(SyncKdf.KeySize);
            }

            return new PasswordKeys(SyncKdf.Derive(master, SyncKdf.AuthInfo), SyncKdf.Derive(master, SyncKdf.WrapInfo));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (master is not null)
                CryptographicOperations.ZeroMemory(master);
        }
    }

    /// <summary>Confere o tamanho da senha, contado em caracteres depois da normalização NFKC.</summary>
    /// <exception cref="ArgumentException">Senha com menos de 10 ou mais de 1024 caracteres.</exception>
    public static void Validate(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var length = SyncKdf.NormalizePassword(password).EnumerateRunes().Count();
        if (length is < MinLength or > MaxLength)
            throw new ArgumentException("A senha deve ter de 10 a 1024 caracteres.");
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(AuthKey);
        CryptographicOperations.ZeroMemory(WrapKey);
    }
}
