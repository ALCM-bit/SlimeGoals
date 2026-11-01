using SlimeGoals.Platform.Linux;

namespace SlimeGoals.Platform.Linux.Tests;

public class InputMethodEnvironmentTests
{
    private readonly Dictionary<string, string?> _env = new();

    private void Apply() => InputMethodEnvironment.Apply(k => _env.GetValueOrDefault(k), (k, v) => _env[k] = v);

    [Fact]
    public void Disables_dbus_ime_when_variable_is_not_set()
    {
        Apply();

        _env[InputMethodEnvironment.Variable].Should().Be("none");
    }

    [Fact]
    public void Keeps_the_value_the_user_chose()
    {
        _env[InputMethodEnvironment.Variable] = "fcitx";

        Apply();

        _env[InputMethodEnvironment.Variable].Should().Be("fcitx");
    }
}
