using Avalonia.Controls;

namespace SlimeGoals.App.Views;

/// <summary>Fechar a janela só a oculta (os lembretes continuam); encerrar é pelo "Sair".</summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    /// <summary>Ligado pela <see cref="App"/> quando o encerramento foi pedido: aí o fechamento segue.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // O motivo do fechamento cobre o Shutdown mesmo se o ShutdownRequested não chegar antes.
        var shuttingDown = AllowClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (!shuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
