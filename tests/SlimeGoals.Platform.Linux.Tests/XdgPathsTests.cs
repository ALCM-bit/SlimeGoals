using SlimeGoals.Platform.Linux;

namespace SlimeGoals.Platform.Linux.Tests;

public class XdgPathsTests
{
    [Fact]
    public void Uses_xdg_data_home_when_set() =>
        XdgPaths.DatabasePath(v => v == "XDG_DATA_HOME" ? "/x/data" : null, "/home/u")
            .Should().Be("/x/data/SlimeGoals/slimegoals.db");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_to_home_local_share_when_unset_or_empty(string? value) =>
        XdgPaths.DatabasePath(_ => value, "/home/u")
            .Should().Be("/home/u/.local/share/SlimeGoals/slimegoals.db");

    [Fact]
    public void Runtime_directory_uses_xdg_runtime_dir_when_set() =>
        XdgPaths.RuntimeDirectory(v => v == "XDG_RUNTIME_DIR" ? "/run/user/1000" : null, "ana", "/tmp")
            .Should().Be("/run/user/1000");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Runtime_directory_falls_back_to_temp_per_user_when_unset_or_empty(string? value) =>
        XdgPaths.RuntimeDirectory(_ => value, "ana", "/tmp")
            .Should().Be("/tmp/slimegoals-ana");
}
