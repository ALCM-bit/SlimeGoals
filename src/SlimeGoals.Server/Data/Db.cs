using Npgsql;

namespace SlimeGoals.Server.Data;

/// <summary>
/// Porta de entrada para o PostgreSQL: devolve conexões abertas do pool. As conexões comuns usam o papel
/// do app (só linhas); as migrações usam o papel dono, ou o mesmo do app quando não há outro configurado.
/// </summary>
public sealed class Db(NpgsqlDataSource dataSource, NpgsqlDataSource? migrationsDataSource = null) : IAsyncDisposable
{
    private readonly NpgsqlDataSource _migrations = migrationsDataSource ?? dataSource;

    /// <summary>Fecha os pools; o contêiner chama isto ao encerrar. Não fecha duas vezes o mesmo pool.</summary>
    public async ValueTask DisposeAsync()
    {
        await dataSource.DisposeAsync();
        if (!ReferenceEquals(_migrations, dataSource))
        {
            await _migrations.DisposeAsync();
        }
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken) =>
        await dataSource.OpenConnectionAsync(cancellationToken);

    /// <summary>Conexão do papel dono, só para as migrações.</summary>
    public async Task<NpgsqlConnection> OpenForMigrationsAsync(CancellationToken cancellationToken) =>
        await _migrations.OpenConnectionAsync(cancellationToken);
}
