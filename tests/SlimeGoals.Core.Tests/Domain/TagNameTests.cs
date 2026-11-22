using System.Globalization;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Domain;

public class TagNameTests
{
    [Theory]
    [InlineData("  Estudos  ", "estudos")]
    [InlineData("Vida   Saudável", "vida saudável")]
    [InlineData("Saúde", "saúde")] // NFD vira NFC
    public void Normalize_is_nfc_trimmed_single_spaced_and_invariant_lower(string raw, string expected) =>
        TagName.Normalize(raw).Should().Be(expected);

    [Fact]
    public void Turkish_culture_does_not_change_normalization()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            TagName.Normalize("TARIFAS").Should().Be("tarifas");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Validate_rejects_empty_and_long()
    {
        TagName.Validate("").Should().Be("Dê um nome à tag.");
        TagName.Validate(new string('a', 25)).Should().Be("A tag pode ter até 24 caracteres.");
        TagName.Validate(new string('a', 24)).Should().BeNull();
    }
}
