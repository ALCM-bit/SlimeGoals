using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SlimeGoals.Sync.Tests;

public class CertificatePinsTests
{
    private static readonly Uri Remote = new("https://sync.exemplo.ts.net/");

    private static X509Certificate2 Root(string name) =>
        X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Certificates", name)));

    [Theory]
    [InlineData("isrg-root-x1.pem")]
    [InlineData("isrg-root-x2.pem")]
    [InlineData("isrg-root-yr.pem")]
    [InlineData("isrg-root-ye.pem")]
    public void Each_lets_encrypt_root_is_pinned(string root)
    {
        CertificatePins.Roots.Should().Contain(CertificatePins.SpkiHash(Root(root)));
    }

    [Fact]
    public void Only_the_lets_encrypt_roots_are_pinned()
    {
        CertificatePins.Roots.Should().BeEquivalentTo(
            new[] { "isrg-root-x1.pem", "isrg-root-x2.pem", "isrg-root-yr.pem", "isrg-root-ye.pem" }
                .Select(name => CertificatePins.SpkiHash(Root(name))));
    }

    [Fact]
    public void Chain_ending_in_a_pinned_root_is_trusted()
    {
        using var leaf = SelfSigned();
        using var root = Root("isrg-root-x1.pem");

        CertificatePins.IsTrusted(Remote, SslPolicyErrors.None, [leaf, root]).Should().BeTrue();
    }

    [Fact]
    public void Chain_from_another_ca_is_refused()
    {
        using var leaf = SelfSigned();
        using var other = SelfSigned();

        CertificatePins.IsTrusted(Remote, SslPolicyErrors.None, [leaf, other]).Should().BeFalse();
        CertificatePins.IsTrusted(Remote, SslPolicyErrors.None, []).Should().BeFalse();
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    public void System_validation_failure_is_refused_even_with_a_pinned_root(SslPolicyErrors errors)
    {
        using var root = Root("isrg-root-x1.pem");

        CertificatePins.IsTrusted(Remote, errors, [root]).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://127.0.0.1:8443/")]
    [InlineData("https://localhost/")]
    [InlineData("https://[::1]/")]
    public void Loopback_needs_only_the_system_validation(string server)
    {
        using var leaf = SelfSigned();

        CertificatePins.IsTrusted(new Uri(server), SslPolicyErrors.None, [leaf]).Should().BeTrue();
        CertificatePins.IsTrusted(new Uri(server), SslPolicyErrors.RemoteCertificateChainErrors, [leaf]).Should().BeFalse();
    }

    // Cadeia que o letsencrypt.org apresentou em 23/11/2026: folha, YE2, Root YE (cruzada pela X2), X2 (cruzada pela X1).
    private static X509Certificate2Collection PresentedChain()
    {
        var chain = new X509Certificate2Collection();
        chain.ImportFromPemFile(Path.Combine(AppContext.BaseDirectory, "Certificates", "letsencrypt-org-chain.pem"));
        return chain;
    }

    /// <summary>
    /// Como o Android entrega: a plataforma já validou, mas a cadeia vem sem montar, só com o que o servidor apresentou
    /// em <see cref="X509ChainPolicy.ExtraStore"/>, e com a revogação ligada. Aqui a confiança fica só na X1, a hora na
    /// validade da folha, e a revogação sem rede: sem OCSP no Let's Encrypt, ela fica desconhecida, como no Android.
    /// </summary>
    private static X509Chain UnbuiltChain(IEnumerable<X509Certificate2> presented)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(Root("isrg-root-x1.pem"));
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
        chain.ChainPolicy.VerificationTime = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        chain.ChainPolicy.ExtraStore.AddRange(presented.ToArray());
        return chain;
    }

    [Fact]
    public void Unbuilt_chain_is_rebuilt_and_the_pin_checked_on_the_validated_path()
    {
        var presented = PresentedChain();
        using var chain = UnbuiltChain(presented.Skip(1));

        var path = CertificatePins.ValidatedPath(presented[0], chain).ToList();

        path.Select(certificate => certificate.Subject).Should().Contain(subject => subject.Contains("Root YE"));
        CertificatePins.IsTrusted(Remote, SslPolicyErrors.None, path).Should().BeTrue();
    }

    [Fact]
    public void Pinned_root_presented_outside_the_validated_path_is_refused()
    {
        // Folha de outra CA com uma cópia da Root YE anexada: a cadeia não monta, e a Root YE solta não vale.
        using var leaf = SelfSigned();
        using var chain = UnbuiltChain(PresentedChain().Skip(1));

        CertificatePins.IsTrusted(Remote, SslPolicyErrors.None, CertificatePins.ValidatedPath(leaf, chain)).Should().BeFalse();
    }

    [Fact]
    public void Built_chain_is_used_as_is()
    {
        var presented = PresentedChain();
        using var chain = UnbuiltChain(presented.Skip(1));
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(presented[0]).Should().BeTrue();

        CertificatePins.ValidatedPath(leaf: null, chain).Should().HaveCount(chain.ChainElements.Count);
    }

    [Fact]
    public void Missing_chain_or_leaf_gives_an_empty_path()
    {
        using var chain = UnbuiltChain([]);

        CertificatePins.ValidatedPath(PresentedChain()[0], null).Should().BeEmpty();
        CertificatePins.ValidatedPath(null, chain).Should().BeEmpty();
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=outra-ca", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
