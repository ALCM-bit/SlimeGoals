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
    public void AutostartDirectory_uses_xdg_config_home_or_default()
    {
        XdgPaths.AutostartDirectory(v => v == "XDG_CONFIG_HOME" ? "/x/cfg" : null, "/home/u")
            .Should().Be("/x/cfg/autostart");
        XdgPaths.AutostartDirectory(_ => null, "/home/u").Should().Be("/home/u/.config/autostart");
        XdgPaths.AutostartDirectory(_ => "", "/home/u").Should().Be("/home/u/.config/autostart");
    }

    [Fact]
    public void Runtime_directory_uses_xdg_runtime_dir_when_set() =>
        XdgPaths.RuntimeDirectory(v => v == "XDG_RUNTIME_DIR" ? "/run/user/1000" : null, "/home/u")
            .Should().Be("/run/user/1000");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Runtime_directory_falls_back_to_user_cache_when_unset_or_empty(string? value) =>
        XdgPaths.RuntimeDirectory(_ => value, "/home/u")
            .Should().Be("/home/u/.cache/SlimeGoals");

    [Fact]
    public void Runtime_directory_fallback_honors_xdg_cache_home() =>
        XdgPaths.RuntimeDirectory(v => v == "XDG_CACHE_HOME" ? "/data/cache" : null, "/home/u")
            .Should().Be("/data/cache/SlimeGoals");
}
