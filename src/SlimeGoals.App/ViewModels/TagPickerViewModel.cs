using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>Um chip de tag: o quadro da meta, Configurações e o filtro do Hoje usam o mesmo.</summary>
public sealed partial class TagChipViewModel(string name, GoalColor color, bool isSelected = false, int? count = null)
    : ObservableObject
{
    public string Name { get; } = name;

    public GoalColor Color { get; } = color;

    /// <summary>Quantas metas têm a tag; nulo onde a contagem não aparece (o quadro da meta).</summary>
    public int? Count { get; } = count;

    [ObservableProperty] public partial bool IsSelected { get; set; } = isSelected;
}

/// <summary>
/// O quadro de tags da página da meta (spec Tags §4.6). Só escolhe nomes: quem grava as tags e as ligações é o
/// <see cref="GoalService"/>, ao salvar a meta. Por isso "Criar" só põe um chip marcado na lista.
/// </summary>
public sealed partial class TagPickerViewModel : ObservableObject
{
    private readonly TagService _service;

    /// <summary>Nomes que eram tags visíveis na última leitura: um deles que sumiu foi excluído, e sai da seleção.</summary>
    private HashSet<string> _known = new(StringComparer.Ordinal);
    private int _version;

    public TagPickerViewModel(TagService service, IReadOnlyList<string> initial)
    {
        _service = service;
        // Antes da primeira leitura, os nomes da meta já aparecem marcados (em Purple): salvar cedo não os perde.
        foreach (var name in initial.Distinct(StringComparer.Ordinal))
            Chips.Add(new TagChipViewModel(name, GoalColor.Purple, isSelected: true));
    }

    public ObservableCollection<TagChipViewModel> Chips { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewNameError))]
    public partial string? NewNameError { get; private set; }

    [ObservableProperty] public partial string NewName { get; set; } = "";

    public bool HasNewNameError => NewNameError is not null;

    /// <summary>Os nomes marcados, na ordem dos chips.</summary>
    public IReadOnlyList<string> SelectedNames => [.. Chips.Where(c => c.IsSelected).Select(c => c.Name)];

    partial void OnNewNameChanged(string value) => NewNameError = null;

    /// <summary>Relê as tags visíveis (cores, nomes, exclusões), mantendo a seleção.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ReloadAsync()
    {
        var version = ++_version;
        IReadOnlyList<TagSummary> list;
        try
        {
            list = await _service.ListAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return; // fica a lista de antes
        }
        if (version != _version)
            return;

        var visible = list.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var selected = SelectedNames.Where(n => visible.Contains(n) || !_known.Contains(n)).ToHashSet(StringComparer.Ordinal);
        _known = visible;

        Chips.Clear();
        foreach (var tag in list)
            Chips.Add(new TagChipViewModel(tag.Name, tag.Color, selected.Contains(tag.Name)));
        // Marcadas que ainda não são tags do aparelho (a tag está a caminho, ou foi digitada e só grava ao salvar).
        foreach (var name in selected.Where(n => !visible.Contains(n)).Order(StringComparer.Ordinal))
            Chips.Add(new TagChipViewModel(name, GoalColor.Purple, isSelected: true));
    }

    [RelayCommand]
    private static void Toggle(TagChipViewModel? chip)
    {
        if (chip is not null)
            chip.IsSelected = !chip.IsSelected;
    }

    [RelayCommand]
    private void Create()
    {
        var name = TagName.Normalize(NewName);
        if (TagName.Validate(name) is { } error)
        {
            NewNameError = error;
            return;
        }

        if (Chips.FirstOrDefault(c => c.Name == name) is { } existing)
            existing.IsSelected = true;
        else
            Chips.Add(new TagChipViewModel(name, GoalColor.Purple, isSelected: true));
        NewName = "";
    }
}
