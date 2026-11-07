using System.Globalization;
using System.Reflection;
using Dapper;

namespace SlimeGoals.Server.Data;

/// <summary>
/// Aplica os scripts embutidos em Data/Migrations, na ordem do nome. O bloqueio consultivo
/// da transação faz duas instâncias subindo juntas rodarem cada script uma vez só.
/// </summary>
public sealed class ServerMigrator(Db db)
{
    private const long LockKey = 0x534C494D45L; // "SLIME"

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(@key)", new { key = LockKey }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS schema_version (version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())",
            transaction: transaction, cancellationToken: cancellationToken));
        var current = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(version), 0) FROM schema_version", transaction: transaction, cancellationToken: cancellationToken));

        foreach (var (version, sql) in LoadScripts().Where(s => s.Version > current))
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_version (version) VALUES (@version)", new { version }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static IEnumerable<(int Version, string Sql)> LoadScripts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var file = resource[..^".sql".Length];
            file = file[(file.LastIndexOf('.') + 1)..];
            var version = int.Parse(file.Split('_')[0], CultureInfo.InvariantCulture);
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            yield return (version, reader.ReadToEnd());
        }
    }
}
