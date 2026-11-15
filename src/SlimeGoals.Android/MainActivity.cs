using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Avalonia.Android;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Android.Reminders;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Services;

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
        // Recriada (processo morto) ou aberta pelos recentes, a Activity recebe de novo o intent original: o "Parar"
        // dele já foi atendido e não pode parar um cronômetro iniciado depois.
        if (savedInstanceState is null && (Intent?.Flags & ActivityFlags.LaunchedFromHistory) == 0)
            HandleStopTimer(Intent);
    }

    /// <summary>O app já estava aberto e o "Parar" da notificação o trouxe para a frente.</summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleStopTimer(intent);
    }

    /// <summary>"Parar" da notificação do cronômetro: para e mostra Hoje. O extra sai do intent atendido.</summary>
    private static void HandleStopTimer(Intent? intent)
    {
        if (intent is null || !intent.GetBooleanExtra(AndroidTimerNotification.ExtraStopTimer, false))
            return;
        intent.RemoveExtra(AndroidTimerNotification.ExtraStopTimer);
        if (AndroidApplication.Services is not { } services)
            return;
        var timers = services.GetRequiredService<TimerService>();
        var shell = services.GetRequiredService<ShellViewModel>();
        Dispatcher.UIThread.Post(() => _ = StopRequest.RunAsync(timers, shell));
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
