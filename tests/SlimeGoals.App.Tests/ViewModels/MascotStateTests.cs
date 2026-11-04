using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Tests.ViewModels;

public class MascotStateTests
{
    [Theory]
    [InlineData(0, 0, false, MascotPose.Sleeping, "Zzz…")]
    [InlineData(0, 0, true, MascotPose.Sleeping, "Zzz…")]
    [InlineData(3, 0, false, MascotPose.Idle, "Vamos começar?")]
    [InlineData(3, 1, false, MascotPose.Happy, "Continue assim!")]
    [InlineData(3, 2, false, MascotPose.Happy, "Falta pouco!")]
    [InlineData(1, 0, false, MascotPose.Idle, "Vamos começar?")]
    [InlineData(3, 3, false, MascotPose.Celebrating, "Tudo feito hoje!")]
    [InlineData(3, 3, true, MascotPose.Celebrating, "Tudo feito hoje!")]
    [InlineData(3, 1, true, MascotPose.Sleeping, "Zzz…")]
    public void For_maps_day_progress_to_pose_and_message(int total, int done, bool paused, MascotPose pose, string message) =>
        MascotState.For(total, done, paused).Should().Be(new MascotState(pose, message));
}
