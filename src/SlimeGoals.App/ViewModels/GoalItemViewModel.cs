using System.Globalization;
using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

/// <summary>Uma meta na lista. As datas de status são mostradas no fuso local recebido.</summary>
public sealed class GoalItemViewModel(Goal goal, TimeZoneInfo zone)
{
    public Goal Goal { get; } = goal;
    public string Title => Goal.Title;
    public string IconKey => Goal.Icon;
    public GoalColor Color => Goal.Color;
    public string Summary { get; } = GoalFormatting.Summary(goal);
    public bool IsArchived => Goal.ArchivedAt is not null;
    public bool IsCompletedOnce => Goal is { Recurrence: Recurrence.Once, CompletedAt: not null };
    public bool CanUnarchive => IsArchived && !IsCompletedOnce;

    public string? Status => Goal switch
    {
        { Recurrence: Recurrence.Once, CompletedAt: { } done } => $"Concluída em {LocalDay(done)}",
        { ArchivedAt: { } archived } => $"Arquivada em {LocalDay(archived)}",
        _ => null,
    };

    private string LocalDay(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("dd/MM", CultureInfo.InvariantCulture);
}
