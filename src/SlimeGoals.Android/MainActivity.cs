using Trace = System.Diagnostics.Trace;
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

    /// <summary>Conta as retomadas, para a conferência atrasada de <see cref="OnResume"/> saber se ainda é a dela.</summary>
    private int _resumes;

    private bool _resumed;

    /// <summary>Quanto esperar para tomar uma retomada como volta da pessoa ao app.</summary>
    private static readonly TimeSpan ResumeSettle = TimeSpan.FromSeconds(1);

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
        {
            HandleStopTimer(Intent);
            HandleOpenTempo(Intent);
        }
    }

    /// <summary>O app já estava aberto e o "Parar" da notificação, ou o aviso do Pomodoro, o trouxe para a frente.</summary>
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleStopTimer(intent);
        HandleOpenTempo(intent);
    }

    /// <summary>
    /// O toque no aviso de fim de bloco (ou na tela de alarme): cala o som e abre a aba Tempo. Outro app pode mandar o
    /// mesmo extra, mas ele só troca de página. O extra sai do intent.
    /// </summary>
    private static void HandleOpenTempo(Intent? intent)
    {
        if (intent?.GetBooleanExtra(AndroidFocusAlert.ExtraOpenTempo, false) != true)
            return;
        intent.RemoveExtra(AndroidFocusAlert.ExtraOpenTempo);
        if (AndroidApplication.Services is not { } services)
            return;
        // O shell assina os pedidos de troca de página ao nascer; numa abertura do zero ele pode não existir ainda.
        services.GetRequiredService<ShellViewModel>();
        services.GetRequiredService<FocusAlertHost>().Open();
    }

    /// <summary>
    /// "Parar" da notificação do cronômetro: para e mostra Hoje, se o pedido trouxer o código da instalação. A Activity
    /// é exportada, então outro app pode mandar o mesmo extra; sem o código, o pedido é ignorado. O extra sai do intent.
    /// </summary>
    private static void HandleStopTimer(Intent? intent)
    {
        if (intent?.HasExtra(AndroidTimerNotification.ExtraStopTimer) != true)
            return;
        // O extra vem como texto; um de outro tipo (de outro app, ou da notificação de antes do código) lê como nulo.
        var received = intent.Extras?.Get(AndroidTimerNotification.ExtraStopTimer) as Java.Lang.String;
        intent.RemoveExtra(AndroidTimerNotification.ExtraStopTimer);
        if (AndroidApplication.Services is not { } services)
            return;
        if (!StopRequestSecret.Matches(services.GetRequiredService<StopTimerSecretStore>().Get(), received?.ToString()))
        {
            Trace.TraceWarning("Pedido de parar o cronômetro sem o código da notificação: ignorado.");
            return;
        }
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
        AndroidApplication.NotifyDataChanged();
    }

    /// <summary>
    /// Ao voltar ao app (inclusive no dia seguinte, com o processo vivo) a notificação do cronômetro é reposta e as
    /// telas recarregam.
    /// A primeira retomada é a da abertura: as páginas já carregam sozinhas.
    /// </summary>
    protected override void OnResume()
    {
        base.OnResume();
        // A notificação do cronômetro pode ter sido dispensada (Android 14+): ao voltar ao app, ela volta.
        if (AndroidApplication.Services?.GetService<TimerNotificationSync>() is { } timerNotification)
            _ = Task.Run(() => timerNotification.SyncAsync());
        // §4: sincronizar ao voltar ao primeiro plano. Sem conta ligada, não faz nada.
        AndroidApplication.Services?.GetService<SlimeGoals.Sync.ISyncAccount>()?.SyncNow();
        if (_resumedBefore)
            AndroidApplication.NotifyDataChanged();
        _resumedBefore = true;
        _resumed = true;
        var resume = ++_resumes;
        // Voltar ao app avisa a página aberta (na aba Tempo, cala o aviso do Pomodoro). Acender a tela não é voltar: sem
        // bloqueio de tela, o próprio aviso acende a tela e retoma a Activity por um instante, antes de a tela de alarme
        // subir. Só conta a retomada que dura, com a janela em foco (nada por cima: tela de alarme, bloqueio).
        _ = Task.Delay(ResumeSettle).ContinueWith(_ => RunOnUiThread(() =>
        {
            if (resume == _resumes && _resumed && HasWindowFocus)
                AndroidApplication.Services?.GetService<ShellViewModel>()?.Resume();
        }), TaskScheduler.Default);
    }

    protected override void OnPause()
    {
        _resumed = false;
        base.OnPause();
    }
}
