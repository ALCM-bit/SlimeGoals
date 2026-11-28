using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(SlimeGoals.Server.Tests.PostgresFixture))]

namespace SlimeGoals.Server.Tests;

/// <summary>As duas cadeias de conexão de um banco de teste: o papel do app e o papel dono.</summary>
public sealed record PostgresTestDatabase(string App, string Owner);

/// <summary>Um PostgreSQL descartável por execução; cada teste pede o seu banco novo.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:19-alpine").Build();

    /// <summary>A cadeia do superusuário do contêiner, no banco padrão.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Cria um banco vazio e aplica o <c>roles.sql</c> de produção com papéis de nome único (os papéis valem
    /// para o cluster inteiro e os testes rodam em paralelo).
    /// </summary>
    public async Task<PostgresTestDatabase> NewDatabaseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var database = "t_" + suffix;
        var owner = "owner_" + suffix;
        var app = "app_" + suffix;
        var ownerPassword = Guid.NewGuid().ToString("N");
        var appPassword = Guid.NewGuid().ToString("N");

        await ExecuteAsync(ConnectionString, $"CREATE DATABASE \"{database}\"");

        var script = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "roles.sql")))
            .Replace(":\"owner_role\"", $"\"{owner}\"", StringComparison.Ordinal)
            .Replace(":\"app_role\"", $"\"{app}\"", StringComparison.Ordinal)
            .Replace(":\"db_name\"", $"\"{database}\"", StringComparison.Ordinal)
            .Replace(":'owner_password'", $"'{ownerPassword}'", StringComparison.Ordinal)
            .Replace(":'app_password'", $"'{appPassword}'", StringComparison.Ordinal);
        await ExecuteAsync(For(ConnectionString, database), script);

        return new PostgresTestDatabase(
            For(ConnectionString, database, app, appPassword),
            For(ConnectionString, database, owner, ownerPassword));
    }

    private static string For(string baseConnection, string database, string? user = null, string? password = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnection) { Database = database, Pooling = false };
        if (user is not null)
        {
            builder.Username = user;
            builder.Password = password;
        }

        return builder.ConnectionString;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
