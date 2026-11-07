using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlimeGoals.Core.Sync;

namespace SlimeGoals.Sync.Crypto;

/// <summary>
/// Cifra dos registros (Sincronizacao_v1 §6): <c>versão 1 (1) ‖ nonce (12) ‖ cifra ‖ tag (16)</c>, com AES-256-GCM.
/// A cifra guarda o <see cref="SyncEnvelope"/> em JSON; o dado autenticado é <c>"slimegoals/v1/record" ‖ record_key</c>.
/// As subchaves <c>enc</c> e <c>rk</c> vêm da chave de dados por HKDF e são zeradas em <see cref="Dispose"/>.
/// Todo uso das subchaves e o <see cref="Dispose"/> passam pelo mesmo lock: um <c>Dispose</c> concorrente nunca zera a
/// chave no meio de um <c>Seal</c>, então nada sai cifrado com zeros. Depois dele, tudo lança <see cref="ObjectDisposedException"/>.
/// </summary>
public sealed partial class AesGcmSyncCrypto : ISyncCrypto, IDisposable
{
    private const byte BlobVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize;
    private const int MinBlobSize = HeaderSize + TagSize;

    private static ReadOnlySpan<byte> AadPrefix => "slimegoals/v1/record"u8;

    private readonly byte[] _encKey;
    private readonly byte[] _recordKeyKey;
    private readonly Lock _lock = new();
    private bool _disposed;

    public AesGcmSyncCrypto(ReadOnlySpan<byte> dataKey)
    {
        if (dataKey.Length != SyncKdf.KeySize)
            throw new ArgumentException("A chave de dados deve ter 32 bytes.", nameof(dataKey));
        _encKey = SyncKdf.Derive(dataKey, SyncKdf.EncInfo);
        _recordKeyKey = SyncKdf.Derive(dataKey, SyncKdf.RecordKeyInfo);
    }

    /// <summary>Chave de dados nova: 32 bytes aleatórios, criada no cadastro.</summary>
    public static byte[] NewDataKey() => RandomNumberGenerator.GetBytes(SyncKdf.KeySize);

    /// <summary><c>base64url(HMAC-SHA256(rk, id))</c> sem preenchimento (43 caracteres); o id em 16 bytes, big-endian.</summary>
    public string RecordKey(Guid recordId)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return RecordKeyCore(recordId);
        }
    }

    /// <summary>Só com o lock tomado e depois de conferir o descarte.</summary>
    private string RecordKeyCore(Guid recordId)
    {
        Span<byte> id = stackalloc byte[16];
        recordId.TryWriteBytes(id, bigEndian: true, out _);
        Span<byte> mac = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_recordKeyKey, id, mac);
        return Base64Url.EncodeToString(mac);
    }

    public byte[] Seal(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var plain = JsonSerializer.SerializeToUtf8Bytes(new EnvelopeDto
        {
            Version = envelope.Version,
            Kind = envelope.Kind.ToString(),
            RecordId = envelope.RecordId,
            DeviceId = envelope.DeviceId,
            Payload = envelope.Payload,
        }, EnvelopeJsonContext.Default.EnvelopeDto);
        try
        {
            var blob = new byte[MinBlobSize + plain.Length];
            blob[0] = BlobVersion;
            var nonce = blob.AsSpan(1, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                using var aes = new AesGcm(_encKey, TagSize);
                aes.Encrypt(nonce, plain, blob.AsSpan(HeaderSize, plain.Length), blob.AsSpan(HeaderSize + plain.Length),
                    Aad(RecordKeyCore(envelope.RecordId)));
            }

            return blob;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <exception cref="SyncFormatException">Byte de versão do blob ou versão do envelope desconhecidos.</exception>
    /// <exception cref="CryptographicException">Blob de outra chave, de outro registro, adulterado ou ilegível.</exception>
    public SyncEnvelope Open(string recordKey, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(recordKey);
        ArgumentNullException.ThrowIfNull(blob);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (blob.Length == 0)
            throw new CryptographicException("O blob do registro está vazio.");
        if (blob[0] != BlobVersion)
            throw new SyncFormatException(blob[0]);
        if (blob.Length < MinBlobSize)
            throw new CryptographicException("O blob do registro está truncado.");

        var cipherLength = blob.Length - MinBlobSize;
        var plain = new byte[cipherLength];
        try
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                using (var aes = new AesGcm(_encKey, TagSize))
                    aes.Decrypt(blob.AsSpan(1, NonceSize), blob.AsSpan(HeaderSize, cipherLength), blob.AsSpan(HeaderSize + cipherLength),
                        plain, Aad(recordKey));

                var envelope = Read(plain);
                // O GCM já amarra o blob à chave; conferir o id dentro do conteúdo fecha a §6.
                if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(RecordKeyCore(envelope.RecordId)), Encoding.UTF8.GetBytes(recordKey)))
                    throw new CryptographicException("O blob não pertence a esta chave.");
                return envelope;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            CryptographicOperations.ZeroMemory(_encKey);
            CryptographicOperations.ZeroMemory(_recordKeyKey);
        }
    }

    /// <summary>A versão vem antes do resto: um envelope de versão nova pode ter outra forma e ainda assim dá <see cref="SyncFormatException"/>.</summary>
    private static SyncEnvelope Read(byte[] plain)
    {
        var declared = ReadVersion(plain) ?? throw new CryptographicException("O conteúdo do registro não tem versão.");
        if (declared != SyncCodec.FormatVersion)
            throw new SyncFormatException(declared);

        EnvelopeDto dto;
        try
        {
            dto = JsonSerializer.Deserialize(plain, EnvelopeJsonContext.Default.EnvelopeDto)
                ?? throw new CryptographicException("O conteúdo do registro está vazio.");
        }
        catch (JsonException e)
        {
            throw new CryptographicException("O conteúdo do registro é ilegível.", e);
        }

        var version = dto.Version ?? throw new CryptographicException("O conteúdo do registro não tem versão.");
        if (version != SyncCodec.FormatVersion)
            throw new SyncFormatException(version);
        if (!Enum.TryParse<SyncRecordKind>(dto.Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind)
            || !string.Equals(kind.ToString(), dto.Kind, StringComparison.Ordinal))
            throw new CryptographicException("O conteúdo do registro tem tipo desconhecido.");
        if (dto.RecordId is not { } recordId || dto.DeviceId is not { } deviceId || dto.Payload is not { } payload)
            throw new CryptographicException("O conteúdo do registro está incompleto.");
        return new SyncEnvelope(version, kind, recordId, deviceId, payload);
    }

    /// <summary>Só o campo <c>version</c> do objeto de fora, sem olhar a forma dos outros; nulo se faltar ou não for inteiro.</summary>
    /// <exception cref="CryptographicException">O conteúdo não é um objeto JSON legível.</exception>
    private static int? ReadVersion(ReadOnlySpan<byte> plain)
    {
        try
        {
            var reader = new Utf8JsonReader(plain);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new CryptographicException("O conteúdo do registro não é um objeto.");
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isVersion = reader.ValueTextEquals("version"u8);
                reader.Read();
                if (isVersion)
                    return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var version) ? version : null;
                reader.Skip();
            }

            return null;
        }
        catch (JsonException e)
        {
            throw new CryptographicException("O conteúdo do registro é ilegível.", e);
        }
    }

    private static byte[] Aad(string recordKey)
    {
        var aad = new byte[AadPrefix.Length + Encoding.UTF8.GetByteCount(recordKey)];
        AadPrefix.CopyTo(aad);
        Encoding.UTF8.GetBytes(recordKey, aad.AsSpan(AadPrefix.Length));
        return aad;
    }

    /// <summary>Forma do envelope no JSON cifrado. Campos anuláveis: o que faltar vira erro, não valor padrão.</summary>
    internal sealed class EnvelopeDto
    {
        public int? Version { get; set; }
        public string? Kind { get; set; }
        public Guid? RecordId { get; set; }
        public Guid? DeviceId { get; set; }
        public string? Payload { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(EnvelopeDto))]
    internal sealed partial class EnvelopeJsonContext : JsonSerializerContext;
}
