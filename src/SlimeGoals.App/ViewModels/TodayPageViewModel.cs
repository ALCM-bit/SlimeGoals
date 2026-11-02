using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.Core;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Tela Hoje (spec §7): resumo da semana e cartões das metas de hoje com controles por medida.
/// Cada ação chama um serviço do Core e recarrega o instantâneo.
/// </summary>
public sealed partial class TodayPageViewModel(
    ProgressService progress, CheckinService checkins, TimerService timers, GoalService goals, TimeProvider time) : PageViewModel
{
    private int _loadVersion;
    private ITimer? _ticker;
    private SynchronizationContext? _ui;

    public override string Title => "Hoje";

    public ObservableCollection<TodayGoalViewModel> Goals { get; } = [];

    [ObservableProperty] public partial int WeekPercent { get; set; }
    [ObservableProperty] public partial int InProgress { get; set; }
    [ObservableProperty] public partial int Completed { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public bool IsEmpty => Goals.Count == 0;
    public string EmptyMessage => "Nenhuma meta para hoje. Crie uma em Metas.";

    public override void Activate() => LoadCommand.Execute(null);

    /// <summary>Arquiva as vencidas e monta o instantâneo. Se houver cargas sobrepostas, vale a última.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        // O tique do cronômetro precisa voltar à thread da interface (bindings do Avalonia).
        _ui ??= SynchronizationContext.Current;
        var version = ++_loadVersion;
        ErrorMessage = null;
        try
        {
            await goals.ArchiveExpiredAsync();
            var snapshot = await progress.GetTodayAsync();
            if (version != _loadVersion)
                return;

            StopTicker();
            Goals.Clear();
            foreach (var item in snapshot.Goals)
                Goals.Add(new TodayGoalViewModel(item, snapshot.Timer, time) { CanStartTimer = snapshot.Timer is null });
            (WeekPercent, InProgress, Completed) = (snapshot.Week.Percent, snapshot.Week.InProgress, snapshot.Week.Completed);
            if (snapshot.Timer is { StoppedAt: null })
                _ticker = time.CreateTimer(_ => OnUi(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Não foi possível carregar o dia.";
        }
        finally
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private Task CompleteAsync(TodayGoalViewModel? item) => RunAsync(item, id => checkins.AdjustAsync(id, 1));

    [RelayCommand]
    private Task UncompleteAsync(TodayGoalViewModel? item) => RunAsync(item, id => checkins.AdjustAsync(id, -1));

    [RelayCommand]
    private Task IncrementAsync(TodayGoalViewModel? item) => RunAsync(item, id => checkins.AdjustAsync(id, 1));

    [RelayCommand]
    private Task DecrementAsync(TodayGoalViewModel? item) => RunAsync(item, id => checkins.AdjustAsync(id, -1));

    [RelayCommand]
    private static void OpenPanel(TodayGoalViewModel? item) => item?.ResetPanel(open: true);

    [RelayCommand]
    private static void CancelPanel(TodayGoalViewModel? item) => item?.ResetPanel(open: false);

    [RelayCommand]
    private Task ConfirmPanelAsync(TodayGoalViewModel? item)
    {
        if (item is null)
            return Task.CompletedTask;
        if (item.PanelAmount() is not { } amount)
        {
            item.PanelError = "Informe um valor.";
            return Task.CompletedTask;
        }
        return RunAsync(item, id => checkins.AdjustAsync(id, amount, item.PanelNote), panel: true);
    }

    [RelayCommand]
    private Task StartTimerAsync(TodayGoalViewModel? item) => RunAsync(item, id => timers.StartAsync(id));

    [RelayCommand]
    private Task StopTimerAsync(TodayGoalViewModel? item) => RunAsync(item, _ => timers.StopAsync());

    [RelayCommand]
    private Task RegisterTimerAsync(TodayGoalViewModel? item)
    {
        if (item is null)
            return Task.CompletedTask;
        if (item.StoppedMinutesInput is not { } minutes)
        {
            item.PanelError = "Informe um valor.";
            return Task.CompletedTask;
        }
        var value = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        return RunAsync(item, _ => timers.RegisterAsync(value, item.PanelNote), panel: true);
    }

    [RelayCommand]
    private Task DiscardTimerAsync(TodayGoalViewModel? item) => RunAsync(item, _ => timers.DiscardAsync());

    /// <summary>
    /// Executa a ação e recarrega. Erro de validação numa ação de painel fica no painel, aberto (sem recarregar);
    /// os demais erros vão para a faixa de aviso, depois da recarga (que limpa <see cref="ErrorMessage"/>).
    /// </summary>
    private async Task RunAsync(TodayGoalViewModel? item, Func<Guid, Task> action, bool panel = false)
    {
        if (item is null)
            return;
        ErrorMessage = null;
        string? error = null;
        try
        {
            await action(item.Goal.Id);
        }
        catch (GoalValidationException ex) when (panel)
        {
            item.PanelError = ex.Errors[0].Message;
            return;
        }
        catch (GoalValidationException ex)
        {
            error = ex.Errors[0].Message;
        }
        catch (GoalStateException ex)
        {
            error = ex.Message;
        }
        catch (GoalNotFoundException)
        {
            error = "Esta meta não existe mais.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = "Não foi possível registrar.";
        }
        await LoadCommand.ExecuteAsync(null);
        ErrorMessage ??= error;
    }

    private void Tick()
    {
        foreach (var item in Goals)
            item.UpdateElapsed();
    }

    private void OnUi(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    private void StopTicker()
    {
        _ticker?.Dispose();
        _ticker = null;
    }
}
