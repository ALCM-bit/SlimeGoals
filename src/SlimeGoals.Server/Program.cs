using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using SlimeGoals.Server;
using SlimeGoals.Server.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ServerOptions>().Bind(builder.Configuration).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ServerOptions>, ServerOptionsValidator>();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
    NpgsqlDataSource.Create(sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurada.")));
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<ServerMigrator>();

var app = builder.Build();

if (app.Services.GetRequiredService<IOptions<ServerOptions>>().Value.Server.MigrateOnStart)
{
    await app.Services.GetRequiredService<ServerMigrator>().RunAsync(CancellationToken.None);
}

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
