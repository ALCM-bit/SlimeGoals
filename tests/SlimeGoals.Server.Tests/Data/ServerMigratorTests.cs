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
        (await connection.QueryAsync<int>("SELECT version FROM schema_version ORDER BY version")).Should().Equal(1, 2);
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
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM schema_version")).Should().Be(2);
    }
    /// <summary>As rotas já conferem os tamanhos; o banco recusa o mesmo, para um defeito numa rota não gravar lixo.</summary>
    [Theory]
    [InlineData("email", "repeat('a', 250) || '@x.com'")]
    [InlineData("kdf_params", "repeat('x', 257)")]
    [InlineData("auth_hash", "decode(repeat('00', 31), 'hex')")]
    [InlineData("recovery_hash", "decode(repeat('00', 33), 'hex')")]
    [InlineData("kdf_salt", "decode(repeat('00', 8), 'hex')")]
    [InlineData("wrapped_key_password", "decode(repeat('00', 257), 'hex')")]
    [InlineData("wrapped_key_recovery", "''::bytea")]
    public async Task Database_refuses_account_fields_outside_the_limits(string column, string value)
    {
        var connectionString = await postgres.NewDatabaseAsync();
        await MigratorFor(connectionString).RunAsync(CancellationToken.None);
        await using var connection = new NpgsqlConnection(connectionString);
        const string Insert = """
            INSERT INTO accounts (id, email, kdf_salt, kdf_params, auth_hash, recovery_hash, wrapped_key_password, wrapped_key_recovery, created_at)
            VALUES (gen_random_uuid(), 'ana@exemplo.com', decode(repeat('00', 16), 'hex'), '{"v":1}', decode(repeat('00', 32), 'hex'),
                    decode(repeat('00', 32), 'hex'), decode('0102', 'hex'), decode('0304', 'hex'), now())
            """;
        await connection.ExecuteAsync(Insert);

        var act = () => connection.ExecuteAsync($"UPDATE accounts SET {column} = {value}");

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Theory]
    [InlineData("name", "repeat('a', 101)")]
    [InlineData("name", "''")]
    [InlineData("token_hash", "decode(repeat('00', 31), 'hex')")]
    public async Task Database_refuses_device_fields_outside_the_limits(string column, string value)
    {
        var connectionString = await postgres.NewDatabaseAsync();
        await MigratorFor(connectionString).RunAsync(CancellationToken.None);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.ExecuteAsync("""
            INSERT INTO accounts (id, email, kdf_salt, kdf_params, auth_hash, recovery_hash, wrapped_key_password, wrapped_key_recovery, created_at)
            VALUES ('00000000-0000-0000-0000-000000000001', 'ana@exemplo.com', decode(repeat('00', 16), 'hex'), '{"v":1}',
                    decode(repeat('00', 32), 'hex'), decode(repeat('00', 32), 'hex'), decode('0102', 'hex'), decode('0304', 'hex'), now());
            INSERT INTO devices (id, account_id, name, token_hash, last_seen_at)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', 'pc', decode(repeat('00', 32), 'hex'), now());
            """);

        var act = () => connection.ExecuteAsync($"UPDATE devices SET {column} = {value}");

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }
}
