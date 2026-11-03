using SlimeGoals.Platform.Linux.Autostart;

namespace SlimeGoals.Platform.Linux.Tests;

public sealed class XdgAutostartTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slimegoals-test-" + Guid.NewGuid().ToString("N"));
    private string Dir => Path.Combine(_root, "autostart");
    private string EntryPath => Path.Combine(Dir, XdgAutostart.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Enable_writes_entry_with_quoted_exec_and_background_flag()
    {
        var sut = new XdgAutostart(Dir, "/opt/Meus Apps/slimegoals");
        sut.Enable();

        File.ReadAllText(EntryPath).Should().Be(
            "[Desktop Entry]\nType=Application\nName=SlimeGoals\nComment=Metas e lembretes\n" +
            "Exec=\"/opt/Meus Apps/slimegoals\" --background\nIcon=slimegoals\nTerminal=false\n" +
            "X-GNOME-Autostart-enabled=true\n");
        sut.IsEnabled().Should().BeTrue();
    }

    [Fact]
    public void QuoteExec_escapes_special_characters() =>
        XdgAutostart.QuoteExec("a\"b$c`d\\e%f").Should().Be("\"a\\\\\"b\\\\$c\\\\`d\\\\\\\\e%%f\"");

    [Fact]
    public void Disable_removes_entry_and_is_idempotent()
    {
        var sut = new XdgAutostart(Dir, "/x/slimegoals");
        sut.Enable();
        sut.Disable();

        File.Exists(EntryPath).Should().BeFalse();
        sut.IsEnabled().Should().BeFalse();
        var again = () => sut.Disable();
        again.Should().NotThrow();
    }

    [Theory]
    [InlineData("Hidden=true")]
    [InlineData("X-GNOME-Autostart-enabled=false")]
    public void Entry_hidden_by_user_counts_as_disabled(string line)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(EntryPath, $"[Desktop Entry]\nType=Application\n{line}\n");
        var sut = new XdgAutostart(Dir, "/x/slimegoals");

        sut.IsEnabled().Should().BeFalse();
        sut.Enable();
        sut.IsEnabled().Should().BeTrue();
        File.ReadAllText(EntryPath).Should().NotContain(line);
    }

    [Fact]
    public void Entry_pointing_to_another_executable_counts_as_disabled_and_enable_rewrites_it()
    {
        new XdgAutostart(Dir, "/velho/slimegoals").Enable();
        var sut = new XdgAutostart(Dir, "/novo/slimegoals");

        sut.IsEnabled().Should().BeFalse();
        sut.Enable();
        sut.IsEnabled().Should().BeTrue();
        File.ReadAllText(EntryPath).Should().Contain("Exec=\"/novo/slimegoals\" --background\n")
            .And.NotContain("/velho/");
    }
}
