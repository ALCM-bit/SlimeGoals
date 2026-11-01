using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.Converters;

/// <summary>
/// <see cref="GoalColor"/> + parâmetro "Bg" ou "Fg" → pincel do tema (<c>Goal{Cor}{Bg|Fg}</c>, spec §11).
/// Assim as cores continuam vindo só dos tokens de Theme.axaml.
/// </summary>
public sealed class GoalColorToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not GoalColor color || parameter is not ("Bg" or "Fg"))
            return BindingOperations.DoNothing;

        var app = Application.Current;
        return app is not null && app.TryGetResource($"Goal{color}{parameter}", app.ActualThemeVariant, out var brush)
            ? brush
            : BindingOperations.DoNothing;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
