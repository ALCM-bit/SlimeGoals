using System.Reflection;
using SlimeGoals.App.About;

namespace SlimeGoals.App.Tests.About;

public class LicenseCatalogTests
{
    private const string ResourcePrefix = "SlimeGoals.Licenses.";

    [Fact]
    public void Catalog_starts_with_slimegoals()
    {
        LicenseCatalog.All[0].Should().Be(new LicenseEntry("SlimeGoals", "PolyForm Noncommercial 1.0.0", "LICENSE.md", "Linux e Android"));
    }

    [Fact]
    public void Every_catalog_file_is_embedded()
    {
        foreach (var entry in LicenseCatalog.All)
            LicenseTexts.Read(entry.File).Should().NotBeNullOrWhiteSpace(entry.File);
    }

    [Fact]
    public void Read_unknown_file_throws()
    {
        var act = () => LicenseTexts.Read("nada.txt");

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Every_embedded_license_is_listed()
    {
        var embedded = typeof(LicenseTexts).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..]);

        embedded.Should().BeSubsetOf(LicenseCatalog.All.Select(e => e.File));
    }
}
