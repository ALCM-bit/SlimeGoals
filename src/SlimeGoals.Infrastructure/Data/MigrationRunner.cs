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
        var scripts = Scripts().ToList();
        var latest = scripts.Count == 0 ? 0 : scripts[^1].Number;
        EnsureNotNewer(ReadVersion(connection, tx: null), latest);

        foreach (var (number, resource) in scripts)
        {
            // IMMEDIATE trava o banco para escrita já no BEGIN: se outro processo migrou primeiro,
            // a versão relida aqui dentro já reflete isso e o script é pulado.
            using var tx = connection.BeginTransaction(deferred: false);
            if (ReadVersion(connection, tx) >= number)
                continue;

            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            // PRAGMA não aceita parâmetros; o número vem do nome do recurso, não do usuário.
            cmd.CommandText = ReadResource(resource) + $"\nPRAGMA user_version = {number.ToString(CultureInfo.InvariantCulture)};";
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        return ReadVersion(connection, tx: null);
    }

    /// <summary>Banco criado por uma versão mais nova do app: abrir com o esquema antigo arriscaria corromper dados.</summary>
    private static void EnsureNotNewer(int version, int latest)
    {
        if (version > latest)
            throw new InvalidOperationException(
                $"O banco está na versão {version}, mais nova que a suportada por este app ({latest}). Atualize o SlimeGoals.");
    }

    private static int ReadVersion(SqliteConnection connection, SqliteTransaction? tx)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
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
