using Microsoft.AspNetCore.Authentication;
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
builder.Services.AddSingleton<AuthRateLimits>();
builder.Services.AddSingleton<RecordStore>();
builder.Services.AddSingleton<RecordPurger>();
builder.Services.AddHostedService<MaintenanceService>();
builder.Services.TryAddSingleton<IEmailSender, LogEmailSender>();
builder.Services.AddSingleton<IValidateOptions<ServerOptions>, EmailSenderValidator>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, SyncJsonContext.Default));
builder.Services.AddAuthentication(DeviceTokenHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DeviceTokenHandler>(DeviceTokenHandler.SchemeName, null);
builder.Services.AddAuthorization();

var app = builder.Build();

// Antes de ler as opções: ler IOptions<ServerOptions>.Value roda todos os validadores.
if (!app.Environment.IsDevelopment())
{
    await DatabaseRoleGuard.EnsureSafeAsync(app.Services.GetRequiredService<Db>(), CancellationToken.None);
}

if (app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.Server.MigrateOnStart)
{
    await app.Services.GetRequiredService<ServerMigrator>().RunAsync(CancellationToken.None);
}

app.UseBodyLimits();
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
