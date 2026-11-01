using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

/// <summary>Uma meta na lista. As datas de status são mostradas no fuso local recebido.</summary>
public sealed partial class GoalItemViewModel(Goal goal, TimeZoneInfo zone) : ObservableObject
{
    /// <summary>Confirmação inline de exclusão no cartão (só na lista de arquivadas).</summary>
    [ObservableProperty] public partial bool IsConfirmingDelete { get; set; }

    public Goal Goal { get; } = goal;
    public string Title => Goal.Title;
    public string IconKey => Goal.Icon;
    public GoalColor Color => Goal.Color;
    public string Summary { get; } = GoalFormatting.Summary(goal);
    public bool IsArchived => Goal.ArchivedAt is not null;
    public bool CanUnarchive => IsArchived;

    public string? Status => Goal.ArchivedAt is { } archived ? $"Arquivada em {LocalDay(archived)}" : null;

    private string LocalDay(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("dd/MM", CultureInfo.InvariantCulture);
}
