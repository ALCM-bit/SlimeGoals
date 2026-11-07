using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Sync.Tests.Crypto;

public class RecoveryCodeTests
{
    // Bytes 00 01 … 0f em Base32 de Crockford, conferidos com um codificador à parte em Python.
    private const string FixedDisplay = "000G-40R4-0M30-E209-185G-R38E-1W";

    [Fact]
    public void Generated_code_has_the_display_shape()
    {
        using var code = RecoveryCode.Generate();

        code.Display.Should().MatchRegex("^([0-9A-HJKMNP-TV-Z]{4}-){6}[0-9A-HJKMNP-TV-Z]{2}$");
        code.Display.Replace("-", "").Should().HaveLength(26);
    }

    [Fact]
    public void Generated_codes_differ()
    {
        using var a = RecoveryCode.Generate();
        using var b = RecoveryCode.Generate();

        a.Display.Should().NotBe(b.Display);
    }

    [Fact]
    public void Recovery_code_round_trips_and_tolerates_typing()
    {
        using var code = RecoveryCode.Generate();
        using var parsed = RecoveryCode.Parse(code.Display);
        parsed.Display.Should().Be(code.Display);

        using var fixedCode = RecoveryCode.Parse(FixedDisplay);
        fixedCode.Display.Should().Be(FixedDisplay);

        foreach (var typed in new[]
        {
            "000g-40r4-0m30-e209-185g-r38e-1w",
            "000G40R40M30E209185GR38E1W",
            "  000G 40R4 0M30 E209 185G R38E 1W ",
            "OOOG-4OR4-OM3O-E2O9-I85G-R38E-1W",
            "000G-40R4-0M30-E209-L85G-R38E-lW",
        })
        {
            using var lenient = RecoveryCode.Parse(typed);
            lenient.Display.Should().Be(FixedDisplay, typed);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("000G-40R4-0M30-E209-185G-R38E-1")]
    [InlineData("000G-40R4-0M30-E209-185G-R38E-1WW")]
    [InlineData("000G-40R4-0M30-E209-185G-R38E-1U")]
    [InlineData("000G-40R4-0M30-E209-185G-R38E-1*")]
    [InlineData("000G-40R4-0M30-E209-185G-R38E-1Ç")]
    [InlineData("000G-40R4-0M30-E209-185G-R38E-1X")]
    public void Recovery_code_rejects_wrong_length_or_alphabet(string typed)
    {
        var act = () => RecoveryCode.Parse(typed);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Recovery_keys_match_the_fixed_vector()
    {
        using var code = RecoveryCode.Parse(FixedDisplay);
        using var keys = code.Keys();

        // Conferidos com o HKDF do `openssl kdf` e com um HMAC em Python.
        Convert.ToHexStringLower(keys.RecoveryKey).Should().Be("944be12c002a09efb7f8b498f2063f4b674d48a0d24e0bb8d4d814e625dd2159");
        Convert.ToHexStringLower(keys.WrapKey).Should().Be("c7da2cd9178e6498001e21d80eead4bdd5ea7273c354e309ad2bd7fe4e1e3eef");
    }
}
