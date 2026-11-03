namespace SlimeGoals.App.Hosting;

/// <summary>Estado das permissões dos lembretes: notificações e alarmes exatos.</summary>
public sealed record ReminderPermissionStatus(bool Notifications, bool ExactAlarms);

/// <summary>Permissões dos lembretes (Android). Pedir não espera a resposta: ela chega quando o app volta (aviso de dados mudados).</summary>
public interface IReminderPermissions
{
    ReminderPermissionStatus Get();

    void RequestNotifications();

    void RequestExactAlarms();
}
