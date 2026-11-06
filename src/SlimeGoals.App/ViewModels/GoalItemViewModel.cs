using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

/// <summary>Uma meta na lista. As datas de status são mostradas no fuso local recebido; hoje é a data nesse fuso.</summary>
public sealed partial class GoalItemViewModel(Goal goal, TimeZoneInfo zone, DateOnly today) : ObservableObject
{
    /// <summary>Confirmação inline de exclusão no cartão (só na lista de arquivadas).</summary>
    [ObservableProperty] public partial bool IsConfirmingDelete { get; set; }

    public Goal Goal { get; } = goal;
    public string Title => Goal.Title;
    public string IconKey => Goal.Icon;
    public GoalColor Color => Goal.Color;
    public string Summary { get; } = GoalFormatting.Summary(goal);
    public bool IsArchived => Goal.ArchivedAt is not null;

    /// <summary>O período já terminou: desarquivar seria recusado (spec §15), então o botão nem aparece.</summary>
    public bool IsPeriodOver => Goal.EndDate is { } end && end < today;

    public bool CanUnarchive => IsArchived && !IsPeriodOver;

    public string? Status => Goal.ArchivedAt is { } archived
        ? $"Arquivada em {LocalDay(archived)}{(IsPeriodOver ? " · período encerrado" : "")}"
        : null;

    private string LocalDay(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("dd/MM", CultureInfo.InvariantCulture);
}
