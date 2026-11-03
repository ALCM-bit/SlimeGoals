using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Goals;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.App.ViewModels;

/// <summary>Linha de "Próximos lembretes": hora local ("14:30") e título da meta.</summary>
public sealed record UpcomingReminderViewModel(string Time, string Title);

/// <summary>
/// Tela Hoje (spec §7): resumo da semana, cartões das metas de hoje com controles por medida
/// e os próximos lembretes do dia. Cada ação chama um serviço do Core e recarrega o instantâneo;
/// escritas de fora da tela (notificação, virada do dia) chegam pelo <see cref="DataChangeNotifier"/>.
/// </summary>
public sealed partial class TodayPageViewModel(ProgressService progress, CheckinService checkins, TimerService timers,
    GoalService goals, TimeProvider time, ReminderService reminders, DataChangeNotifier changes,
    ReminderPause? pause = null, ReportService? reports = null) : PageViewModel
{
    private bool _subscribed;
    private int _loadVersion;
    private ITimer? _ticker;
    private SynchronizationContext? _ui;
    private ActiveTimer? _offscreenTimer;
    private string _offscreenTitle = "";

    public override string Title => "Hoje";

    public ObservableCollection<TodayGoalViewModel> Goals { get; } = [];

    public ObservableCollection<UpcomingReminderViewModel> Upcoming { get; } = [];

    public bool HasUpcoming => Upcoming.Count > 0;

    /// <summary>Lembretes pausados: a tela mostra a faixa "Lembretes pausados" e esconde os próximos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText), nameof(CanPause))]
    public partial bool IsPaused { get; set; }

    public string PauseButtonText => IsPaused ? "Retomar lembretes" : "Pausar lembretes";

    /// <summary>Botão "Pausar lembretes" do cabeçalho: só sem pausa e com a pausa disponível.</summary>
    public bool CanPause => pause is not null && !IsPaused;

    [ObservableProperty] public partial int WeekPercent { get; set; }
    [ObservableProperty] public partial int InProgress { get; set; }
    [ObservableProperty] public partial int Completed { get; set; }
    /// <summary>Tile "Sequência": maior sequência atual entre as metas ativas, com o título da meta.</summary>
    [ObservableProperty] public partial int StreakValue { get; set; }
    [ObservableProperty] public partial string StreakUnit { get; set; } = "dias";
    [ObservableProperty] public partial string? StreakGoal { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    /// <summary>Cronômetro de meta que não está na lista de hoje (ex.: dia inativo): faixa com Parar/Descartar.</summary>
    [ObservableProperty] public partial bool HasOffscreenTimer { get; set; }
    [ObservableProperty] public partial bool IsOffscreenTimerRunning { get; set; }
    [ObservableProperty] public partial string? OffscreenTimerText { get; set; }

    public bool IsEmpty => Goals.Count == 0;
    public string EmptyMessage => "Nenhuma meta para hoje. Crie uma em Metas.";

    public override void Activate() => LoadCommand.Execute(null);

    /// <summary>Arquiva as vencidas e monta o instantâneo. Se houver cargas sobrepostas, vale a última.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        // O tique do cronômetro precisa voltar à thread da interface (bindings do Avalonia).
        _ui ??= SynchronizationContext.Current;
        if (!_subscribed)
        {
            // Assina uma vez (a página vive com o app); pode chegar de qualquer thread, recarrega na da interface.
            _subscribed = true;
            changes.Changed += (_, _) => OnUi(() => LoadCommand.Execute(null));
        }
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
            var shownInCard = Goals.Any(g => g.IsTimerRunning || g.IsTimerStopped);
            _offscreenTimer = shownInCard ? null : snapshot.Timer;
            _offscreenTitle = snapshot.TimerGoal?.Title ?? "meta removida";
            HasOffscreenTimer = _offscreenTimer is not null;
            IsOffscreenTimerRunning = _offscreenTimer is { StoppedAt: null };
            UpdateOffscreenText();
            (WeekPercent, InProgress, Completed) = (snapshot.Week.Percent, snapshot.Week.InProgress, snapshot.Week.Completed);
            if (snapshot.Timer is { StoppedAt: null })
                _ticker = time.CreateTimer(_ => OnUi(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            await LoadStreakAsync(version);
            await LoadUpcomingAsync(version);
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

    /// <summary>Falha aqui só zera o tile: o resto da tela Hoje continua valendo.</summary>
    private async Task LoadStreakAsync(int version)
    {
        StreakHighlight? highlight = null;
        try
        {
            if (reports is not null)
                highlight = await reports.GetTopStreakAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao carregar a sequência: {ex}");
        }
        if (version != _loadVersion)
            return;

        StreakValue = highlight?.Length ?? 0;
        StreakGoal = highlight?.Goal.Title;
        var one = StreakValue == 1;
        StreakUnit = highlight?.Goal.Recurrence == Recurrence.Weekly ? (one ? "semana" : "semanas") : (one ? "dia" : "dias");
    }

    /// <summary>Falha aqui só esconde a seção: o resto da tela Hoje continua valendo.</summary>
    private async Task LoadUpcomingAsync(int version)
    {
        IReadOnlyList<UpcomingReminder> upcoming;
        var paused = false;
        try
        {
            paused = pause is not null && (await pause.GetAsync()).IsPaused;
            upcoming = paused ? [] : await reminders.UpcomingTodayAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao carregar os próximos lembretes: {ex}");
            upcoming = [];
        }
        if (version != _loadVersion)
            return;

        IsPaused = paused;
        Upcoming.Clear();
        foreach (var item in upcoming)
            Upcoming.Add(new UpcomingReminderViewModel(
                TimeZoneInfo.ConvertTime(item.At, time.LocalTimeZone).ToString("HH:mm", CultureInfo.InvariantCulture),
                item.Goal.Title));
        OnPropertyChanged(nameof(HasUpcoming));
    }

    /// <summary>Pausa ou retoma; a recarga chega pelo <see cref="DataChangeNotifier"/>.</summary>
    [RelayCommand(CanExecute = nameof(HasPause))]
    private async Task TogglePauseAsync()
    {
        if (pause is null)
            return;
        ErrorMessage = null;
        try
        {
            if (IsPaused)
                await pause.ResumeAsync();
            else
                await pause.PauseAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao pausar/retomar os lembretes: {ex}");
            ErrorMessage = "Não foi possível alterar a pausa dos lembretes.";
        }
    }

    private bool HasPause() => pause is not null;

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

    [RelayCommand]
    private Task StopOffscreenTimerAsync() => RunCoreAsync(() => timers.StopAsync(), panelItem: null);

    [RelayCommand]
    private Task DiscardOffscreenTimerAsync() => RunCoreAsync(() => timers.DiscardAsync(), panelItem: null);

    /// <summary>
    /// Executa a ação e recarrega. Erro de validação numa ação de painel fica no painel, aberto (sem recarregar);
    /// os demais erros vão para a faixa de aviso, depois da recarga (que limpa <see cref="ErrorMessage"/>).
    /// </summary>
    private Task RunAsync(TodayGoalViewModel? item, Func<Guid, Task> action, bool panel = false) =>
        item is null ? Task.CompletedTask : RunCoreAsync(() => action(item.Goal.Id), panel ? item : null);

    private async Task RunCoreAsync(Func<Task> action, TodayGoalViewModel? panelItem)
    {
        ErrorMessage = null;
        string? error = null;
        try
        {
            await action();
        }
        catch (GoalValidationException ex) when (panelItem is not null)
        {
            panelItem.PanelError = ex.Errors[0].Message;
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
        UpdateOffscreenText();
    }

    private void UpdateOffscreenText() => OffscreenTimerText = _offscreenTimer switch
    {
        null => null,
        { StoppedAt: null } t => $"Cronômetro rodando em \"{_offscreenTitle}\" · {GoalFormatting.Elapsed(time.GetUtcNow() - t.StartedAt)}",
        var t => $"Cronômetro parado em \"{_offscreenTitle}\" · {GoalFormatting.Duration(TimerService.Minutes(t, time.GetUtcNow()))}",
    };

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
