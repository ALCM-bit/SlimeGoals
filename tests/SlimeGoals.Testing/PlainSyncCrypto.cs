using System.Security.Cryptography;
using System.Text.Json;
using SlimeGoals.Core.Sync;

namespace SlimeGoals.Testing;

/// <summary>Cripto de teste, sem cifra: o blob é o JSON do envelope. Recusa o que a cripto real recusaria.</summary>
public sealed class PlainSyncCrypto : ISyncCrypto
{
    public string RecordKey(Guid recordId) => "k:" + recordId;

    public byte[] Seal(SyncEnvelope envelope) => JsonSerializer.SerializeToUtf8Bytes(envelope);

    public SyncEnvelope Open(string recordKey, byte[] blob)
    {
        var envelope = Read(blob);
        if (RecordKey(envelope.RecordId) != recordKey)
            throw new CryptographicException("O blob não pertence a esta chave.");
        if (envelope.Version != SyncCodec.FormatVersion)
            throw new SyncFormatException(envelope.Version);
        return envelope;
    }

    private static SyncEnvelope Read(byte[] blob)
    {
        try
        {
            return JsonSerializer.Deserialize<SyncEnvelope>(blob) ?? throw new CryptographicException("Blob vazio.");
        }
        catch (JsonException e)
        {
            throw new CryptographicException("Blob corrompido.", e);
        }
    }
}
