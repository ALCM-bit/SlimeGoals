using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SlimeGoals.App.Converters;

/// <summary>
/// <see cref="ViewModels.PageViewModel.IconKey"/> → <see cref="StreamGeometry"/> <c>Icon{chave}</c> do tema
/// (ex.: "NavToday" → <c>IconNavToday</c>). Chave vazia ou desconhecida: sem desenho.
/// </summary>
public sealed class NavIconConverter : IValueConverter
{
    public static NavIconConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } key
        && Application.Current?.TryGetResource("Icon" + key, Application.Current.ActualThemeVariant, out var resource) == true
        && resource is Geometry geometry
            ? geometry
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
