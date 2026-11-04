using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Controls.Platform;
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
    }

    private TopLevel? _topLevel;
    private bool _backSubscribed;
    private IInputPane? _inputPane;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        // Só no Android: no desktop Alt+Left / botão Voltar do mouse descartariam o formulário em edição.
        if (_topLevel is not null && Application.Current?.ApplicationLifetime is IActivityApplicationLifetime)
        {
            _topLevel.BackRequested += OnBackRequested;
            _backSubscribed = true;
            if (_topLevel.InputPane is { } inputPane)
            {
                _inputPane = inputPane;
                inputPane.StateChanged += OnInputPaneStateChanged;
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null && _backSubscribed)
            _topLevel.BackRequested -= OnBackRequested;
        _backSubscribed = false;
        if (_inputPane is not null)
            _inputPane.StateChanged -= OnInputPaneStateChanged;
        _inputPane = null;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Teclado virtual (Android): o teclado cobre a janela sem redimensioná-la, então o ScrollViewer ainda "enxerga"
    /// a área sob ele. Reserva embaixo a altura ocupada e, depois do novo layout, traz o campo em foco para a vista.
    /// A reserva vai na margem do conteúdo, não no Padding do shell: o Avalonia põe ali as áreas das barras do
    /// sistema, e um valor local apagaria essas margens de vez.
    /// </summary>
    private void OnInputPaneStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        var open = e.NewState == InputPaneState.Open;
        Root.Margin = new Thickness(0, 0, 0, open ? _inputPane?.OccludedRect.Height ?? 0 : 0);
        if (!open)
            return;
        DispatcherTimer.RunOnce(
            () => (_topLevel?.FocusManager?.GetFocusedElement() as Control)?.BringIntoView(),
            TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Voltar do Android: o shell consome quando há algo a fechar; em Hoje o sistema segue o padrão.</summary>
    private void OnBackRequested(object? sender, RoutedEventArgs e) =>
        e.Handled = (DataContext as ShellViewModel)?.GoBack() == true;
}
