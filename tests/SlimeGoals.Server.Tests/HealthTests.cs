using System.Net;
using Microsoft.Extensions.Options;

namespace SlimeGoals.Server.Tests;

public sealed class HealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_answers_ok_with_the_database_up()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_answers_503_without_the_database()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2;Pooling=false",
            ["Server:MigrateOnStart"] = "false",
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Production_refuses_to_start_without_the_prelogin_secret()
    {
        await using var factory = new ServerFactory(
            postgres,
            new Dictionary<string, string?> { ["Server:PreloginSecret"] = "" },
            environment: "Production");

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>();
    }
}
