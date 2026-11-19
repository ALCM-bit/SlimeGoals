namespace SlimeGoals.App.Hosting;

/// <summary>O que a plataforma oferece; os ViewModels escondem o que não existe nela.</summary>
/// <param name="Sync">Seção "Conta e sincronização": só nos builds de desenvolvimento (fases 9.5a e 9.5b).</param>
/// <param name="MiniTimer">Janela pequena com o cronômetro rodando, quando a principal some: só no desktop.</param>
public sealed record PlatformCapabilities(bool Reminders, bool Autostart, bool WindowClose, bool Quit, bool Sync = false,
    bool MiniTimer = false)
{
    /// <summary>Desktop: tudo disponível, menos a sincronização, que o head liga à parte.</summary>
    public static PlatformCapabilities Desktop { get; } = new(true, true, true, true, MiniTimer: true);

    /// <summary>Android: só os lembretes. O sistema cuida da partida, da janela e do encerramento.</summary>
    public static PlatformCapabilities Android { get; } = new(Reminders: true, Autostart: false, WindowClose: false, Quit: false);
}
