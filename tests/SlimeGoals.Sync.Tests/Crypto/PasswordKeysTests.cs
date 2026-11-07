using System.Text;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Sync.Tests.Crypto;

public class PasswordKeysTests
{
    private static readonly byte[] Salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();

    // Conferidos fora do .NET: Argon2id do `openssl kdf` e HKDF do `openssl kdf` e de um HMAC em Python.
    private const string ExpectedAuthKey = "ba53acc24ffa9ba24189d1f5b7946535d1ee123feaef82cc6d9b5cfbb2f172bc";
    private const string ExpectedWrapKey = "67c2cecefb41882ac98e816bab7619c23f0dc2d30a089743bb33c67ed79bc552";

    [Fact]
    public void Derive_matches_the_fixed_vector()
    {
        using var keys = PasswordKeys.Derive("correct horse battery", Salt, KdfParams.Default);

        Convert.ToHexStringLower(keys.AuthKey).Should().Be(ExpectedAuthKey);
        Convert.ToHexStringLower(keys.WrapKey).Should().Be(ExpectedWrapKey);
    }

    [Fact]
    public void Composed_and_decomposed_accents_give_the_same_keys()
    {
        var composed = "maçã-verde-10".Normalize(NormalizationForm.FormC);
        var decomposed = "maçã-verde-10".Normalize(NormalizationForm.FormD);
        composed.Should().NotBe(decomposed);

        using var a = PasswordKeys.Derive(composed, Salt, KdfParams.Default);
        using var b = PasswordKeys.Derive(decomposed, Salt, KdfParams.Default);

        a.AuthKey.Should().Equal(b.AuthKey);
        a.WrapKey.Should().Equal(b.WrapKey);
    }

    [Fact]
    public void Different_salt_or_password_gives_different_keys()
    {
        var otherSalt = Salt.Reverse().ToArray();

        using var bySalt = PasswordKeys.Derive("correct horse battery", otherSalt, KdfParams.Default);
        using var byPassword = PasswordKeys.Derive("correct horse battery!", Salt, KdfParams.Default);

        Convert.ToHexStringLower(bySalt.AuthKey).Should().NotBe(ExpectedAuthKey);
        Convert.ToHexStringLower(byPassword.AuthKey).Should().NotBe(ExpectedAuthKey);
        bySalt.AuthKey.Should().NotEqual(byPassword.AuthKey);
        bySalt.AuthKey.Should().NotEqual(bySalt.WrapKey);
    }

    [Fact]
    public void Derive_refuses_params_outside_the_limits()
    {
        var act = () => PasswordKeys.Derive("correct horse battery", Salt, new KdfParams(1, 1024, 1, 1));

        act.Should().Throw<KdfParamsException>();
    }

    [Fact]
    public void Dispose_zeroes_the_keys()
    {
        var keys = PasswordKeys.Derive("correct horse battery", Salt, KdfParams.Default);
        var auth = keys.AuthKey;
        var wrap = keys.WrapKey;

        keys.Dispose();

        auth.Should().OnlyContain(b => b == 0);
        wrap.Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void Validate_rejects_short_and_long_passwords()
    {
        var shortAct = () => PasswordKeys.Validate(new string('a', 9));
        var longAct = () => PasswordKeys.Validate(new string('a', 1025));

        shortAct.Should().Throw<ArgumentException>().WithMessage("A senha deve ter de 10 a 1024 caracteres.");
        longAct.Should().Throw<ArgumentException>().WithMessage("A senha deve ter de 10 a 1024 caracteres.");
        PasswordKeys.Validate(new string('a', 10));
        PasswordKeys.Validate(new string('a', 1024));
    }

    [Fact]
    public void Validate_counts_characters_after_normalization()
    {
        // NFD tem 10 unidades, mas em NFKC são 8 caracteres.
        var decomposed = "maçãverde".Normalize(NormalizationForm.FormD)[..10];
        decomposed.Length.Should().Be(10);

        var act = () => PasswordKeys.Validate(decomposed);

        act.Should().Throw<ArgumentException>();
    }
}
