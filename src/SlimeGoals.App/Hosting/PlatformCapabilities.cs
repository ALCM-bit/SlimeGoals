namespace SlimeGoals.App.Hosting;

/// <summary>O que a plataforma oferece; os ViewModels escondem o que não existe nela.</summary>
/// <param name="Sync">Seção "Conta e sincronização": só no build de desenvolvimento do desktop (fase 9.5a).</param>
public sealed record PlatformCapabilities(bool Reminders, bool Autostart, bool WindowClose, bool Quit, bool Sync = false)
{
    /// <summary>Desktop: tudo disponível, menos a sincronização, que o head liga à parte.</summary>
    public static PlatformCapabilities Desktop { get; } = new(true, true, true, true);
}
