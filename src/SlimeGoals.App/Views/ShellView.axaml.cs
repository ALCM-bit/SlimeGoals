using Avalonia.Controls;

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
}
