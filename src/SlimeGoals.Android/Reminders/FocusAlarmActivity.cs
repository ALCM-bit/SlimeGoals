using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// A tela de alarme do Pomodoro (spec Pomodoro §4.5): acende a tela e aparece sobre a tela de bloqueio, aberta pelo aviso
/// em tela cheia do <see cref="AndroidFocusAlert"/>. Por segurança, só mostra a fase ("Hora da pausa"): nenhuma meta,
/// tarefa ou tempo. "Parar som" cala e fecha; "Abrir" passa pelo desbloqueio e abre a aba Tempo. Nenhum dos dois muda a
/// sessão. Tarefa própria e fora dos recentes, para não trazer o app junto para cima da tela de bloqueio.
/// </summary>
[Activity(
    Name = "dev.alcm.slimegoals.FocusAlarmActivity",
    Theme = "@style/SlimeGoals.AlarmTheme",
    Exported = false,
    ShowWhenLocked = true,
    TurnScreenOn = true,
    ExcludeFromRecents = true,
    TaskAffinity = "",
    LaunchMode = LaunchMode.SingleInstance)]
public sealed class FocusAlarmActivity : Activity
{
    public const string ExtraKind = "kind";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.focus_alarm);
        FindViewById<Button>(Resource.Id.focus_alarm_silence)!.Click += (_, _) =>
        {
            Silence();
            Finish();
        };
        FindViewById<Button>(Resource.Id.focus_alarm_open)!.Click += (_, _) => Open();
        ShowKind();
    }

    /// <summary>Um aviso novo com a tela ainda aberta: troca o texto.</summary>
    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        ShowKind();
    }

    // O extra vem só do aviso do app (a Activity não é exportada); um valor estranho fecha a tela.
    private void ShowKind()
    {
        if (!Enum.TryParse<FocusAlertKind>(Intent?.GetStringExtra(ExtraKind), out var kind) || !Enum.IsDefined(kind))
        {
            Finish();
            return;
        }
        FindViewById<TextView>(Resource.Id.focus_alarm_title)!.Text = FocusAlertText.Of(kind);
    }

    private static void Silence() => AndroidApplication.Services?.GetService<FocusAlerts>()?.Silence();

    /// <summary>Com a tela bloqueada, pede o desbloqueio antes; cancelado, a tela de alarme continua.</summary>
    private void Open()
    {
        var keyguard = (KeyguardManager)GetSystemService(KeyguardService)!;
        if (keyguard.IsKeyguardLocked)
            keyguard.RequestDismissKeyguard(this, new DismissCallback(this));
        else
            OpenApp();
    }

    private void OpenApp()
    {
        Silence();
        StartActivity(AndroidFocusAlert.OpenTempoIntent(this));
        Finish();
    }

    private sealed class DismissCallback(FocusAlarmActivity activity) : KeyguardManager.KeyguardDismissCallback
    {
        public override void OnDismissSucceeded() => activity.OpenApp();

        // Sem como pedir o desbloqueio aqui, o app abre assim mesmo: o Android o mostra depois do desbloqueio.
        public override void OnDismissError() => activity.OpenApp();
    }
}
