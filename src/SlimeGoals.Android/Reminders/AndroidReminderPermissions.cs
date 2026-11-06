using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using SlimeGoals.App.Hosting;
using AProvider = Android.Provider;
using AUri = Android.Net.Uri;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// Permissões dos lembretes no Android. Os pedidos saem pela Activity atual (registrada pela
/// <see cref="MainActivity"/>); sem ela, não fazem nada. A resposta chega pelo aviso de dados mudados.
/// </summary>
internal sealed class AndroidReminderPermissions(Context context) : IReminderPermissions
{
    private const string PreferencesName = "permissoes";
    private const string AskedNotificationsKey = "notificacoesPedidas";

    /// <summary>Código do pedido de <c>POST_NOTIFICATIONS</c> no <c>OnRequestPermissionsResult</c>.</summary>
    public const int NotificationsRequestCode = 1001;

    /// <summary>A Activity em primeiro plano, ou nula.</summary>
    public Activity? CurrentActivity { get; set; }

    private NotificationManager Notifications
        => (NotificationManager)context.GetSystemService(Context.NotificationService)!;

    public ReminderPermissionStatus Get()
    {
        var manager = Notifications;
        // O canal só existe depois do primeiro lembrete; se o usuário o desligou, as notificações não aparecem.
        var channel = manager.GetNotificationChannel(AndroidNotificationService.ChannelId);
        var notifications = manager.AreNotificationsEnabled()
            && (channel is null || channel.Importance != NotificationImportance.None);

        var exactAlarms = !OperatingSystem.IsAndroidVersionAtLeast(31)
            || ((AlarmManager)context.GetSystemService(Context.AlarmService)!).CanScheduleExactAlarms();

        var battery = ((PowerManager)context.GetSystemService(Context.PowerService)!)
            .IsIgnoringBatteryOptimizations(context.PackageName);

        return new ReminderPermissionStatus(notifications, exactAlarms, battery);
    }

    public void RequestNotifications()
    {
        if (CurrentActivity is not { } activity)
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && activity.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            var preferences = activity.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
            var askedBefore = preferences.GetBoolean(AskedNotificationsKey, false);
            // Depois de duas recusas o sistema não mostra mais o diálogo: aí só as configurações resolvem.
            if (!askedBefore || activity.ShouldShowRequestPermissionRationale(Manifest.Permission.PostNotifications))
            {
                preferences.Edit()!.PutBoolean(AskedNotificationsKey, true)!.Apply();
                activity.RequestPermissions([Manifest.Permission.PostNotifications], NotificationsRequestCode);
                return;
            }
        }

        TryStart(activity,
            new Intent(AProvider.Settings.ActionAppNotificationSettings)
                .PutExtra(AProvider.Settings.ExtraAppPackage, activity.PackageName),
            AppDetails(activity));
    }

    public void RequestExactAlarms()
    {
        if (CurrentActivity is not { } activity || !OperatingSystem.IsAndroidVersionAtLeast(31))
            return;

        TryStart(activity,
            new Intent(AProvider.Settings.ActionRequestScheduleExactAlarm, PackageUri(activity)),
            AppDetails(activity));
    }

    public void RequestBatteryUnrestricted()
    {
        if (CurrentActivity is not { } activity)
            return;

        // Primeiro o diálogo direto; sem tela que o resolva, a lista geral de otimização de bateria.
        TryStart(activity,
            new Intent(AProvider.Settings.ActionRequestIgnoreBatteryOptimizations, PackageUri(activity)),
            new Intent(AProvider.Settings.ActionIgnoreBatteryOptimizationSettings));
    }

    private static AUri PackageUri(Activity activity) => AUri.Parse($"package:{activity.PackageName}")!;

    private static Intent AppDetails(Activity activity)
        => new(AProvider.Settings.ActionApplicationDetailsSettings, PackageUri(activity));

    /// <summary>
    /// Abre a primeira tela que o sistema tiver. Fabricantes podem remover telas de configuração, e a exceção
    /// derrubaria o app dentro do comando; sem nenhuma tela, o pedido só fica no Trace.
    /// </summary>
    private static void TryStart(Activity activity, params Intent[] intents)
    {
        foreach (var intent in intents)
        {
            try
            {
                activity.StartActivity(intent);
                return;
            }
            catch (ActivityNotFoundException)
            {
            }
        }

        System.Diagnostics.Trace.TraceError($"Nenhuma tela do sistema para: {intents[0].Action}");
    }
}
