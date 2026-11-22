using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Configurações › Tags (spec Tags §4.7): a lista das tags visíveis, cada uma com cor, uso, renomear e excluir, e o
/// campo de "+ Nova tag" no topo. Cada ação grava na hora e relê a lista.
/// </summary>
public sealed partial class TagsSettingsViewModel : ObservableObject
{
    internal const string SaveError = "Não foi possível salvar a tag.";

    private readonly TagService _service;
    private int _version;

    public TagsSettingsViewModel(TagService service)
    {
        _service = service;
        Rows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Summary));
        };
    }

    public ObservableCollection<TagRowViewModel> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyMessage => "Nenhuma tag. Crie uma aqui ou na página de uma meta.";

    /// <summary>Resumo na linha da lista de grupos.</summary>
    public string Summary => Rows.Count switch
    {
        0 => "Nenhuma tag",
        1 => "1 tag",
        var n => $"{n} tags",
    };

    public IReadOnlyList<ColorOption> Colors { get; } =
    [
        new(GoalColor.Purple, "Roxo"), new(GoalColor.Pink, "Rosa"), new(GoalColor.Amber, "Âmbar"), new(GoalColor.Green, "Verde"),
    ];

    [ObservableProperty] public partial bool IsCreating { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewNameError))]
    public partial string? NewNameError { get; private set; }

    [ObservableProperty] public partial string NewName { get; set; } = "";

    public bool HasNewNameError => NewNameError is not null;

    partial void OnNewNameChanged(string value) => NewNameError = null;

    /// <summary>Relê as tags. Uma leitura que termina depois de outra mais nova é descartada.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    internal async Task LoadAsync()
    {
        var version = ++_version;
        IReadOnlyList<TagSummary> list;
        try
        {
            list = await _service.ListAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao carregar as tags: {TraceText.Types(ex)}");
            return; // fica a lista de antes
        }
        if (version != _version)
            return;

        Rows.Clear();
        foreach (var tag in list)
            Rows.Add(new TagRowViewModel(tag, this, _service));
    }

    [RelayCommand]
    private void BeginCreate()
    {
        NewName = "";
        NewNameError = null;
        IsCreating = true;
    }

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreating = false;
        NewName = "";
        NewNameError = null;
    }

    [RelayCommand]
    private async Task ConfirmCreateAsync()
    {
        try
        {
            await _service.CreateAsync(NewName);
        }
        catch (GoalValidationException ex)
        {
            NewNameError = ex.Errors[0].Message;
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao criar a tag: {TraceText.Types(ex)}");
            NewNameError = SaveError;
            return;
        }
        IsCreating = false;
        NewName = "";
        await LoadAsync();
    }
}

/// <summary>
/// Uma linha da lista de tags. Os estados (cores, edição, junção, exclusão) se excluem: abrir um fecha os outros.
/// </summary>
public sealed partial class TagRowViewModel : ObservableObject
{
    private readonly TagsSettingsViewModel _owner;
    private readonly TagService _service;
    private readonly int _goalCount;

    internal TagRowViewModel(TagSummary summary, TagsSettingsViewModel owner, TagService service)
    {
        _owner = owner;
        _service = service;
        Name = summary.Name;
        Color = summary.Color;
        _goalCount = summary.ActiveGoals + summary.ArchivedGoals;
        UsageText = BuildUsage(summary);
        DeleteQuestion = _goalCount switch
        {
            0 => $"Excluir a tag \"{Name}\"?",
            1 => $"Excluir a tag \"{Name}\"? Ela sai de 1 meta.",
            var n => $"Excluir a tag \"{Name}\"? Ela sai de {n} metas.",
        };
    }

    public string Name { get; }

    public GoalColor Color { get; }

    public string UsageText { get; }

    public string DeleteQuestion { get; }

    public IReadOnlyList<ColorOption> Colors => _owner.Colors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsPickingColor { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsEditing { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditError))]
    public partial string? EditError { get; private set; }

    [ObservableProperty] public partial string EditText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsConfirmingDelete { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsConfirmingMerge { get; private set; }

    [ObservableProperty] public partial string MergeQuestion { get; private set; } = "";

    public bool HasEditError => EditError is not null;

    /// <summary>Nenhum estado aberto: a linha mostra nome, uso e os botões ✎ e 🗑.</summary>
    public bool IsIdle => !IsEditing && !IsConfirmingDelete && !IsConfirmingMerge;

    partial void OnEditTextChanged(string value) => EditError = null;

    [RelayCommand]
    private void TogglePicker() => IsPickingColor = !IsPickingColor;

    /// <summary>Grava a cor escolhida e fecha as cores.</summary>
    [RelayCommand]
    private async Task PickColorAsync(GoalColor color)
    {
        IsPickingColor = false;
        try
        {
            await _service.ChangeColorAsync(Name, color);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao trocar a cor da tag: {TraceText.Types(ex)}");
        }
        await _owner.LoadAsync();
    }

    [RelayCommand]
    private void BeginEdit()
    {
        IsPickingColor = false;
        IsConfirmingDelete = false;
        EditText = Name;
        EditError = null;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditError = null;
    }

    /// <summary>✓: valida e renomeia; se o nome novo já existe, pede a confirmação de juntar.</summary>
    [RelayCommand]
    private async Task ConfirmEditAsync()
    {
        try
        {
            if (await _service.RenameNeedsMergeAsync(Name, EditText))
            {
                MergeQuestion = $"Já existe a tag \"{TagName.Normalize(EditText)}\". Juntar as duas?";
                IsEditing = false;
                IsConfirmingMerge = true;
                return;
            }
            await _service.RenameAsync(Name, EditText);
        }
        catch (GoalValidationException ex)
        {
            EditError = ex.Errors[0].Message;
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao renomear a tag: {TraceText.Types(ex)}");
            EditError = TagsSettingsViewModel.SaveError;
            return;
        }
        IsEditing = false;
        await _owner.LoadAsync();
    }

    [RelayCommand]
    private async Task ConfirmMergeAsync()
    {
        try
        {
            await _service.RenameAsync(Name, EditText);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao juntar as tags: {TraceText.Types(ex)}");
            IsConfirmingMerge = false;
            EditError = TagsSettingsViewModel.SaveError;
            IsEditing = true;
            return;
        }
        IsConfirmingMerge = false;
        await _owner.LoadAsync();
    }

    /// <summary>Volta ao campo, com o texto que a pessoa digitou.</summary>
    [RelayCommand]
    private void CancelMerge()
    {
        IsConfirmingMerge = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void BeginDelete()
    {
        IsPickingColor = false;
        IsEditing = false;
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        try
        {
            await _service.DeleteAsync(Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao excluir a tag: {TraceText.Types(ex)}");
        }
        IsConfirmingDelete = false;
        await _owner.LoadAsync();
    }

    private static string BuildUsage(TagSummary tag)
    {
        var total = tag.ActiveGoals + tag.ArchivedGoals;
        if (total == 0)
            return "nenhuma meta";
        var text = total == 1 ? "usada em 1 meta" : $"usada em {total} metas";
        return tag.ArchivedGoals switch
        {
            0 => text,
            1 => $"{text} (1 arquivada)",
            var a => $"{text} ({a} arquivadas)",
        };
    }
}
