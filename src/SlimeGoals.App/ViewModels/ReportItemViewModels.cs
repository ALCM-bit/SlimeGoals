using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

public enum ReportMode { Week, Month }

/// <summary>Fraction 0..1 já normalizada pela maior barra (registros) ou por 100 (%).</summary>
public sealed record BarViewModel(string Label, string Value, double Fraction, bool IsCurrent)
{
    /// <summary>Altura em px da barra na área de 150 px (número, não tipo visual).</summary>
    public double BarHeight => Math.Max(4, Fraction * 120);
}

public sealed record ReportRowViewModel(string Title, string IconKey, GoalColor Color, string Detail,
    string PercentText, double PercentFraction, string? Extra, bool IsArchived);
