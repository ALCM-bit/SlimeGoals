using System.Runtime.Versioning;
using SlimeGoals.Platform.Linux;

namespace SlimeGoals.Platform.Linux.Tests;

[SupportedOSPlatform("linux")]
public sealed class PrivateDirectoryTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private readonly string _root = Directory.CreateTempSubdirectory("slimegoals-private-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Creates_missing_directory_for_the_owner_only()
    {
        var path = Path.Combine(_root, "dados");

        PrivateDirectory.Ensure(path);

        File.GetUnixFileMode(path).Should().Be(OwnerOnly);
    }

    [Fact]
    public void Tightens_existing_directory_and_keeps_its_files()
    {
        var path = Path.Combine(_root, "dados");
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var file = Path.Combine(path, "slimegoals.db");
        File.WriteAllText(file, "dados");

        PrivateDirectory.Ensure(path);

        File.GetUnixFileMode(path).Should().Be(OwnerOnly);
        File.ReadAllText(file).Should().Be("dados");
    }

    [Fact]
    public void TryEnsure_does_not_throw_when_the_path_is_a_file()
    {
        var path = Path.Combine(_root, "arquivo");
        File.WriteAllText(path, "");

        var act = () => PrivateDirectory.TryEnsure(Path.Combine(path, "dados"));

        act.Should().NotThrow();
    }
}
