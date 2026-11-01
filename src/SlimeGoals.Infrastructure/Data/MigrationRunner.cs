using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>
/// Aplica os scripts Migrations/NNN_nome.sql (recursos embutidos) cujo número é maior
/// que o PRAGMA user_version do banco. Cada script roda numa transação própria.
/// Chaves estrangeiras ficam desligadas durante os scripts, para permitir recriar tabelas
/// referenciadas (procedimento oficial do SQLite para ALTER TABLE), e são conferidas antes de cada COMMIT.
/// </summary>
public static partial class MigrationRunner
{
    [GeneratedRegex(@"\.Migrations\.(\d{3})_[^.]+\.sql$")]
    private static partial Regex ScriptName();

    /// <param name="upTo">Para na versão indicada (testes de migração com dados antigos). Padrão: a mais recente.</param>
    public static int Migrate(SqliteConnection connection, int upTo = int.MaxValue)
    {
        var all = Scripts().ToList();
        // "Mais novo que o app" compara com o último script existente, não com o limite upTo.
        EnsureNotNewer(ReadVersion(connection, tx: null), all.Count == 0 ? 0 : all[^1].Number);
        var scripts = all.Where(s => s.Number <= upTo).ToList();

        // foreign_keys só pode mudar fora de transação.
        var foreignKeys = Pragma(connection, "foreign_keys");
        SetForeignKeys(connection, false);
        try
        {
            ApplyScripts(connection, scripts);
        }
        finally
        {
            SetForeignKeys(connection, foreignKeys == 1);
        }

        return ReadVersion(connection, tx: null);
    }

    private static void ApplyScripts(SqliteConnection connection, List<(int Number, string Resource)> scripts)
    {
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
            EnsureForeignKeysValid(connection, tx, number);
            tx.Commit();
        }
    }

    private static void EnsureForeignKeysValid(SqliteConnection connection, SqliteTransaction tx, int number)
    {
        using var check = connection.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "PRAGMA foreign_key_check;";
        using var reader = check.ExecuteReader();
        if (reader.Read())
            throw new InvalidOperationException(
                $"A migração {number:000} deixaria referências inválidas (tabela {reader.GetString(0)}).");
    }

    private static long Pragma(SqliteConnection connection, string name)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA {name};";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void SetForeignKeys(SqliteConnection connection, bool on)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = on ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;";
        cmd.ExecuteNonQuery();
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
