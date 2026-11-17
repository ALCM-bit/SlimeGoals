using System.Globalization;
using Avalonia.Data.Converters;

namespace SlimeGoals.App.Converters;

/// <summary>
/// Verdadeiro quando o primeiro texto existe e é diferente de todos os outros. Serve para não mostrar duas vezes a mesma
/// frase (ex.: o erro do Pomodoro igual ao motivo de "Começar" estar desligado, que já tem a faixa dele).
/// </summary>
public sealed class DistinctTextConverter : IMultiValueConverter
{
    public static DistinctTextConverter Instance { get; } = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count > 0 && values[0] is string { Length: > 0 } first
        && values.Skip(1).All(v => !string.Equals(v as string, first, StringComparison.Ordinal));
}
