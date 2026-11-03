namespace SlimeGoals.App.Hosting;

/// <summary>O que a plataforma oferece; os ViewModels escondem o que não existe nela.</summary>
public sealed record PlatformCapabilities(bool Reminders, bool Autostart, bool WindowClose, bool Quit)
{
    /// <summary>Desktop: tudo disponível.</summary>
    public static PlatformCapabilities Desktop { get; } = new(true, true, true, true);
}
