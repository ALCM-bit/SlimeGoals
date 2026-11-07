using System.Security.Cryptography;
using System.Text;
using SlimeGoals.Core.Sync;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Sync.Tests.Crypto;

public sealed class AesGcmSyncCryptoTests : IDisposable
{
    private const string Title = "Beber água";

    private static readonly Guid RecordId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly byte[] FixedDataKey = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    private readonly AesGcmSyncCrypto _crypto = new(FixedDataKey);

    public void Dispose() => _crypto.Dispose();

    private static SyncEnvelope Envelope(int version = SyncCodec.FormatVersion) => new(version, SyncRecordKind.Goal, RecordId,
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), $$"""{"id":"{{RecordId}}","title":"{{Title}}"}""");

    [Fact]
    public void Seal_then_open_returns_the_envelope()
    {
        var envelope = Envelope() with { Kind = SyncRecordKind.Checkin };

        var blob = _crypto.Seal(envelope);

        blob[0].Should().Be(1);
        _crypto.Open(_crypto.RecordKey(RecordId), blob).Should().Be(envelope);
    }

    /// <summary>Conferido fora do .NET, com Python (hmac/hashlib) e com OpenSSL.</summary>
    [Fact]
    public void Record_key_matches_the_fixed_vector_and_is_43_chars()
    {
        var key = _crypto.RecordKey(RecordId);

        key.Should().Be("QDTsiTp-QhBeJKBWIU_Iu_FbuyRZgQnQWvn4-NzxRxY");
        key.Should().HaveLength(43);
        _crypto.RecordKey(Guid.NewGuid()).Should().NotBe(key);
    }

    [Fact]
    public void Same_envelope_sealed_twice_gives_different_blobs()
    {
        var first = _crypto.Seal(Envelope());
        var second = _crypto.Seal(Envelope());

        second.Should().NotEqual(first, "o nonce é aleatório");
        first.AsSpan(1, 12).SequenceEqual(second.AsSpan(1, 12)).Should().BeFalse();
    }

    [Fact]
    public void Blob_opened_under_another_record_key_is_rejected()
    {
        var blob = _crypto.Seal(Envelope());

        var act = () => _crypto.Open(_crypto.RecordKey(Guid.NewGuid()), blob);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Tampered_blob_is_rejected()
    {
        var blob = _crypto.Seal(Envelope());
        var key = _crypto.RecordKey(RecordId);

        // Nonce, cifra e tag.
        foreach (var index in new[] { 1, 12, 13, blob.Length / 2, blob.Length - 17, blob.Length - 16, blob.Length - 1 })
        {
            var tampered = (byte[])blob.Clone();
            tampered[index] ^= 0x01;
            var act = () => _crypto.Open(key, tampered);
            act.Should().Throw<CryptographicException>($"o byte {index} foi trocado");
        }

        var truncated = () => _crypto.Open(key, blob[..28]);
        var empty = () => _crypto.Open(key, []);
        truncated.Should().Throw<CryptographicException>();
        empty.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Blob_from_another_data_key_is_rejected()
    {
        using var other = new AesGcmSyncCrypto(AesGcmSyncCrypto.NewDataKey());
        var blob = other.Seal(Envelope());

        var act = () => _crypto.Open(_crypto.RecordKey(RecordId), blob);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unknown_blob_version_byte_throws_format_exception()
    {
        var blob = _crypto.Seal(Envelope());
        blob[0] = 2;

        var act = () => _crypto.Open(_crypto.RecordKey(RecordId), blob);

        act.Should().Throw<SyncFormatException>().Which.Version.Should().Be(2);
    }

    /// <summary>Selado à mão, com as subchaves e o JSON do formato, para não depender de <c>Seal</c> aceitar a versão.</summary>
    [Fact]
    public void Unknown_envelope_version_throws_format_exception()
    {
        var recordKey = _crypto.RecordKey(RecordId);
        var json = Encoding.UTF8.GetBytes($$"""
            {"version":2,"kind":"Tag","recordId":"{{RecordId}}","deviceId":"{{Guid.NewGuid()}}","payload":"{}","extra":true}
            """);
        var encKey = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, FixedDataKey, encKey, [], "slimegoals/v1/enc"u8);
        var blob = new byte[1 + 12 + json.Length + 16];
        blob[0] = 1;
        RandomNumberGenerator.Fill(blob.AsSpan(1, 12));
        using (var aes = new AesGcm(encKey, 16))
            aes.Encrypt(blob.AsSpan(1, 12), json, blob.AsSpan(13, json.Length), blob.AsSpan(13 + json.Length),
                [.. "slimegoals/v1/record"u8, .. Encoding.UTF8.GetBytes(recordKey)]);

        var act = () => _crypto.Open(recordKey, blob);

        act.Should().Throw<SyncFormatException>().Which.Version.Should().Be(2);
    }

    [Fact]
    public void Blob_does_not_contain_the_payload_in_clear()
    {
        var blob = _crypto.Seal(Envelope());

        Contains(blob, Encoding.UTF8.GetBytes(Title)).Should().BeFalse();
        Contains(blob, Encoding.UTF8.GetBytes("Beber")).Should().BeFalse();
        Convert.ToBase64String(blob).Should().NotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Title)));
        Convert.ToBase64String(blob).Should().NotContain("QmViZXI"); // "Beber" em base64
    }

    [Fact]
    public void Data_key_must_have_32_bytes_and_new_keys_are_random()
    {
        var act = () => new AesGcmSyncCrypto(new byte[16]);

        act.Should().Throw<ArgumentException>();
        AesGcmSyncCrypto.NewDataKey().Should().HaveCount(32).And.NotEqual(AesGcmSyncCrypto.NewDataKey());
    }

    [Fact]
    public void Disposed_crypto_refuses_to_work()
    {
        var crypto = new AesGcmSyncCrypto(FixedDataKey);
        crypto.Dispose();

        var act = () => crypto.RecordKey(RecordId);

        act.Should().Throw<ObjectDisposedException>();
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
