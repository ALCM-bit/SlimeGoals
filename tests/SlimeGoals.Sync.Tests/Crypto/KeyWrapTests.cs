using System.Security.Cryptography;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Sync.Tests.Crypto;

public class KeyWrapTests
{
    private static readonly byte[] DataKey = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] WrapKey = RandomNumberGenerator.GetBytes(32);

    [Theory]
    [InlineData(WrapPurpose.Password)]
    [InlineData(WrapPurpose.Recovery)]
    public void Wrap_round_trips_and_is_61_bytes(WrapPurpose purpose)
    {
        var wrapped = KeyWrap.Wrap(DataKey, WrapKey, purpose);

        wrapped.Should().HaveCount(61);
        wrapped[0].Should().Be(1);
        KeyWrap.Unwrap(wrapped, WrapKey, purpose).Should().Equal(DataKey);
        KeyWrap.Wrap(DataKey, WrapKey, purpose).Should().NotEqual(wrapped, "o nonce é aleatório");
    }

    [Fact]
    public void Unwrap_rejects_wrong_key_tampering_and_swapped_purpose()
    {
        var wrapped = KeyWrap.Wrap(DataKey, WrapKey, WrapPurpose.Password);

        Rejects(wrapped, RandomNumberGenerator.GetBytes(32), WrapPurpose.Password);
        Rejects(wrapped, WrapKey, WrapPurpose.Recovery);
        foreach (var index in new[] { 1, 13, 44, 60 })
        {
            var tampered = (byte[])wrapped.Clone();
            tampered[index] ^= 0x01;
            Rejects(tampered, WrapKey, WrapPurpose.Password);
        }

        var otherVersion = (byte[])wrapped.Clone();
        otherVersion[0] = 2;
        Rejects(otherVersion, WrapKey, WrapPurpose.Password);
        Rejects(wrapped[..60], WrapKey, WrapPurpose.Password);
        Rejects([.. wrapped, 0], WrapKey, WrapPurpose.Password);
    }

    [Fact]
    public void Wrap_requires_32_byte_keys()
    {
        var shortData = () => KeyWrap.Wrap(new byte[16], WrapKey, WrapPurpose.Password);
        var shortWrap = () => KeyWrap.Wrap(DataKey, new byte[16], WrapPurpose.Password);

        shortData.Should().Throw<ArgumentException>();
        shortWrap.Should().Throw<ArgumentException>();
    }

    private static void Rejects(byte[] wrapped, byte[] key, WrapPurpose purpose)
    {
        var act = () => KeyWrap.Unwrap(wrapped, key, purpose);
        act.Should().Throw<CryptographicException>();
    }
}
