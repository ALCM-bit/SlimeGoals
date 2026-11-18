using Dapper;

namespace SlimeGoals.Server.Data;

/// <summary>Recusa a partida quando o papel do app tem poder demais ou é o mesmo das migrações.</summary>
public static class DatabaseRoleGuard
{
    public static async Task EnsureSafeAsync(Db db, CancellationToken cancellationToken)
    {
        await using var app = await db.OpenAsync(cancellationToken);
        var role = await app.QuerySingleAsync<RoleInfo>(new CommandDefinition(
            """
            SELECT current_user::text AS Name, r.rolsuper AS Super, r.rolcreaterole AS CreateRole, r.rolcreatedb AS CreateDb
            FROM pg_roles r WHERE r.rolname = current_user
            """,
            cancellationToken: cancellationToken));

        await using var migrations = await db.OpenForMigrationsAsync(cancellationToken);
        var migrationsRole = await migrations.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT current_user::text", cancellationToken: cancellationToken));

        if (role.Super || role.CreateRole || role.CreateDb || role.Name == migrationsRole)
        {
            throw new InvalidOperationException(
                "O papel do banco do servidor não pode ser superusuário, criar papéis ou bancos, nem ser o das migrações.");
        }
    }

    private sealed record RoleInfo(string Name, bool Super, bool CreateRole, bool CreateDb);
}
