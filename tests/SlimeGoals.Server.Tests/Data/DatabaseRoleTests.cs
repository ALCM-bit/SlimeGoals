using Dapper;
using Npgsql;
using SlimeGoals.Server.Data;

namespace SlimeGoals.Server.Tests.Data;

public sealed class DatabaseRoleTests(PostgresFixture postgres)
{
    private async Task<PostgresTestDatabase> MigratedDatabaseAsync()
    {
        var database = await postgres.NewDatabaseAsync();
        await new ServerMigrator(new Db(NpgsqlDataSource.Create(database.Owner))).RunAsync(CancellationToken.None);
        return database;
    }

    [Fact]
    public async Task App_role_cannot_create_or_drop_tables()
    {
        var database = await MigratedDatabaseAsync();
        await using var connection = new NpgsqlConnection(database.App);

        var create = () => connection.ExecuteAsync("CREATE TABLE x(i int)");
        var drop = () => connection.ExecuteAsync("DROP TABLE accounts");

        (await create.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
        (await drop.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task App_role_reads_and_writes_rows_after_migrations()
    {
        var database = await MigratedDatabaseAsync();
        await using var connection = new NpgsqlConnection(database.App);

        await connection.ExecuteAsync("""
            INSERT INTO accounts (id, email, kdf_salt, kdf_params, auth_hash, recovery_hash, wrapped_key_password, wrapped_key_recovery, created_at)
            VALUES ('00000000-0000-0000-0000-000000000001', 'ana@exemplo.com', decode(repeat('00', 16), 'hex'), '{"v":1}',
                    decode(repeat('00', 32), 'hex'), decode(repeat('00', 32), 'hex'), decode('0102', 'hex'), decode('0304', 'hex'), now())
            """);
        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM accounts")).Should().Be(1);
        await connection.ExecuteAsync("DELETE FROM accounts");

        (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM accounts")).Should().Be(0);
    }

    [Fact]
    public async Task Production_refuses_superuser_connection()
    {
        var database = await postgres.NewDatabaseAsync();
        var superuser = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = new NpgsqlConnectionStringBuilder(database.App).Database }.ConnectionString;
        await using var factory = ServerFactory.Production(postgres, new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = superuser,
            ["ConnectionStrings:PostgresMigrations"] = database.Owner,
        });

        var act = () => factory.CreateClient();

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("não pode ser superusuário");
        message.Should().NotContain(new NpgsqlConnectionStringBuilder(superuser).Password!);
        message.Should().NotContain(new NpgsqlConnectionStringBuilder(superuser).Host!);
    }

    [Fact]
    public async Task Production_refuses_same_role_for_app_and_migrations()
    {
        var database = await postgres.NewDatabaseAsync();
        await using var factory = ServerFactory.Production(postgres, new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = database.App,
            ["ConnectionStrings:PostgresMigrations"] = database.App,
        });

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("nem ser o das migrações");
    }

    [Fact]
    public async Task Production_starts_with_the_unprivileged_app_role()
    {
        await using var factory = ServerFactory.Production(postgres);

        using var client = factory.CreateClient();

        (await client.GetAsync("/health")).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_connection_string_fails_with_fixed_message()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = "" });

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("ConnectionStrings:Postgres não configurada.");
    }
}
