using Avalonia.Controls;

namespace SlimeGoals.App.Views;

/// <summary>Fechar a janela oculta (os lembretes continuam) ou encerra, conforme a configuração; o "Sair" sempre encerra.</summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Ligado pela <see cref="App"/> quando o encerramento foi pedido: aí o fechamento segue.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Ligado pela <see cref="App"/>: verdadeiro quando fechar a janela deve encerrar o app.</summary>
    public Func<bool>? QuitOnClose { get; set; }

    /// <summary>Ligado pela <see cref="App"/>: o mesmo caminho do "Sair".</summary>
    public Action? Quit { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // O motivo do fechamento cobre o Shutdown mesmo se o ShutdownRequested não chegar antes.
        var shuttingDown = AllowClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (!shuttingDown)
        {
            e.Cancel = true;
            if (QuitOnClose?.Invoke() == true && Quit is not null)
                Quit();
            else
                Hide();
        }
        base.OnClosing(e);
    }
}
