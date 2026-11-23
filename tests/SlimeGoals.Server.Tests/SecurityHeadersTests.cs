using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests;

/// <summary>
/// ASVS V14.4 e API8: toda resposta sai sem cache (o token e as chaves embrulhadas não ficam em proxy nem em disco) e
/// sem farejar o tipo do conteúdo.
/// </summary>
public sealed class SecurityHeadersTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ok_response_is_not_cached_nor_sniffed()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldCarrySecurityHeaders(response);
    }

    [Fact]
    public async Task Prelogin_response_is_not_cached_nor_sniffed()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/prelogin", new PreloginRequest("ana@exemplo.com"),
            SyncJsonContext.Default.PreloginRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldCarrySecurityHeaders(response);
    }

    [Fact]
    public async Task Unauthorized_response_is_not_cached_nor_sniffed()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/devices");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ShouldCarrySecurityHeaders(response);
    }

    [Fact]
    public async Task Production_error_is_not_cached_nor_sniffed()
    {
        await using var factory = ServerFactory.Production(postgres);
        await using var server = factory.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddTransient<IStartupFilter, ThrowingRouteFilter>()));
        using var client = server.CreateClient();

        var response = await client.GetAsync(ThrowingRouteFilter.Route);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        ShouldCarrySecurityHeaders(response);
    }

    private static void ShouldCarrySecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
    }

    private sealed class ThrowingRouteFilter : IStartupFilter
    {
        public const string Route = "/test/throws";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(Route, branch => branch.Run(_ => throw new InvalidOperationException("detalhe interno")));
        };
    }
}
