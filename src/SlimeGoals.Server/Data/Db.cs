using Npgsql;

namespace SlimeGoals.Server.Data;

/// <summary>Porta de entrada para o PostgreSQL: devolve conexões abertas do pool.</summary>
public sealed class Db(NpgsqlDataSource dataSource)
{
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) =>
        await dataSource.OpenConnectionAsync(cancellationToken);
}
