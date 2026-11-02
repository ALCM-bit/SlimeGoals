using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// <see cref="IAppLifetime"/> do desktop. A <see cref="App"/> preenche <see cref="Desktop"/> ao iniciar;
/// antes disso, <see cref="Quit"/> não faz nada.
/// </summary>
public sealed class DesktopAppLifetime : IAppLifetime
{
    public IClassicDesktopStyleApplicationLifetime? Desktop { get; set; }

    public void Quit() => Dispatcher.UIThread.Post(() => Desktop?.Shutdown());
}
