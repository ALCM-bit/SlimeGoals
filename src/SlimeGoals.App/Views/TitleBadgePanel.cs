using Avalonia;
using Avalonia.Controls;

namespace SlimeGoals.App.Views;

/// <summary>
/// Título e selo lado a lado, com o selo logo depois do título (a linha do Hoje no desktop, decisão 18). Numa coluna
/// estreita quem encolhe é o título (o primeiro filho, que corta com reticências); o selo (o segundo) nunca sai dela.
/// Um Grid "Auto,*" deixava o título longo passar por cima da coluna do progresso.
/// </summary>
public sealed class TitleBadgePanel : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TitleBadgePanel, double>(nameof(Spacing));

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private Control? Title => Children.Count > 0 ? Children[0] : null;

    private Control? Badge => Children.Count > 1 && Children[1].IsVisible ? Children[1] : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        var badge = Badge;
        badge?.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var badgeWidth = badge is null ? 0 : badge.DesiredSize.Width + Spacing;
        var title = Title;
        title?.Measure(new Size(Math.Max(0, availableSize.Width - badgeWidth), availableSize.Height));
        var width = (title?.DesiredSize.Width ?? 0) + badgeWidth;
        var height = Math.Max(title?.DesiredSize.Height ?? 0, badge?.DesiredSize.Height ?? 0);
        return new Size(double.IsFinite(availableSize.Width) ? Math.Min(width, availableSize.Width) : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var badge = Badge;
        var badgeWidth = badge is null ? 0 : badge.DesiredSize.Width + Spacing;
        var title = Title;
        // Sem selo, o título fica com a coluna toda; com ele, só o que o selo deixa, e o selo vem logo depois.
        var titleWidth = badge is null ? finalSize.Width
            : Math.Min(title?.DesiredSize.Width ?? 0, Math.Max(0, finalSize.Width - badgeWidth));
        title?.Arrange(new Rect(0, 0, titleWidth, finalSize.Height));
        if (badge is not null)
        {
            var y = (finalSize.Height - badge.DesiredSize.Height) / 2;
            badge.Arrange(new Rect(titleWidth + Spacing, y, badge.DesiredSize.Width, badge.DesiredSize.Height));
        }
        return finalSize;
    }
}
