using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SlimeGoals.App.Goals;

namespace SlimeGoals.App.Converters;

/// <summary>Chave de ícone → desenho (24×24). Chave desconhecida cai no ícone padrão.</summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Geometry> Cache = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var icon = GoalIcons.Get(value as string);
        return Cache.GetOrAdd(icon.Key, _ => StreamGeometry.Parse(icon.PathData));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
