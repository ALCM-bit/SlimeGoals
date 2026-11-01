using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.Core;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Tela Metas: lista (ativas ou arquivadas) e, quando <see cref="Editor"/> não é nulo,
/// a página de edição no lugar da lista (spec §7).
/// </summary>
public sealed partial class GoalsPageViewModel(GoalService service, TimeProvider time) : PageViewModel
{
    private int _loadVersion;

    public override string Title => "Metas";

    public ObservableCollection<GoalItemViewModel> Goals { get; } = [];

    [ObservableProperty] public partial bool ShowArchived { get; set; }
    [ObservableProperty] public partial GoalEditViewModel? Editor { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public bool IsEmpty => Goals.Count == 0;
    public string EmptyMessage => ShowArchived ? "Nenhuma meta arquivada." : "Nenhuma meta ativa. Crie a primeira!";

    public override void Activate() => LoadCommand.Execute(null);

    partial void OnShowArchivedChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        LoadCommand.Execute(null);
    }

    /// <summary>Arquiva as metas vencidas e recarrega. Se houver cargas sobrepostas, vale a última.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        ErrorMessage = null;
        try
        {
            await service.ArchiveExpiredAsync();
            var all = await service.ListAsync(includeArchived: ShowArchived);
            if (version != _loadVersion)
                return;

            var items = ShowArchived
                ? all.Where(g => g.ArchivedAt is not null).OrderByDescending(g => g.ArchivedAt)
                : all.Where(g => g.ArchivedAt is null);

            Goals.Clear();
            foreach (var goal in items)
                Goals.Add(new GoalItemViewModel(goal, time.LocalTimeZone));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Não foi possível carregar as metas.";
        }
        finally
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private void New() => Editor = new GoalEditViewModel(service, null, CloseEditor);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit(GoalItemViewModel? item)
    {
        if (item is not null)
            Editor = new GoalEditViewModel(service, item.Goal, CloseEditor);
    }

    private static bool CanEdit(GoalItemViewModel? item) => item is { IsArchived: false };

    [RelayCommand]
    private Task ArchiveAsync(GoalItemViewModel? item) =>
        RunAsync(item, id => service.ArchiveAsync(id));

    [RelayCommand]
    private Task UnarchiveAsync(GoalItemViewModel? item) =>
        RunAsync(item, id => service.UnarchiveAsync(id));

    /// <summary>
    /// Executa a ação e recarrega. Erro de estado: mostra a mensagem sem recarregar (a lista continua certa).
    /// Meta sumiu: recarrega e só depois mostra a mensagem, porque a recarga limpa <see cref="ErrorMessage"/>.
    /// </summary>
    private async Task RunAsync(GoalItemViewModel? item, Func<Guid, Task> action)
    {
        if (item is null)
            return;
        ErrorMessage = null;
        string? error = null;
        try
        {
            await action(item.Goal.Id);
        }
        catch (GoalStateException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }
        catch (GoalNotFoundException)
        {
            error = "Esta meta não existe mais.";
        }
        await LoadCommand.ExecuteAsync(null);
        ErrorMessage ??= error;
    }

    private void CloseEditor(bool saved)
    {
        Editor = null;
        if (saved)
            LoadCommand.Execute(null);
    }
}
