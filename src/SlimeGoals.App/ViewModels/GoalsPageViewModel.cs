using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Tela Metas: lista (ativas ou arquivadas) e, quando <see cref="Editor"/> não é nulo,
/// a página de edição no lugar da lista (spec §7).
/// </summary>
public sealed partial class GoalsPageViewModel(GoalService service, TimeProvider time, TagService tags, PageNavigator navigator,
    DataChangeNotifier? changes = null)
    : PageViewModel
{
    private int _loadVersion;
    private bool _active = true;
    private bool _listening;
    private SynchronizationContext? _ui;

    public override string Title => "Metas";

    public override string IconKey => "NavGoals";

    public ObservableCollection<GoalItemViewModel> Goals { get; } = [];

    [ObservableProperty] public partial bool ShowArchived { get; set; }
    [ObservableProperty] public partial GoalEditViewModel? Editor { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public bool IsEmpty => Goals.Count == 0;
    public string EmptyMessage => ShowArchived ? "Nenhuma meta arquivada." : "Nenhuma meta ativa. Crie a primeira!";

    public override void Activate()
    {
        _active = true;
        LoadCommand.Execute(null);
        // O editor sobrevive à ida a Configurações › Tags: na volta, cores, nomes e exclusões de lá já aparecem nos chips.
        Editor?.Tags.ReloadCommand.Execute(null);
        UpdateListening();
    }

    public override void Deactivate()
    {
        _active = false;
        UpdateListening();
    }

    partial void OnEditorChanged(GoalEditViewModel? value) => UpdateListening();

    /// <summary>
    /// Com o editor aberto e a página à vista, uma mudança de fora (sincronização, outro processo) relê os chips: uma tag
    /// excluída sai da seleção antes de salvar, em vez de voltar com a meta (spec Tags §4.4.3). Fechado o editor ou
    /// escondida a página, deixa de ouvir; a volta à página relê de qualquer jeito.
    /// </summary>
    private void UpdateListening()
    {
        var listen = changes is not null && _active && Editor is not null;
        if (listen == _listening)
            return;
        _listening = listen;
        _ui ??= SynchronizationContext.Current;
        if (listen)
            changes!.Changed += OnDataChanged;
        else
            changes!.Changed -= OnDataChanged;
    }

    /// <summary>Pode chegar de qualquer thread; relê os chips na da interface.</summary>
    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (_ui is null)
            ReloadEditorTags();
        else
            _ui.Post(_ => ReloadEditorTags(), null);
    }

    private void ReloadEditorTags()
    {
        if (_listening)
            Editor?.Tags.ReloadCommand.Execute(null);
    }

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

            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            Goals.Clear();
            foreach (var goal in items)
                Goals.Add(new GoalItemViewModel(goal, time.LocalTimeZone, today));
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
    private void New() => Editor = new GoalEditViewModel(service, tags, null, measureLocked: false, CloseEditor, navigator: navigator);

    /// <summary>Abre o editor; meta com registros abre com a medida travada (spec §5.2).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync(GoalItemViewModel? item)
    {
        if (item is null)
            return;
        ErrorMessage = null;
        try
        {
            var locked = await service.HasRecordsAsync(item.Goal.Id);
            Editor = new GoalEditViewModel(service, tags, item.Goal, locked, CloseEditor, navigator: navigator);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Não foi possível abrir a meta.";
        }
    }

    private static bool CanEdit(GoalItemViewModel? item) => item is { IsArchived: false };

    [RelayCommand]
    private Task ArchiveAsync(GoalItemViewModel? item) =>
        RunAsync(item, id => service.ArchiveAsync(id));

    [RelayCommand]
    private Task UnarchiveAsync(GoalItemViewModel? item) =>
        RunAsync(item, id => service.UnarchiveAsync(id));

    // Meta arquivada só pode ser desarquivada ou excluída (spec §15); a exclusão pede confirmação no cartão.
    [RelayCommand]
    private static void RequestDelete(GoalItemViewModel? item)
    {
        if (item is not null) item.IsConfirmingDelete = true;
    }

    [RelayCommand]
    private static void CancelDelete(GoalItemViewModel? item)
    {
        if (item is not null) item.IsConfirmingDelete = false;
    }

    [RelayCommand]
    private Task ConfirmDeleteAsync(GoalItemViewModel? item) =>
        RunAsync(item, id => service.DeleteAsync(id));

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Erro inesperado do banco: mostra a mensagem em vez de derrubar o app.
            ErrorMessage = "Não foi possível concluir a ação.";
            return;
        }
        await LoadCommand.ExecuteAsync(null);
        ErrorMessage ??= error;
    }

    public override bool TryGoBack() => CloseEditorIfOpen();

    /// <summary>Voltar do sistema: fecha o editor aberto sem salvar. Devolve falso se não havia editor.</summary>
    public bool CloseEditorIfOpen()
    {
        if (Editor is null)
            return false;
        CloseEditor(saved: false);
        return true;
    }

    private void CloseEditor(bool saved)
    {
        Editor = null;
        if (saved)
            LoadCommand.Execute(null);
    }
}
