using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Android.Reminders;

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

    private static AndroidReminderPermissions? Permissions
        => AndroidApplication.Services?.GetService<AndroidReminderPermissions>();

    /// <summary>Registra-se como a Activity dos pedidos de permissão.</summary>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Permissions is { } permissions)
            permissions.CurrentActivity = this;
        if (AndroidApplication.StartupFailure is { } failure)
            failure.CloseRequested += OnStartupFailureClose;
    }

    protected override void OnDestroy()
    {
        if (Permissions is { } permissions && ReferenceEquals(permissions.CurrentActivity, this))
            permissions.CurrentActivity = null;
        if (AndroidApplication.StartupFailure is { } failure)
            failure.CloseRequested -= OnStartupFailureClose;
        base.OnDestroy();
    }

    /// <summary>"Fechar" da tela de erro: sai do app e da lista de recentes.</summary>
    private void OnStartupFailureClose(object? sender, EventArgs e) => FinishAndRemoveTask();

    /// <summary>O usuário respondeu ao diálogo de permissão: a tela relê o estado.</summary>
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        (Application as AndroidApplication)?.NotifyDataChanged();
    }

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
