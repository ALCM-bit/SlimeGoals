using System.Security.Cryptography;

namespace SlimeGoals.Sync.Crypto;

/// <summary>
/// Código de recuperação: 128 bits aleatórios em Base32 de Crockford, 26 caracteres em grupos de 4 (o último com 2).
/// <see cref="Dispose"/> zera os bytes.
/// </summary>
public sealed class RecoveryCode : IDisposable
{
    private const int ByteCount = 16;
    private const int CharCount = 26;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private readonly byte[] _bytes;

    private RecoveryCode(byte[] bytes)
    {
        _bytes = bytes;
        Display = Format(bytes);
    }

    /// <summary>Forma de mostrar à pessoa, como <c>XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XX</c>.</summary>
    public string Display { get; }

    public static RecoveryCode Generate() => new(RandomNumberGenerator.GetBytes(ByteCount));

    /// <summary>
    /// Lê o código digitado. Ignora caixa, espaços e hífens; troca <c>O</c> por <c>0</c> e <c>I</c>/<c>L</c> por <c>1</c>.
    /// </summary>
    /// <exception cref="FormatException">Tamanho errado, caractere fora do alfabeto ou bits de sobra diferentes de zero.</exception>
    public static RecoveryCode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Span<int> digits = stackalloc int[CharCount];
        var count = 0;
        foreach (var raw in text)
        {
            if (raw == '-' || char.IsWhiteSpace(raw))
                continue;
            var c = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };
            var digit = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (digit < 0)
                throw new FormatException("O código de recuperação tem um caractere inválido.");
            if (count == CharCount)
                throw new FormatException("O código de recuperação deve ter 26 caracteres.");
            digits[count++] = digit;
        }

        if (count != CharCount)
            throw new FormatException("O código de recuperação deve ter 26 caracteres.");

        // 26 × 5 = 130 bits: os 2 últimos são de sobra e precisam ser zero, senão o código não é canônico.
        if ((digits[CharCount - 1] & 0b11) != 0)
            throw new FormatException("O código de recuperação não é válido.");

        var bytes = new byte[ByteCount];
        int buffer = 0, bits = 0, index = 0;
        foreach (var digit in digits)
        {
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits >= 8 && index < ByteCount)
            {
                bits -= 8;
                bytes[index++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        return new RecoveryCode(bytes);
    }

    /// <summary><c>recovery_key</c> e <c>recovery_wrap</c> por HKDF direto do código, sem Argon2id.</summary>
    public RecoveryKeys Keys() => new(
        SyncKdf.Derive(_bytes, SyncKdf.RecoveryAuthInfo),
        SyncKdf.Derive(_bytes, SyncKdf.RecoveryWrapInfo));

    public void Dispose() => CryptographicOperations.ZeroMemory(_bytes);

    private static string Format(byte[] bytes)
    {
        Span<char> chars = stackalloc char[CharCount + CharCount / 4];
        int buffer = 0, bits = 0, written = 0, digits = 0;
        void Emit(Span<char> target, int digit)
        {
            if (digits > 0 && digits % 4 == 0)
                target[written++] = '-';
            target[written++] = Alphabet[digit];
            digits++;
        }

        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                Emit(chars, (buffer >> bits) & 31);
            }
        }

        if (bits > 0)
            Emit(chars, (buffer << (5 - bits)) & 31);

        return new string(chars[..written]);
    }
}

/// <summary>Chaves do código de recuperação. <see cref="Dispose"/> zera as duas.</summary>
public sealed class RecoveryKeys(byte[] recoveryKey, byte[] wrapKey) : IDisposable
{
    /// <summary>Prova de posse do código enviada ao servidor.</summary>
    public byte[] RecoveryKey { get; } = recoveryKey;

    /// <summary>Abre o embrulho de recuperação da chave de dados; nunca sai do aparelho.</summary>
    public byte[] WrapKey { get; } = wrapKey;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(RecoveryKey);
        CryptographicOperations.ZeroMemory(WrapKey);
    }
}
