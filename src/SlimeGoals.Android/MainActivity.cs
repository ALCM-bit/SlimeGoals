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
    private bool _resumedBefore;

    /// <summary>
    /// Ao voltar ao app (inclusive no dia seguinte, com o processo vivo) as telas recarregam.
    /// A primeira retomada é a da abertura: as páginas já carregam sozinhas.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();
        if (_resumedBefore)
            (Application as AndroidApplication)?.NotifyDataChanged();
        _resumedBefore = true;
    }
}
