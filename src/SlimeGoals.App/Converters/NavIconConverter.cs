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

    /// <summary>
    /// Com o parâmetro "Side" (barra lateral do desktop), tenta antes <c>IconSide{chave}</c>: os ícones do mockup
    /// (decisão 18), sem mudar os da navegação inferior do celular.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } key
            ? (parameter is string { Length: > 0 } variant ? Find("Icon" + variant + key) : null) ?? Find("Icon" + key)
            : null;

    private static Geometry? Find(string resourceKey) =>
        Application.Current?.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var resource) == true
            ? resource as Geometry
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
