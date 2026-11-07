using Avalonia;
using Avalonia.Controls;

namespace SlimeGoals.App.Views;

/// <summary>
/// StackPanel que pede toda a largura oferecida (já limitada pelo MaxWidth), para um estado de texto curto não encolher a
/// lista de Configurações, alinhada à esquerda. Diferente de um Width fixo, nunca passa da coluna: na janela mínima do
/// desktop (900 px) ela tem uns 622 px. Estilizado como StackPanel.
/// </summary>
public sealed class FillWidthStackPanel : StackPanel
{
    protected override Type StyleKeyOverride => typeof(StackPanel);

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        return double.IsFinite(availableSize.Width) ? size.WithWidth(Math.Max(size.Width, availableSize.Width)) : size;
    }
}
