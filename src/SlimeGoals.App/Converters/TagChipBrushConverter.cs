using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.Converters;

/// <summary>
/// (marcado, cor) + parâmetro "Back", "Line" ou "Text" → pincel do chip de tag. Marcado: fundo, borda e texto
/// na cor da tag (<c>Goal{Cor}Bg/Fg</c>); desmarcado: neutro (<c>Surface2</c>, <c>Border</c>, <c>Text</c>).
/// </summary>
public sealed class TagChipBrushConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [bool selected, GoalColor color] || parameter is not ("Back" or "Line" or "Text"))
            return BindingOperations.DoNothing;

        var key = (selected, parameter) switch
        {
            (true, "Back") => $"Goal{color}Bg",
            (true, _) => $"Goal{color}Fg",
            (false, "Back") => "Surface2",
            (false, "Line") => "Border",
            _ => "Text",
        };
        var app = Application.Current;
        return app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var brush)
            ? brush
            : BindingOperations.DoNothing;
    }
}
