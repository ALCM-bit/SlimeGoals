using CommunityToolkit.Mvvm.ComponentModel;
using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

/// <summary>Um dia da semana no seletor de dias ativos.</summary>
public sealed partial class DayOption(WeekDays day, string label, string name) : ObservableObject
{
    public WeekDays Day { get; } = day;
    public string Label { get; } = label;   // letra no botão: "S", "T"…
    public string Name { get; } = name;     // nome completo, para leitores de tela

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Ícone no seletor; a View resolve o desenho pela chave.</summary>
public sealed record IconOption(string Key, string Name)
{
    public static IconOption From(GoalIcon icon) => new(icon.Key, icon.Name);
}

public sealed record ColorOption(GoalColor Color, string Name);

public sealed record IntervalOption(int Minutes)
{
    public string Label => GoalFormatting.Interval(Minutes);
    public override string ToString() => Label;
}
