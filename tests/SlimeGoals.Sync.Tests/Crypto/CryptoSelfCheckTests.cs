using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Sync.Tests.Crypto;

/// <summary>A conferência que o APK de desenvolvimento roda no aparelho: os vetores fixos da spec.</summary>
public class CryptoSelfCheckTests
{
    [Fact]
    public void All_fixed_vectors_pass()
    {
        var results = CryptoSelfCheck.Run();

        results.Select(r => r.Name).Should().Equal("auth_key", "wrap_key", "recovery_key", "recovery_wrap", "record_key");
        results.Should().OnlyContain(r => r.Passed);
    }
}
