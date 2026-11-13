using Microsoft.Data.Sqlite;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>
/// Abre conexões já configuradas. SQLite vem com chaves estrangeiras desligadas por padrão, e sem
/// secure_delete o que é apagado continua legível no arquivo até a página ser reaproveitada. Na primeira abertura,
/// o arquivo do banco fica legível só pelo dono.
/// </summary>
public sealed class SqliteConnectionFactory(string connectionString)
{
    private int _restricted;

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        // Uma vez por processo, depois que o arquivo existe: o SQLite cria o WAL com a permissão do banco.
        if (Interlocked.Exchange(ref _restricted, 1) == 0)
            DatabaseFiles.RestrictToOwner(connection.DataSource);
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA secure_delete = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }
}
