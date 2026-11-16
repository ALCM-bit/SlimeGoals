using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.App.ViewModels;

/// <summary>Uma opção de "Contar numa meta": <c>Id</c> nulo é "Sem meta".</summary>
public sealed record FocusGoalOption(Guid? Id, string Title);

/// <summary>
/// Subaba Pomodoro da aba Tempo (spec Pomodoro §4): o preparo da sessão (sequência de blocos, "Repetir", meta opcional e
/// "Começar"), a sessão rodando (<c>PomodoroViewModel.Running.cs</c>) e a tela de fim (<c>PomodoroViewModel.End.cs</c>).
/// A sugestão (a última sequência começada, ou 25/5 4×) entra na primeira carga e de novo depois de cada sessão;
/// recargas no meio do preparo não mexem no que a pessoa montou.
/// </summary>
public sealed partial class PomodoroViewModel : ObservableObject
{
    private const string LoadError = "Não foi possível carregar o Pomodoro.";
    private static readonly FocusGoalOption NoGoal = new(null, "Sem meta");

    // Limite só para o cast de decimal para int não estourar; os limites de verdade são os do FocusPlan.Validate.
    private const decimal MaxInput = 1_000;

    private readonly FocusService _focus;
    private readonly GoalService _goals;
    private readonly TimeProvider _time;
    private readonly DataChangeNotifier _changes;
    private readonly PageNavigator? _navigator;

    private bool _subscribed;
    private bool _seeded;
    private int _loadVersion;
    private SynchronizationContext? _ui;
    private FocusSession? _session;
    private string? _sessionGoalTitle;

    public PomodoroViewModel(FocusService focus, GoalService goals, TimeProvider time, DataChangeNotifier changes,
        PageNavigator? navigator = null)
    {
        _focus = focus;
        _goals = goals;
        _time = time;
        _changes = changes;
        _navigator = navigator;
        Blocks.CollectionChanged += OnBlocksChanged;
    }

    /// <summary>A sequência, na ordem; o limite de 12 vale para ela, antes da repetição.</summary>
    public ObservableCollection<FocusBlockViewModel> Blocks { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartText), nameof(RepeatUnitText))]
    public partial decimal? RepeatInput { get; set; }

    /// <summary>A palavra depois do campo: "Repetir [1] vez", "Repetir [2] vezes".</summary>
    public string RepeatUnitText => Repeat() == 1 ? "vez" : "vezes";

    /// <summary>"Sem meta" e as metas de tempo ativas hoje.</summary>
    [ObservableProperty] public partial IReadOnlyList<FocusGoalOption> GoalOptions { get; set; } = [NoGoal];

    private FocusGoalOption _selectedGoal = NoGoal;

    /// <summary>A meta escolhida. Ignora null: o ComboBox pode desmarcar ao trocar a lista de opções.</summary>
    public FocusGoalOption SelectedGoal
    {
        get => _selectedGoal;
        set
        {
            if (value is null)
                OnPropertyChanged();
            else
                SetProperty(ref _selectedGoal, value);
        }
    }

    /// <summary>"Começar · 2 h 30": quanto a sessão inteira dura, com a repetição.</summary>
    public string StartText
    {
        get
        {
            // Em long: campos absurdos (antes da validação) não podem estourar a conta.
            var total = Blocks.Sum(b => (long)(b.Minutes() ?? 0)) * Math.Max(0, Repeat());
            return $"Começar · {GoalFormatting.Duration((int)Math.Clamp(total, 0, int.MaxValue))}";
        }
    }

    /// <summary>Por que "Começar" está desligado: um cronômetro rodando ou parado à espera de registro.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string? StartBlockedReason { get; set; }

    [ObservableProperty] public partial string? Error { get; set; }

    /// <summary>Sem sessão e sem resumo na tela.</summary>
    public bool IsPreparing => _session is null && _summary is null;
    public bool IsRunning => _session is { FinishedAt: null };

    /// <summary>A tela de fim: uma sessão terminada à espera de destino, ou o resumo guardado até "Nova sessão".</summary>
    public bool IsFinished => !IsRunning && _summary is not null;

    public bool CanStart => IsPreparing && StartBlockedReason is null;

    /// <summary>Lê a sessão, o motivo de bloqueio e as metas. Se houver cargas sobrepostas, vale a última.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task LoadAsync()
    {
        _ui ??= SynchronizationContext.Current;
        if (!_subscribed)
        {
            // Assina uma vez (a página vive com o app); pode chegar de qualquer thread, recarrega na da interface.
            _subscribed = true;
            _changes.Changed += (_, _) => OnUi(() => LoadCommand.Execute(null));
        }
        var version = ++_loadVersion;
        try
        {
            var session = await _focus.GetAsync();
            var conflict = session is null ? await _focus.TimerConflictAsync() : null;
            var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
            var all = await _goals.ListAsync();
            var goals = all
                .Where(g => g.Measure == Measure.Duration && GoalPeriod.IsShownOn(g, today))
                .Select(g => new FocusGoalOption(g.Id, g.Title))
                .ToList();
            var suggestion = session is null && !_seeded ? await _focus.SuggestedPlanAsync() : null;
            // A sessão terminada sem meta mostra o que está guardado, inclusive com o app reaberto.
            var held = session is { FinishedAt: not null }
                ? (await _focus.ChunksAsync()).Where(c => c.CheckinId is null).ToList()
                : null;
            var suggestions = session is { FinishedAt: null } ? await TaskSuggestionsAsync() : [];
            if (version != _loadVersion)
                return;

            if (Error == LoadError)
                Error = null;
            GoalOptions = [NoGoal, .. goals];
            SelectedGoal = goals.FirstOrDefault(o => o.Id == SelectedGoal.Id) ?? NoGoal;
            SetAssignOptions(goals);
            if (suggestion is not null)
                Seed(suggestion);
            // Depois de uma sessão, o preparo volta com a sugestão nova (a sequência dela) e sem meta.
            if (session is not null)
                _seeded = false;
            // O erro de um toque sobrevive às recargas de fundo (sincronização, a recarga que o próprio toque postou);
            // só some quando a tela muda: a sessão aparece, termina ou some.
            if (IsRunning != session is { FinishedAt: null } || (_session is null) != (session is null))
                Error = null;
            _sessionGoalTitle = session?.GoalId is { } goalId ? all.FirstOrDefault(g => g.Id == goalId)?.Title : null;
            HoldSummary(session, held);
            var draft = TaskDraft();
            SetSession(session);
            RestoreTaskDraft(draft);
            TaskSuggestions = suggestions;
            StartBlockedReason = conflict;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao carregar o Pomodoro: {TraceText.Of(ex)}");
            if (version == _loadVersion)
                Error = LoadError;
        }
    }

    [RelayCommand]
    private void AddFocus() => Blocks.Add(new FocusBlockViewModel(DefaultBlock(FocusBlockKind.Focus)));

    [RelayCommand]
    private void AddBreak() => Blocks.Add(new FocusBlockViewModel(DefaultBlock(FocusBlockKind.Break)));

    [RelayCommand]
    private void RemoveBlock(FocusBlockViewModel? block)
    {
        if (block is not null)
            Blocks.Remove(block);
    }

    /// <summary>Abre o editor de um bloco; só um fica aberto.</summary>
    [RelayCommand]
    private void EditBlock(FocusBlockViewModel? block)
    {
        if (block is null)
            return;
        CloseBlockEditor();
        block.IsEditing = true;
    }

    /// <summary>Fecha o editor aberto.</summary>
    [RelayCommand]
    private void CloseBlockEditor()
    {
        foreach (var open in Blocks.Where(b => b.IsEditing))
            open.IsEditing = false;
    }

    /// <summary>
    /// Começa a sessão com a sequência montada. Bloco com os campos vazios vale 0 min, para o serviço dar a mensagem do
    /// limite; os erros ficam em <see cref="Error"/> e nada é gravado.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        Error = null;
        var plan = new FocusPlan(Blocks.Select(b => new FocusBlock(b.Kind, b.Minutes() ?? 0)).ToList(), Repeat());
        try
        {
            await _focus.StartAsync(plan, SelectedGoal.Id);
        }
        catch (GoalValidationException ex)
        {
            Error = ex.Errors[0].Message;
            return;
        }
        catch (GoalStateException ex)
        {
            Error = ex.Message;
        }
        catch (GoalNotFoundException)
        {
            Error = "Esta meta não existe mais.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao começar a sessão de Pomodoro: {TraceText.Of(ex)}");
            Error = "Não foi possível começar a sessão.";
            return;
        }
        // Começou, ou outro relógio apareceu (cronômetro, sessão de outro processo, meta que mudou): a recarga mostra.
        var error = Error;
        CloseBlockEditor();
        await LoadAsync();
        Error ??= error;
    }

    [RelayCommand]
    private void GoToToday() => _navigator?.Show<TodayPageViewModel>();

    private static FocusBlock DefaultBlock(FocusBlockKind kind) => FocusPlan.Default.Blocks.First(b => b.Kind == kind);

    private int Repeat() =>
        RepeatInput is { } r ? (int)Math.Round(Math.Clamp(r, -MaxInput, MaxInput), MidpointRounding.AwayFromZero) : 0;

    private void Seed(FocusPlan plan)
    {
        foreach (var block in Blocks)
            block.PropertyChanged -= OnBlockChanged;
        Blocks.Clear();
        foreach (var block in plan.Blocks)
            Blocks.Add(new FocusBlockViewModel(block));
        RepeatInput = plan.Repeat;
        SelectedGoal = NoGoal;
        _seeded = true;
    }

    private void SetSession(FocusSession? session)
    {
        _session = session;
        StopTicker();
        if (session is { FinishedAt: null, PausedAt: null })
            _ticker = _time.CreateTimer(_ => OnUi(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        if (!IsRunning)
            (IsReviewing, IsConfirmingEnd, IsEditingTask, IsSwitchingTask) = (false, false, false, false);
        if (!IsAwaitingDestination)
            IsConfirmingDiscard = false;
        Dots = BuildDots();
        RaiseStates();
    }

    /// <summary>Avisa a tela de tudo que muda com a sessão ou o resumo.</summary>
    private void RaiseStates()
    {
        OnPropertyChanged(nameof(IsPreparing));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(RoundText));
        OnPropertyChanged(nameof(GoalText));
        RaiseSummary();
        RaiseClock();
        StartCommand.NotifyCanExecuteChanged();
    }

    private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var block in e.OldItems?.OfType<FocusBlockViewModel>() ?? [])
            block.PropertyChanged -= OnBlockChanged;
        foreach (var block in e.NewItems?.OfType<FocusBlockViewModel>() ?? [])
            block.PropertyChanged += OnBlockChanged;
        OnPropertyChanged(nameof(StartText));
    }

    private void OnBlockChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FocusBlockViewModel.HoursInput) or nameof(FocusBlockViewModel.MinutesInput))
            OnPropertyChanged(nameof(StartText));
    }

    private void OnUi(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }
}
