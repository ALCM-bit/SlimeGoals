using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Threading;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Views;

public partial class ShellView : UserControl
{
    /// <summary>Abaixo desta largura o shell usa o layout estreito (classe <c>compact</c>, estilos em Controls.axaml).</summary>
    public const double CompactBreakpoint = 720;

    public ShellView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Classes.Set("compact", e.NewSize.Width < CompactBreakpoint);
        // Android: a página volta inteira depois de mudar com o app em segundo plano.
        LayoutUpdated += (_, _) => _layoutRepair.OnLayoutUpdated(this);
    }

    private readonly StaleLayoutRepair _layoutRepair = new();

    private TopLevel? _topLevel;
    private bool _backSubscribed;
    private KeyboardWatch? _keyboard;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        // Voltar ao app no desktop: a janela ganha o foco. No Android, quem avisa é a MainActivity, que sabe se a volta
        // foi da pessoa ou da tela acendendo.
        if (_topLevel is Window window)
            window.Activated += OnAppResumed;
        // Só no Android: no desktop Alt+Left / botão Voltar do mouse descartariam o formulário em edição.
        if (_topLevel is not null && Application.Current?.ApplicationLifetime is IActivityApplicationLifetime)
        {
            _topLevel.BackRequested += OnBackRequested;
            _backSubscribed = true;
            _keyboard = KeyboardWatch.Attach(_topLevel);
            if (_keyboard is not null)
            {
                _keyboard.Changed += OnKeyboardChanged;
                _keyboard.Refresh();
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is Window window)
            window.Activated -= OnAppResumed;
        if (_topLevel is not null && _backSubscribed)
            _topLevel.BackRequested -= OnBackRequested;
        _backSubscribed = false;
        _keyboard?.Dispose();
        _keyboard = null;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Teclado virtual (Android): o teclado cobre a janela sem redimensioná-la, então o ScrollViewer ainda "enxerga"
    /// a área sob ele. Reserva embaixo a altura ocupada e, depois do novo layout, traz o campo em foco para a vista.
    /// A reserva vai na margem do conteúdo, não no Padding do shell: o Avalonia põe ali as áreas das barras do
    /// sistema, e um valor local apagaria essas margens de vez. Ela sai também quando o teclado some sem animar, na
    /// volta do segundo plano (<see cref="KeyboardWatch"/>).
    /// </summary>
    private void OnKeyboardChanged(object? sender, EventArgs e)
    {
        if (sender is not KeyboardWatch keyboard)
            return;
        Root.Margin = new Thickness(0, 0, 0, keyboard.Height);
        if (!keyboard.IsOpen)
            return;
        DispatcherTimer.RunOnce(
            () => (_topLevel?.FocusManager?.GetFocusedElement() as Control)?.BringIntoView(),
            TimeSpan.FromMilliseconds(250));
    }

    private void OnAppResumed(object? sender, EventArgs e) => (DataContext as ShellViewModel)?.Resume();

    /// <summary>Voltar do Android: o shell consome quando há algo a fechar; em Hoje o sistema segue o padrão.</summary>
    private void OnBackRequested(object? sender, RoutedEventArgs e) =>
        e.Handled = (DataContext as ShellViewModel)?.GoBack() == true;
}
