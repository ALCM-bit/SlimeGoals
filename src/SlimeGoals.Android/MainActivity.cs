using Android.App;
using Android.Content.PM;
using Android.Views;
using Avalonia.Android;

namespace SlimeGoals.Android;

/// <summary>Única Activity: hospeda o <c>ShellView</c>. Orientação livre; mudar a configuração não a recria.</summary>
[Activity(
    Label = "SlimeGoals",
    Theme = "@style/SlimeGoals.Theme",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize,
    WindowSoftInputMode = SoftInput.AdjustResize)]
public class MainActivity : AvaloniaMainActivity
{
}
