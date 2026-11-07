using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Sync.Tests.Crypto;

public class KdfParamsTests
{
    [Fact]
    public void Default_params_serialize_exactly()
    {
        KdfParams.Default.Should().Be(new KdfParams(1, 65536, 3, 1));
        KdfParams.Default.ToJson().Should().Be("""{"v":1,"m":65536,"t":3,"p":1}""");
    }

    [Fact]
    public void Parse_round_trips_and_accepts_the_limits()
    {
        KdfParams.Parse(KdfParams.Default.ToJson()).Should().Be(KdfParams.Default);
        KdfParams.Parse("""{ "p": 4, "t": 10, "m": 262144, "v": 1 }""").Should().Be(new KdfParams(1, 262144, 10, 4));
    }

    [Theory]
    [InlineData("""{"v":1,"m":8,"t":3,"p":1}""")]
    [InlineData("""{"v":1,"m":65535,"t":3,"p":1}""")]
    [InlineData("""{"v":1,"m":300000,"t":3,"p":1}""")]
    [InlineData("""{"v":1,"m":65536,"t":1,"p":1}""")]
    [InlineData("""{"v":1,"m":65536,"t":11,"p":1}""")]
    [InlineData("""{"v":1,"m":65536,"t":3,"p":0}""")]
    [InlineData("""{"v":1,"m":65536,"t":3,"p":5}""")]
    [InlineData("""{"v":2,"m":65536,"t":3,"p":1}""")]
    [InlineData("""{"v":1,"m":65536,"t":3,"p":1""")]
    [InlineData("""{"v":1,"m":65536,"t":3}""")]
    [InlineData("""{"v":1,"m":"65536","t":3,"p":1}""")]
    [InlineData("""{"v":1,"m":65536.5,"t":3,"p":1}""")]
    [InlineData("""{"v":1,"m":65536,"t":3,"p":1,"p":4}""")]
    [InlineData("""[1,65536,3,1]""")]
    [InlineData("null")]
    [InlineData("")]
    public void Parse_rejects_weak_huge_or_malformed_params(string json)
    {
        var act = () => KdfParams.Parse(json);

        act.Should().Throw<KdfParamsException>();
    }
}
