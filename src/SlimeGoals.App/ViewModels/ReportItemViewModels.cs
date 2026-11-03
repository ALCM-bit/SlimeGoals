using CommunityToolkit.Mvvm.ComponentModel;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

public enum ReportMode { Week, Month }

/// <summary>Meta que compõe uma barra: valor no dia ("8 de 8 copos") ou % na semana; IsMet marca o ✓.</summary>
public sealed record BarDetailViewModel(string Title, string IconKey, GoalColor Color, string Value, bool IsMet);

/// <summary>
/// Uma barra do gráfico. Fraction 0..1 já normalizada pela maior barra (registros) ou por 100 (%).
/// Clicar seleciona a barra e mostra embaixo as metas que a compõem (<see cref="Details"/>).
/// </summary>
public sealed partial class BarViewModel(string label, string value, double fraction, bool isCurrent,
    string detailTitle, IReadOnlyList<BarDetailViewModel> details, string emptyDetail) : ObservableObject
{
    public string Label { get; } = label;
    public string Value { get; } = value;
    public double Fraction { get; } = fraction;
    public bool IsCurrent { get; } = isCurrent;
    public string DetailTitle { get; } = detailTitle;
    public IReadOnlyList<BarDetailViewModel> Details { get; } = details;
    public bool HasDetails => Details.Count > 0;
    public string EmptyDetail { get; } = emptyDetail;

    /// <summary>Altura em px da barra (até 120, na área de 170 px). Número, não tipo visual.</summary>
    public double BarHeight => Math.Max(4, Fraction * 120);

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public sealed record ReportRowViewModel(string Title, string IconKey, GoalColor Color, string Detail,
    string PercentText, double PercentFraction, string? Extra, bool IsArchived);
