using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.BackRequested += OnBackRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.BackRequested -= OnBackRequested;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Voltar do Android: o shell consome quando há algo a fechar; em Hoje o sistema segue o padrão.</summary>
    private void OnBackRequested(object? sender, RoutedEventArgs e) =>
        e.Handled = (DataContext as ShellViewModel)?.GoBack() == true;
}
