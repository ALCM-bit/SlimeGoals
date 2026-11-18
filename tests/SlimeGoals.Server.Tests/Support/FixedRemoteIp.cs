using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace SlimeGoals.Server.Tests.Support;

/// <summary>Fixa <c>Connection.RemoteIpAddress</c> antes do pipeline do servidor; o servidor de teste em memória não tem IP de origem.</summary>
internal sealed class FixedRemoteIpFilter(IPAddress address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, continuation) =>
        {
            context.Connection.RemoteIpAddress = address;
            return continuation(context);
        });
        next(app);
    };
}

internal static class FixedRemoteIp
{
    /// <summary>Uma visão do mesmo servidor em que todo pedido chega do IP dado (como se fosse o proxy ou o cliente direto).</summary>
    public static WebApplicationFactory<Program> WithRemoteIp(this ServerFactory factory, string ip) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddTransient<IStartupFilter>(_ => new FixedRemoteIpFilter(IPAddress.Parse(ip)))));
}
