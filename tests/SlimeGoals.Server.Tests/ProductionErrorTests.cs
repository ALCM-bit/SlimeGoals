using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests;

public sealed class ProductionErrorTests(PostgresFixture postgres)
{
    private const string ThrowingRoute = "/test/throws";
    private const string BadRequestRoute = "/test/bad-request";

    [Fact]
    public async Task Production_error_has_no_stack_trace()
    {
        await using var factory = ServerFactory.Production(postgres);
        await using var server = WithThrowingRoutes(factory);
        using var client = server.CreateClient();

        var response = await client.GetAsync(ThrowingRoute);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(nameof(InvalidOperationException)).And.NotContain("at ").And.NotContain("detalhe interno");
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("internal_error");
    }

    [Fact]
    public async Task Production_bad_request_from_the_host_keeps_its_status()
    {
        await using var factory = ServerFactory.Production(postgres);
        await using var server = WithThrowingRoutes(factory);
        using var client = server.CreateClient();

        var response = await client.GetAsync(BadRequestRoute);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_request");
    }

    private static WebApplicationFactory<Program> WithThrowingRoutes(ServerFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddTransient<IStartupFilter, ThrowingRouteFilter>()));

    /// <summary>Rotas que lançam depois do pipeline do servidor: um erro inesperado e um pedido que o host recusou.</summary>
    private sealed class ThrowingRouteFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ThrowingRoute, branch => branch.Run(_ => throw new InvalidOperationException("detalhe interno")));
            app.Map(BadRequestRoute, branch => branch.Run(_ => throw new BadHttpRequestException("corpo truncado", StatusCodes.Status400BadRequest)));
        };
    }
}
