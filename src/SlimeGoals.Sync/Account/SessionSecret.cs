using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlimeGoals.Sync.Account;

/// <summary>
/// O único segredo do cofre, <c>sync-session</c> (decisão 10): servidor, e-mail, id do aparelho no servidor, token e
/// chave de dados. Gravado e apagado de uma vez. <see cref="Dispose"/> zera a chave de dados.
/// </summary>
internal sealed class SessionSecret : IDisposable
{
    /// <summary>Nome do segredo no cofre.</summary>
    public const string Name = "sync-session";

    public string? Server { get; set; }

    public string? Email { get; set; }

    public Guid? DeviceId { get; set; }

    public string? Token { get; set; }

    /// <summary>Chave de dados aberta, em base64 no JSON.</summary>
    public byte[]? DataKey { get; set; }

    /// <summary>JSON em UTF-8. Quem chama zera os bytes depois de gravar.</summary>
    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, SessionSecretJsonContext.Default.SessionSecret);

    /// <summary>Lê o segredo e confere que nada falta.</summary>
    /// <exception cref="InvalidDataException">Segredo ilegível ou incompleto.</exception>
    public static SessionSecret Parse(byte[] bytes)
    {
        SessionSecret? secret;
        try
        {
            secret = JsonSerializer.Deserialize(bytes, SessionSecretJsonContext.Default.SessionSecret);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("O segredo da sessão no cofre está ilegível.", e);
        }

        if (secret is null || string.IsNullOrEmpty(secret.Server) || string.IsNullOrEmpty(secret.Email)
            || secret.DeviceId is null || string.IsNullOrEmpty(secret.Token) || secret.DataKey is not { Length: 32 })
        {
            secret?.Dispose();
            throw new InvalidDataException("O segredo da sessão no cofre está incompleto.");
        }

        return secret;
    }

    public void Dispose()
    {
        if (DataKey is not null)
            CryptographicOperations.ZeroMemory(DataKey);
    }
}

/// <summary>JSON do segredo da sessão: nomes em snake_case, bytes em base64.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SessionSecret))]
internal sealed partial class SessionSecretJsonContext : JsonSerializerContext;
