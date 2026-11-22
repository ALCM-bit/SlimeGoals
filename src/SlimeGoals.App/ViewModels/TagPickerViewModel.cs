using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Validation;

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
/// O quadro de tags da página da meta (spec Tags §4.6). "Criar" grava a tag no catálogo na hora, como em Configurações;
/// as ligações com a meta só são gravadas pelo <see cref="GoalService"/>, ao salvar a meta.
/// </summary>
/// <remarks>
/// A seleção é a base (as tags da meta no banco) mais o que a pessoa marcou e menos o que desmarcou nesta edição. A cada
/// recarga a base é relida: uma tag renomeada ou juntada em Configurações, ou excluída por uma sincronização, chega com o
/// nome de agora, e o que a pessoa mexeu continua valendo.
/// </remarks>
public sealed partial class TagPickerViewModel : ObservableObject
{
    private readonly TagService _service;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>?>>? _readBase;

    /// <summary>As tags da meta na última leitura do banco; vazia para uma meta nova.</summary>
    private HashSet<string> _base;

    /// <summary>Marcadas nesta edição (tocadas ou digitadas em "Nova tag").</summary>
    private readonly HashSet<string> _added = new(StringComparer.Ordinal);

    /// <summary>Desmarcadas nesta edição.</summary>
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);

    /// <summary>Nomes que eram tags visíveis na última leitura: um deles que sumiu foi excluído, e sai da seleção.</summary>
    private HashSet<string> _known = new(StringComparer.Ordinal);
    private int _version;

    /// <param name="initial">As tags da meta ao abrir o editor.</param>
    /// <param name="readBase">Relê as tags da meta no banco (nulo: a meta sumiu, fica a base de antes). Sem ele (meta
    /// nova), a base é <paramref name="initial"/>, e só perde as tags excluídas.</param>
    public TagPickerViewModel(TagService service, IReadOnlyList<string> initial,
        Func<CancellationToken, Task<IReadOnlyList<string>?>>? readBase = null)
    {
        _service = service;
        _readBase = readBase;
        _base = initial.ToHashSet(StringComparer.Ordinal);
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

    /// <summary>Relê as tags visíveis (cores, nomes, exclusões) e as da meta, mantendo o que a pessoa mexeu.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ReloadAsync(CancellationToken ct)
    {
        var version = ++_version;
        IReadOnlyList<TagSummary> list;
        IReadOnlyList<string>? fresh = null;
        try
        {
            list = await _service.ListAsync(ct);
            if (_readBase is not null)
                fresh = await _readBase(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return; // fica a lista de antes
        }
        if (version != _version)
            return;

        var visible = list.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        bool Deleted(string name) => _known.Contains(name) && !visible.Contains(name);
        if (fresh is not null)
            _base = fresh.ToHashSet(StringComparer.Ordinal);
        else
            _base.RemoveWhere(Deleted);
        // Marcada que era tag e sumiu foi excluída; marcada que nunca foi tag do aparelho (a caminho) continua.
        _added.RemoveWhere(Deleted);
        _known = visible;

        var selected = _base.Union(_added).Except(_removed).ToHashSet(StringComparer.Ordinal);
        Chips.Clear();
        foreach (var tag in list)
            Chips.Add(new TagChipViewModel(tag.Name, tag.Color, selected.Contains(tag.Name)));
        // Marcadas que ainda não são tags do aparelho: a tag está a caminho.
        foreach (var name in selected.Where(n => !visible.Contains(n)).Order(StringComparer.Ordinal))
            Chips.Add(new TagChipViewModel(name, GoalColor.Purple, isSelected: true));
    }

    [RelayCommand]
    private void Toggle(TagChipViewModel? chip)
    {
        if (chip is null)
            return;
        chip.IsSelected = !chip.IsSelected;
        Remember(chip.Name, chip.IsSelected);
    }

    /// <summary>
    /// Valida o nome e grava a tag (cria ou reativa uma excluída), já marcada. Um nome que já está nos chips só é marcado:
    /// uma tag a caminho não ganha linha nova, que venceria a verdadeira.
    /// </summary>
    [RelayCommand]
    private async Task CreateAsync(CancellationToken ct)
    {
        var name = TagName.Normalize(NewName);
        if (TagName.Validate(name) is { } error)
        {
            NewNameError = error;
            return;
        }

        if (Chips.FirstOrDefault(c => c.Name == name) is { } existing)
        {
            existing.IsSelected = true;
            Remember(name, selected: true);
            NewName = "";
            return;
        }

        try
        {
            await _service.CreateAsync(name, ct);
        }
        catch (GoalValidationException ex)
        {
            NewNameError = ex.Errors[0].Message;
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao criar a tag: {TraceText.Types(ex)}");
            NewNameError = TagsSettingsViewModel.SaveError;
            return;
        }
        Remember(name, selected: true);
        NewName = "";
        await ReloadAsync(ct); // a tag nova chega com a cor dela
    }

    private void Remember(string name, bool selected)
    {
        if (selected)
        {
            _removed.Remove(name);
            _added.Add(name);
        }
        else
        {
            _added.Remove(name);
            _removed.Add(name);
        }
    }
}
