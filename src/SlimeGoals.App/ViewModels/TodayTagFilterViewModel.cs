using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// O filtro por tags da lista "Metas de hoje" (spec Tags §4.8). Muda só quais cartões aparecem (<see cref="TodayGoalViewModel.IsShown"/>);
/// o resto da tela segue contando todas as metas. Seleção múltipla com OU. A seleção vive só aqui, em memória: sobrevive às
/// recargas e não é gravada.
/// </summary>
public sealed partial class TodayTagFilterViewModel : ObservableObject
{
    public const string FilteredEmptyMessage = "Nenhuma meta de hoje com essas tags.";

    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private IReadOnlyList<TodayGoalViewModel> _goals = [];

    /// <summary>Tags de alguma meta de hoje, das mais usadas para as menos; no empate, por nome.</summary>
    public ObservableCollection<TagChipViewModel> Chips { get; } = [];

    /// <summary>A linha some quando nenhuma meta de hoje tem tag.</summary>
    [ObservableProperty] public partial bool IsVisible { get; private set; }

    [ObservableProperty] public partial bool HasSelection { get; private set; }

    /// <summary>"5 de 9 metas", só com seleção.</summary>
    [ObservableProperty] public partial string? SummaryText { get; private set; }

    /// <summary>Há seleção e nenhum cartão passa por ela.</summary>
    [ObservableProperty] public partial bool IsFilteredEmpty { get; private set; }

    [RelayCommand]
    private void Toggle(TagChipViewModel? chip)
    {
        if (chip is null)
            return;
        if (!_selected.Remove(chip.Name))
            _selected.Add(chip.Name);
        chip.IsSelected = _selected.Contains(chip.Name);
        Apply();
    }

    [RelayCommand]
    private void Clear()
    {
        _selected.Clear();
        foreach (var chip in Chips)
            chip.IsSelected = false;
        Apply();
    }

    /// <summary>Remonta a linha com a lista de hoje. Tag marcada que não aparece mais sai da seleção.</summary>
    public void Refresh(IReadOnlyList<TodayGoalViewModel> goals)
    {
        _goals = goals;
        var rows = goals.SelectMany(g => g.Tags).GroupBy(t => t.Name, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Color: g.First().Color, Count: g.Count()))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
        _selected.IntersectWith(rows.Select(r => r.Name));
        Chips.Clear();
        foreach (var row in rows)
            Chips.Add(new TagChipViewModel(row.Name, row.Color, _selected.Contains(row.Name), row.Count));
        Apply();
    }

    private void Apply()
    {
        var any = _selected.Count > 0;
        foreach (var goal in _goals)
            goal.IsShown = !any || goal.Tags.Any(t => _selected.Contains(t.Name));
        var shown = _goals.Count(g => g.IsShown);
        IsVisible = Chips.Count > 0;
        HasSelection = any;
        SummaryText = any ? $"{shown} de {_goals.Count} metas" : null;
        IsFilteredEmpty = any && shown == 0;
    }
}
