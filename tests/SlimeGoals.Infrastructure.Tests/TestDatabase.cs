using Microsoft.Data.Sqlite;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests;

/// <summary>
/// Banco SQLite em memória compartilhado e exclusivo do teste. O banco vive enquanto
/// a conexão-âncora estiver aberta; cada operação do repositório abre a sua própria conexão.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _anchor;

    public TestDatabase(bool migrate = true)
    {
        Factory = new SqliteConnectionFactory($"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _anchor = Factory.Open();
        if (migrate) MigrationRunner.Migrate(_anchor);
    }

    public SqliteConnectionFactory Factory { get; }

    public SqliteConnection Anchor => _anchor;

    public void Dispose() => _anchor.Dispose();
}
