using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using SlimeGoals.Server;
using SlimeGoals.Server.Accounts;
using SlimeGoals.Server.Auth;
using SlimeGoals.Server.Data;
using SlimeGoals.Server.Limits;
using SlimeGoals.Server.Sync;
using SlimeGoals.Sync.Contracts;

var builder = WebApplication.CreateBuilder(args);

// Segredos do Docker Compose: um arquivo por chave (`__` vira `:`). Entra por último para
// valer mais que `environment:`; a pasta ausente (máquina de desenvolvimento) é normal.
builder.Configuration.AddKeyPerFile(builder.Configuration["SecretsDirectory"] ?? "/run/secrets", optional: true);

builder.Services.AddOptions<ServerOptions>().Bind(builder.Configuration).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var appConnection = configuration.GetConnectionString("Postgres");
    if (string.IsNullOrWhiteSpace(appConnection))
    {
        throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.");
    }

    var migrations = configuration.GetConnectionString("PostgresMigrations");
    return new Db(
        NpgsqlDataSource.Create(appConnection),
        string.IsNullOrWhiteSpace(migrations) ? null : NpgsqlDataSource.Create(migrations));
});
builder.Services.AddSingleton<ServerMigrator>();
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<RateLimits>();
builder.Services.AddSingleton<RecordStore>();
builder.Services.AddSingleton<RecordPurger>();
builder.Services.AddHostedService<MaintenanceService>();
builder.Services.AddSingleton<LogEmailSender>();
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.TryAddSingleton<IEmailSender>(sp =>
    sp.GetRequiredService<IOptions<ServerOptions>>().Value.Email.Mode == ServerOptions.EmailMode.Smtp
        ? sp.GetRequiredService<SmtpEmailSender>()
        : sp.GetRequiredService<LogEmailSender>());
builder.Services.AddSingleton<IValidateOptions<ServerOptions>, EmailSenderValidator>();
builder.Services.AddSingleton(_ => new ConfirmationEmailQueue());
builder.Services.AddHostedService<EmailDispatchService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, SyncJsonContext.Default));
builder.Services.AddAuthentication(DeviceTokenHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DeviceTokenHandler>(DeviceTokenHandler.SchemeName, null);
builder.Services.AddAuthorization();
// Só o proxy configurado fala por `X-Forwarded-For`; de qualquer outro o cabeçalho é ignorado.
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ServerOptions>>((forwarded, server) =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    forwarded.ForwardLimit = 1;
    forwarded.KnownProxies.Clear();
    forwarded.KnownIPNetworks.Clear();
    foreach (var network in server.Value.Server.TrustedProxyNetworks)
    {
        forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

var app = builder.Build();

// Fora de Development, erro inesperado responde só o código genérico: nada de pilha nem nome de exceção.
// Pedido que o host recusou ao ler (corpo truncado, por exemplo) continua com o status dele. Em Development
// fica a página de erro do ASP.NET Core.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(error => error.Run(context =>
    {
        var (code, status) = context.Features.Get<IExceptionHandlerFeature>()?.Error is BadHttpRequestException bad
            ? ("invalid_request", bad.StatusCode)
            : ("internal_error", StatusCodes.Status500InternalServerError);
        return Results.Json(new ErrorResponse(code), SyncJsonContext.Default.ErrorResponse, statusCode: status).ExecuteAsync(context);
    }));
}

// Antes de ler as opções: ler IOptions<ServerOptions>.Value roda todos os validadores.
if (!app.Environment.IsDevelopment())
{
    await DatabaseRoleGuard.EnsureSafeAsync(app.Services.GetRequiredService<Db>(), CancellationToken.None);
}

if (app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.Server.MigrateOnStart)
{
    await app.Services.GetRequiredService<ServerMigrator>().RunAsync(CancellationToken.None);
}

// Sem rede configurada o middleware confiaria em qualquer origem (lista vazia = sem checagem), então nem entra no pipeline.
if (app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.Server.TrustedProxyNetworks.Length > 0)
{
    app.UseForwardedHeaders();
}

app.UseBodyLimits();
app.UseWhen(
    context => !context.Request.Path.Equals("/health", StringComparison.Ordinal),
    branch => branch.Use(async (context, next) =>
    {
        if (context.RequestServices.GetRequiredService<RateLimits>().HitRequest(ClientIp.Of(context)) is { } retry)
        {
            await new RateLimitedResult(retry).ExecuteAsync(context);
            return;
        }

        await next(context);
    }));
app.UseAuthentication();
app.UseAuthorization();
app.MapAccountEndpoints();
app.MapSyncEndpoints();

app.MapGet("/health", async (Db db, CancellationToken cancellationToken) =>
{
    try
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(cancellationToken);
        return Results.Ok();
    }
    catch (NpgsqlException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

public partial class Program;
