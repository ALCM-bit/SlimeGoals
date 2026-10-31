using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>
/// Aplica os scripts Migrations/NNN_nome.sql (recursos embutidos) cujo número é maior
/// que o PRAGMA user_version do banco. Cada script roda numa transação própria.
/// </summary>
public static partial class MigrationRunner
{
    [GeneratedRegex(@"\.Migrations\.(\d{3})_[^.]+\.sql$")]
    private static partial Regex ScriptName();

    public static int Migrate(SqliteConnection connection)
    {
        var version = ReadVersion(connection);

        foreach (var (number, resource) in Scripts().Where(s => s.Number > version))
        {
            using var tx = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            // PRAGMA não aceita parâmetros; o número vem do nome do recurso, não do usuário.
            cmd.CommandText = ReadResource(resource) + $"\nPRAGMA user_version = {number.ToString(CultureInfo.InvariantCulture)};";
            cmd.ExecuteNonQuery();
            tx.Commit();
            version = number;
        }

        return version;
    }

    private static int ReadVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IEnumerable<(int Number, string Resource)> Scripts() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Select(name => (Match: ScriptName().Match(name), Name: name))
            .Where(x => x.Match.Success)
            .Select(x => (int.Parse(x.Match.Groups[1].Value, CultureInfo.InvariantCulture), x.Name))
            .OrderBy(x => x.Item1);

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
