using SlimeGoals.App.Hosting;

namespace SlimeGoals.App.Tests.Hosting;

public class AppStartOptionsTests
{
    [Fact]
    public void AppStartOptions_parses_background_flag()
    {
        AppStartOptions.Parse(["--background"]).Background.Should().BeTrue();
        AppStartOptions.Parse([]).Background.Should().BeFalse();
        AppStartOptions.Parse(["--outro"]).Background.Should().BeFalse();
    }
}
