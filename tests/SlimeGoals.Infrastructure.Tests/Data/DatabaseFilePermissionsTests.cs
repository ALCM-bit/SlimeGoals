using System.Runtime.Versioning;
using Microsoft.Data.Sqlite;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

[UnsupportedOSPlatform("windows")]
public sealed class DatabaseFilePermissionsTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _directory = Directory.CreateTempSubdirectory("slimegoals-perm-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Opening_the_database_leaves_it_readable_only_by_the_owner()
    {
        var path = Path.Combine(_directory, "slimegoals.db");
        var factory = new SqliteConnectionFactory(new SqliteConnectionStringBuilder { DataSource = path }.ToString());

        using (var connection = factory.Open())
            MigrationRunner.Migrate(connection);

        File.GetUnixFileMode(path).Should().Be(OwnerOnly);
    }

    [Fact]
    public void Old_copies_beside_the_database_are_restricted_too()
    {
        var path = Path.Combine(_directory, "slimegoals.db");
        var backup = path + ".bak-v3-2026-11-06";
        var other = Path.Combine(_directory, "outro-arquivo.txt");
        File.WriteAllText(backup, "cópia antiga");
        File.WriteAllText(other, "não é do banco");
        File.SetUnixFileMode(backup, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        File.SetUnixFileMode(other, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var factory = new SqliteConnectionFactory(new SqliteConnectionStringBuilder { DataSource = path }.ToString());

        using (factory.Open())
        {
        }

        File.GetUnixFileMode(backup).Should().Be(OwnerOnly);
        File.GetUnixFileMode(other).Should().HaveFlag(UnixFileMode.OtherRead, "só os arquivos do banco mudam");
    }

    [Fact]
    public void A_database_in_memory_opens_without_touching_any_file()
    {
        var factory = new SqliteConnectionFactory("Data Source=perm-test;Mode=Memory;Cache=Shared");

        using var connection = factory.Open();

        connection.State.Should().Be(System.Data.ConnectionState.Open);
        File.Exists("perm-test").Should().BeFalse();
    }
}
