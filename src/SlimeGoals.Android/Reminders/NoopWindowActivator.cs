using SlimeGoals.App.Hosting;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// No Android não há janela a trazer: quando o Core pede para abrir o app, o receiver de ação troca a
/// notificação por uma que abre a <see cref="MainActivity"/> ao toque. Só satisfaz o <c>ReminderHost</c>.
/// </summary>
internal sealed class NoopWindowActivator : IWindowActivator
{
    public void Activate()
    {
    }
}
