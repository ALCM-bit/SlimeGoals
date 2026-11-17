using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using Android.Window;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// A tela de alarme do Pomodoro (spec Pomodoro §4.5): acende a tela e aparece sobre a tela de bloqueio, aberta pelo aviso
/// em tela cheia do <see cref="AndroidFocusAlert"/>. Por segurança, só mostra a fase ("Hora da pausa"): nenhuma meta,
/// tarefa ou tempo. "Parar som" cala e fecha; "Abrir" passa pelo desbloqueio e abre a aba Tempo. Nenhum dos dois muda a
/// sessão. Tarefa própria e fora dos recentes, para não trazer o app junto para cima da tela de bloqueio. Voltar também
/// cala e fecha. A tela some com o aviso: quando o som para por outro caminho (o minuto, dispensar a notificação, abrir a
/// aba Tempo) ou o aviso sai (a sessão seguiu), e não reabre sem um aviso tocando.
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

    /// <summary>A tela aberta agora, para o aviso fechá-la quando cala ou sai. Fraca: não segura a Activity.</summary>
    private static readonly WeakReference<FocusAlarmActivity?> s_shown = new(null);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        s_shown.SetTarget(this);
        SetContentView(Resource.Layout.focus_alarm);
        FindViewById<Button>(Resource.Id.focus_alarm_silence)!.Click += (_, _) => StopSound();
        FindViewById<Button>(Resource.Id.focus_alarm_open)!.Click += (_, _) => Open();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(IOnBackInvokedDispatcher.PriorityDefault,
                new BackCallback(this));
        ShowKind();
    }

    /// <summary>Volta para a tela depois de o som parar por outro caminho (ex.: atrás do desbloqueio): fecha.</summary>
    protected override void OnResume()
    {
        base.OnResume();
        if (!AndroidFocusAlert.IsRinging(this))
            Finish();
    }

    protected override void OnDestroy()
    {
        if (s_shown.TryGetTarget(out var shown) && ReferenceEquals(shown, this))
            s_shown.SetTarget(null);
        base.OnDestroy();
    }

    /// <summary>
    /// O aviso calou ou saiu: a tela aberta, se houver, fecha. Chega de qualquer thread; se, até a vez da thread da
    /// tela, um aviso novo voltou a tocar, ela fica para ele.
    /// </summary>
    internal static void CloseShown()
    {
        if (s_shown.TryGetTarget(out var shown) && shown is { IsFinishing: false })
            shown.RunOnUiThread(() =>
            {
                if (!AndroidFocusAlert.RingingHere)
                    shown.Finish();
            });
    }

    /// <summary>Voltar (até a API 32; da 33 em diante, o <see cref="BackCallback"/>): o mesmo que "Parar som".</summary>
    public override void OnBackPressed() => StopSound();

    private void StopSound()
    {
        Silence();
        Finish();
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
        StartActivity(AndroidFocusAlert.OpenTempoIntent(this));
        Silence();
        Finish();
    }

    private sealed class BackCallback(FocusAlarmActivity activity) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked() => activity.StopSound();
    }

    private sealed class DismissCallback(FocusAlarmActivity activity) : KeyguardManager.KeyguardDismissCallback
    {
        public override void OnDismissSucceeded() => activity.OpenApp();

        // Sem como pedir o desbloqueio aqui, o app abre assim mesmo: o Android o mostra depois do desbloqueio.
        public override void OnDismissError() => activity.OpenApp();
    }
}
