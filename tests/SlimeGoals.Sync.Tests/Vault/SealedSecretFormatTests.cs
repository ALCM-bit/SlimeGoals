using System.Text;
using SlimeGoals.Sync.Vault;

namespace SlimeGoals.Sync.Tests.Vault;

/// <summary>O arquivo cifrado do cofre do Android: versão, nonce e cifra com tag.</summary>
public class SealedSecretFormatTests
{
    private static readonly byte[] Nonce = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
    private static readonly byte[] CipherAndTag = Enumerable.Range(100, 20).Select(i => (byte)i).ToArray();

    [Fact]
    public void Pack_then_unpack_returns_the_same_parts()
    {
        var blob = SealedSecretFormat.Pack(Nonce, CipherAndTag);

        blob[0].Should().Be(1);
        SealedSecretFormat.TryUnpack(blob, out var nonce, out var cipherAndTag).Should().BeTrue();
        nonce.Should().Equal(Nonce);
        cipherAndTag.Should().Equal(CipherAndTag);
    }

    [Fact]
    public void Unpack_refuses_an_unknown_version()
    {
        var blob = SealedSecretFormat.Pack(Nonce, CipherAndTag);
        blob[0] = 2;

        SealedSecretFormat.TryUnpack(blob, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(28, false)]
    [InlineData(29, true)]
    public void Unpack_refuses_a_truncated_blob(int length, bool accepted)
    {
        var blob = new byte[length];
        if (length > 0)
            blob[0] = SealedSecretFormat.Version;

        SealedSecretFormat.TryUnpack(blob, out _, out _).Should().Be(accepted);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    public void Pack_refuses_a_nonce_of_the_wrong_size(int size)
    {
        var act = () => SealedSecretFormat.Pack(new byte[size], CipherAndTag);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Pack_refuses_a_cipher_shorter_than_the_tag()
    {
        var act = () => SealedSecretFormat.Pack(Nonce, new byte[15]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Associated_data_depends_on_the_name()
    {
        SealedSecretFormat.AssociatedData("sync-session").Should()
            .Equal(Encoding.UTF8.GetBytes("slimegoals/v1/vault/sync-session"));
        SealedSecretFormat.AssociatedData("outro").Should().NotEqual(SealedSecretFormat.AssociatedData("sync-session"));
    }

    [Theory]
    [InlineData("sync-session", true)]
    [InlineData("a1", true)]
    [InlineData("", false)]
    [InlineData("../x", false)]
    [InlineData("Sync", false)]
    [InlineData("a b", false)]
    [InlineData("a/b", false)]
    [InlineData("a\n", false)]
    public void Only_simple_names_are_valid(string name, bool valid) =>
        SealedSecretFormat.IsValidName(name).Should().Be(valid);

    [Fact]
    public void A_name_longer_than_64_characters_is_invalid()
    {
        SealedSecretFormat.IsValidName(new string('a', 64)).Should().BeTrue();
        SealedSecretFormat.IsValidName(new string('a', 65)).Should().BeFalse();
    }

    [Fact]
    public void Associated_data_refuses_an_invalid_name()
    {
        var act = () => SealedSecretFormat.AssociatedData("../x");

        act.Should().Throw<ArgumentException>();
    }
}
