using Dapper;
using Npgsql;
using SlimeGoals.Server.Data;

namespace SlimeGoals.Server.Tests.Data;

public sealed class ServerMigratorTests(PostgresFixture postgres)
{
    private static ServerMigrator MigratorFor(string connectionString) => new(new Db(NpgsqlDataSource.Create(connectionString)));

    [Fact]
    public async Task Migrator_creates_the_tables_and_is_idempotent()
    {
        var connectionString = await postgres.NewDatabaseAsync();
        var migrator = MigratorFor(connectionString);

        await migrator.RunAsync(CancellationToken.None);
        await migrator.RunAsync(CancellationToken.None);

        await using var connection = new NpgsqlConnection(connectionString);
        var tables = (await connection.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")).ToList();
        tables.Should().Contain(["accounts", "devices", "records", "schema_version"]);
        (await connection.QueryAsync<int>("SELECT version FROM schema_version")).Should().Equal(1);
    }

    [Fact]
    public async Task Two_migrators_at_once_run_the_script_once()
    {
        var connectionString = await postgres.NewDatabaseAsync();

        var act = () => Task.WhenAll(
            MigratorFor(connectionString).RunAsync(CancellationToken.None),
            MigratorFor(connectionString).RunAsync(CancellationToken.None));

        await act.Should().NotThrowAsync();
        await using var connection = new NpgsqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM schema_version")).Should().Be(1);
    }
}
