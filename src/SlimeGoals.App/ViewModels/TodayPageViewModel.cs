using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Account;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Validation;
using SlimeGoals.Sync;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Tela Hoje (spec §7): resumo da semana, cartões das metas de hoje com controles por medida
/// e os próximos lembretes do dia. Cada ação chama um serviço do Core e recarrega o instantâneo;
/// escritas de fora da tela (notificação, virada do dia) chegam pelo <see cref="DataChangeNotifier"/>.
/// </summary>
public sealed partial class TodayPageViewModel(ProgressService progress, CheckinService checkins, TimerService timers,
    GoalService goals, TimeProvider time, ReminderService reminders, DataChangeNotifier changes,
    ReminderPause? pause = null, ReportService? reports = null, PlatformCapabilities? capabilities = null,
    IReminderPermissions? permissions = null, FocusService? focus = null, PageNavigator? navigator = null,
    ISyncAccount? account = null, TagService? tags = null) : PageViewModel
{
    private readonly bool _reminders = (capabilities ?? PlatformCapabilities.Desktop).Reminders;

    private bool _subscribed;
    private int _loadVersion;
    private int _syncWarningVersion;
    private ITimer? _ticker;
    private SynchronizationContext? _ui;
    private ActiveTimer? _offscreenTimer;
    private string _offscreenTitle = "";

    public override string Title => "Hoje";

    public override string IconKey => "NavToday";

    public ObservableCollection<TodayGoalViewModel> Goals { get; } = [];

    /// <summary>Filtro por tags: muda só quais cartões aparecem; hero, contadores e slime contam <see cref="Goals"/> inteiro.</summary>
    public TodayTagFilterViewModel Filter { get; } = new();

    /// <summary>Lembretes pausados: a tela mostra a faixa "Lembretes pausados" e esconde os próximos.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseButtonText), nameof(CanPause))]
    public partial bool IsPaused { get; set; }

    public string PauseButtonText => IsPaused ? "Retomar lembretes" : "Pausar lembretes";

    /// <summary>Botão "Pausar lembretes" do cabeçalho: só sem pausa e com a pausa disponível.</summary>
    public bool CanPause => _reminders && pause is not null && !IsPaused;

    /// <summary>A hora do próximo lembrete nos cartões e a pausa só existem onde a plataforma tem lembretes.</summary>
    public bool ShowReminders => _reminders;

    /// <summary>Aviso do topo quando faltam permissões dos lembretes (Android); nulo se tudo certo ou sem a porta.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPermissionWarning))]
    public partial string? PermissionWarning { get; set; }

    public bool HasPermissionWarning => PermissionWarning is not null;

    /// <summary>Botão "Permitir": pede as notificações; se elas já estão liberadas, os alarmes exatos.</summary>
    [RelayCommand]
    private void RequestPermission()
    {
        if (permissions is null)
            return;
        if (!permissions.Get().Notifications)
            permissions.RequestNotifications();
        else
            permissions.RequestExactAlarms();
    }

    private void RefreshPermissions()
    {
        var status = _reminders ? permissions?.Get() : null;
        PermissionWarning = status switch
        {
            null => null,
            { Notifications: false } => "Os lembretes estão desligados: permita as notificações do SlimeGoals.",
            { ExactAlarms: false } => "Os lembretes podem atrasar alguns minutos: permita alarmes exatos.",
            _ => null,
        };
    }

    [ObservableProperty] public partial int WeekPercent { get; set; }
    [ObservableProperty] public partial int InProgress { get; set; }
    [ObservableProperty] public partial int Completed { get; set; }
    /// <summary>Tile "Sequência": maior sequência atual entre as metas ativas, com o título da meta.</summary>
    [ObservableProperty] public partial int StreakValue { get; set; }
    [ObservableProperty] public partial string StreakUnit { get; set; } = "dias";
    [ObservableProperty] public partial string? StreakGoal { get; set; }

    /// <summary>"Esta semana" do painel lateral do desktop: metas com progresso por dia, como nos Relatórios.</summary>
    public ObservableCollection<TodayWeekBarViewModel> WeekBars { get; } = [];
    [ObservableProperty] public partial bool HasWeekBars { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    /// <summary>Cronômetro de meta que não está na lista de hoje (ex.: dia inativo): faixa com Parar/Descartar.</summary>
    [ObservableProperty] public partial bool HasOffscreenTimer { get; set; }
    [ObservableProperty] public partial bool IsOffscreenTimerRunning { get; set; }
    [ObservableProperty] public partial string? OffscreenTimerText { get; set; }

    /// <summary>
    /// Faixa da sessão de Pomodoro: enquanto ela existe (rodando, pausada, à espera do toque ou do destino do tempo), o ▶
    /// dos cartões fica desligado e a faixa leva à aba Tempo (spec Pomodoro §4.1).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFocusBand))]
    public partial string? FocusBandText { get; set; }

    public bool HasFocusBand => FocusBandText is not null;

    /// <summary>Aviso de sincronização parada (<see cref="SyncStaleWarning"/>); nulo se tudo certo ou sem conta.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncWarning))]
    public partial string? SyncWarning { get; set; }

    public bool HasSyncWarning => SyncWarning is not null;

    /// <summary>Botão "Abrir" do aviso: vai para as configurações, no grupo de sincronização.</summary>
    [RelayCommand]
    private void OpenSync() => navigator?.ShowSettings(SettingsSection.Sync);

    private async Task RefreshSyncWarningAsync()
    {
        if (account is null)
            return;
        // Uma leitura mais antiga que termina depois de uma mais nova (sem token, ela nem pode ser cancelada) não vale.
        var version = ++_syncWarningVersion;
        string? warning;
        try
        {
            var status = await account.GetStatusAsync();
            warning = SyncStaleWarning.TextFor(status, time.GetUtcNow());
        }
        catch (Exception)
        {
            // O aviso é só um complemento: se a conta não responde, a tela Hoje segue sem ele. Nada escapa daqui, para
            // a carga da tela e o disparo sem espera (StatusChanged) não falharem por causa dele.
            warning = null;
        }
        if (version == _syncWarningVersion)
            SyncWarning = warning;
    }

    [RelayCommand]
    private void OpenTimePage() => navigator?.Show<TimePageViewModel>();

    /// <summary>Pose e frase do slime, conforme o progresso de hoje e a pausa dos lembretes.</summary>
    [ObservableProperty] public partial MascotState Mascot { get; set; } = MascotState.For(0, 0, false);

    /// <summary>Slime e balão só aparecem depois da primeira carga bem-sucedida (evita piscar a pose errada).</summary>
    [ObservableProperty] public partial bool HasMascot { get; set; }

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
            if (account is not null)
                account.StatusChanged += (_, _) => OnUi(() => _ = RefreshSyncWarningAsync());
        }
        var version = ++_loadVersion;
        ErrorMessage = null;
        RefreshPermissions();
        try
        {
            await goals.ArchiveExpiredAsync();
            var snapshot = await progress.GetTodayAsync();
            var todayCheckins = await checkins.TodayAsync();
            var session = focus is null ? null : await focus.GetAsync();
            var tagColors = await LoadTagColorsAsync();
            if (version != _loadVersion)
                return;

            StopTicker();
            // O texto do campo da tarefa sobrevive à recarga (sincronização, outro processo) enquanto for o mesmo cronômetro.
            var draft = Goals.FirstOrDefault(g => g.HasTaskDraft);
            // A lista aberta e o editor de um trecho também sobrevivem à recarga, se o registro ainda existir.
            var openChunks = Goals.Where(g => g.IsChunksOpen).Select(g => g.Goal.Id).ToHashSet();
            var chunkDraft = Goals.SelectMany(g => g.Chunks).FirstOrDefault(c => c.IsEditing);
            Goals.Clear();
            foreach (var item in snapshot.Goals)
            {
                var card = new TodayGoalViewModel(item, snapshot.Timer, time,
                    todayCheckins.Where(c => c.GoalId == item.Goal.Id).ToList(), tagColors)
                {
                    CanStartTimer = snapshot.Timer is null && session is null,
                };
                card.IsChunksOpen = card.HasChunks && openChunks.Contains(card.Goal.Id);
                if (chunkDraft is not null && card.Chunks.FirstOrDefault(c => c.CheckinId == chunkDraft.CheckinId) is { } same)
                {
                    same.OpenEditor();
                    (same.HoursInput, same.MinutesInput, same.SecondsInput, same.NoteInput, same.Error) =
                        (chunkDraft.HoursInput, chunkDraft.MinutesInput, chunkDraft.SecondsInput, chunkDraft.NoteInput,
                            chunkDraft.Error);
                }
                Goals.Add(card);
            }
            Filter.Refresh(Goals);
            var running = Goals.FirstOrDefault(g => g.IsTimerRunning);
            if (draft is not null && running is not null && running.Goal.Id == draft.Goal.Id
                && (draft.IsSwitchingTask || draft.IsEditingTask
                    || TodayGoalViewModel.NormalizeTask(draft.TaskInput) != running.TimerTask))
            {
                running.TaskInput = draft.TaskInput;
                running.IsSwitchingTask = draft.IsSwitchingTask;
                // Com nome gravado, o rascunho só fica à vista no modo de edição.
                running.IsEditingTask = !draft.IsSwitchingTask && running.TimerTask is not null;
            }
            var shownInCard = Goals.Any(g => g.IsTimerRunning || g.IsTimerStopped);
            _offscreenTimer = shownInCard ? null : snapshot.Timer;
            _offscreenTitle = snapshot.TimerGoal?.Title ?? "meta removida";
            HasOffscreenTimer = _offscreenTimer is not null;
            FocusBandText = session switch
            {
                null => null,
                // Terminada sem meta: não anda mais, só espera o destino dos trechos guardados.
                { FinishedAt: not null } => "Pomodoro concluído. Escolha o destino do tempo na aba Tempo.",
                _ => "Sessão de Pomodoro em andamento.",
            };
            IsOffscreenTimerRunning = _offscreenTimer is { StoppedAt: null };
            UpdateOffscreenText();
            (WeekPercent, InProgress, Completed) = (snapshot.Week.Percent, snapshot.Week.InProgress, snapshot.Week.Completed);
            if (snapshot.Timer is { StoppedAt: null })
                _ticker = time.CreateTimer(_ => OnUi(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            if (running is not null)
                await LoadTaskSuggestionsAsync(running, version);
            await LoadStreakAsync(version);
            await LoadWeekBarsAsync(version);
            await LoadUpcomingAsync(version);
            if (version == _loadVersion)
            {
                Mascot = MascotState.For(Goals.Count, Goals.Count(g => g.IsMet), IsPaused);
                HasMascot = true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Não foi possível carregar o dia.";
        }
        finally
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
        // Por último: o aviso é só um complemento e não atrasa os cartões das metas.
        await RefreshSyncWarningAsync();
    }

    /// <summary>Sem as cores, as tags dos cartões ficam em Purple: o filtro não derruba a tela.</summary>
    private async Task<IReadOnlyDictionary<string, GoalColor>?> LoadTagColorsAsync()
    {
        if (tags is null)
            return null;
        try
        {
            return await tags.ColorsAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Trace.TraceWarning($"Cores das tags indisponíveis: {TraceText.Types(e)}");
            return null;
        }
    }

    /// <summary>Sem sugestões se a leitura falhar: o campo continua aceitando texto livre.</summary>
    private async Task LoadTaskSuggestionsAsync(TodayGoalViewModel running, int version)
    {
        try
        {
            var suggestions = await timers.TaskSuggestionsAsync(running.Goal.Id);
            if (version == _loadVersion)
                running.TaskSuggestions = suggestions;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Trace.TraceWarning($"Sugestões de tarefa indisponíveis: {TraceText.Types(e)}");
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
            Trace.TraceError($"Falha ao carregar a sequência: {TraceText.Of(ex)}");
        }
        if (version != _loadVersion)
            return;

        StreakValue = highlight?.Length ?? 0;
        StreakGoal = highlight?.Goal.Title;
        var one = StreakValue == 1;
        StreakUnit = highlight?.Goal.Recurrence == Recurrence.Weekly ? (one ? "semana" : "semanas") : (one ? "dia" : "dias");
    }

    /// <summary>Altura da maior barra da semana, em px.</summary>
    private const double WeekBarMaxHeight = 80;

    /// <summary>Falha aqui só tira o gráfico da semana: o resto da tela Hoje continua valendo.</summary>
    private async Task LoadWeekBarsAsync(int version)
    {
        WeekReport? report = null;
        try
        {
            if (reports is not null)
                report = await reports.GetWeekAsync(DateOnly.FromDateTime(time.GetLocalNow().DateTime));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao carregar a semana: {TraceText.Of(ex)}");
        }
        if (version != _loadVersion)
            return;

        WeekBars.Clear();
        if (report is not null)
        {
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var max = Math.Max(1, report.Days.Max(d => d.Goals.Count));
            foreach (var day in report.Days)
                WeekBars.Add(new TodayWeekBarViewModel(ReportsPageViewModel.WeekLabels[((int)day.Day.DayOfWeek + 6) % 7],
                    Math.Max(4, (double)day.Goals.Count / max * WeekBarMaxHeight), day.Day == today));
        }
        HasWeekBars = WeekBars.Count > 0;
    }

    /// <summary>Hora do próximo lembrete em cada cartão. Falha aqui só tira as horas: o resto da tela Hoje continua valendo.</summary>
    private async Task LoadUpcomingAsync(int version)
    {
        if (!_reminders)
            return;
        IReadOnlyList<UpcomingReminder> upcoming;
        var paused = false;
        try
        {
            paused = pause is not null && (await pause.GetAsync()).IsPaused;
            upcoming = paused ? [] : await reminders.UpcomingTodayAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao carregar os próximos lembretes: {TraceText.Of(ex)}");
            upcoming = [];
        }
        if (version != _loadVersion)
            return;

        IsPaused = paused;
        var next = upcoming.ToDictionary(u => u.Goal.Id,
            u => TimeZoneInfo.ConvertTime(u.At, time.LocalTimeZone).ToString("HH:mm", CultureInfo.InvariantCulture));
        foreach (var card in Goals)
            card.NextReminder = next.GetValueOrDefault(card.Goal.Id);
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
            Trace.TraceError($"Falha ao pausar/retomar os lembretes: {TraceText.Of(ex)}");
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
    private Task StopTimerAsync(TodayGoalViewModel? item) => RunAsync(item, async _ =>
    {
        // ■ tocado com o campo em edição: o nome digitado vale para o trecho que para (e vira a nota do painel).
        await SaveTypedTaskAsync(item!);
        await timers.StopAsync();
    });

    [RelayCommand]
    private Task RegisterTimerAsync(TodayGoalViewModel? item)
    {
        if (item is null)
            return Task.CompletedTask;
        if (item.StoppedAmount() is not { } value)
        {
            item.PanelError = "Informe um valor.";
            return Task.CompletedTask;
        }
        return RunAsync(item, _ => timers.RegisterAsync(value, item.PanelNote), panel: true);
    }

    /// <summary>✓, Enter ou sugestão escolhida: grava o nome digitado e fecha a edição. Sem mudança, só fecha.</summary>
    [RelayCommand]
    private Task SaveTaskAsync(TodayGoalViewModel? item)
    {
        if (item is not { IsTimerRunning: true, IsSwitchingTask: false })
            return Task.CompletedTask;
        if (TodayGoalViewModel.NormalizeTask(item.TaskInput) == item.TimerTask)
        {
            item.IsEditingTask = false;
            item.TaskInput = item.TimerTask;
            return Task.CompletedTask;
        }
        // Fecha só se gravou: se falhar, o texto digitado continua no campo depois da recarga.
        return RunAsync(item, async _ =>
        {
            await timers.SetTaskAsync(item.TaskInput);
            item.IsEditingTask = false;
        });
    }

    /// <summary>✎: o nome gravado volta a ser um campo, preenchido com ele.</summary>
    [RelayCommand]
    private static void BeginEditTask(TodayGoalViewModel? item)
    {
        if (item is not { ShowsTaskLabel: true })
            return;
        item.TaskInput = item.TimerTask;
        item.IsEditingTask = true;
    }

    /// <summary>✕ ou Esc: desiste da edição e volta ao nome gravado.</summary>
    [RelayCommand]
    private static void CancelEditTask(TodayGoalViewModel? item)
    {
        if (item is not { IsEditingTask: true })
            return;
        item.IsEditingTask = false;
        item.TaskInput = item.TimerTask;
    }

    [RelayCommand]
    private Task BeginSwitchTaskAsync(TodayGoalViewModel? item)
    {
        if (item is not { IsTimerRunning: true })
            return Task.CompletedTask;
        // ⇄ tocado com o campo em edição: o nome digitado é o da tarefa atual, e grava antes de abrir a troca.
        var pending = item is { IsSwitchingTask: false } && TodayGoalViewModel.NormalizeTask(item.TaskInput) != item.TimerTask;
        var typed = item.TaskInput;
        item.TaskInput = null;
        item.IsSwitchingTask = true;
        return pending ? RunAsync(item, _ => timers.SetTaskAsync(typed)) : Task.CompletedTask;
    }

    /// <summary>Grava o nome digitado e ainda não gravado, se houver (fora do modo de troca).</summary>
    private Task SaveTypedTaskAsync(TodayGoalViewModel item) =>
        item is { IsTimerRunning: true, IsSwitchingTask: false } && TodayGoalViewModel.NormalizeTask(item.TaskInput) != item.TimerTask
            ? timers.SetTaskAsync(item.TaskInput)
            : Task.CompletedTask;

    [RelayCommand]
    private static void CancelSwitchTask(TodayGoalViewModel? item)
    {
        if (item is null)
            return;
        item.IsSwitchingTask = false;
        item.TaskInput = item.TimerTask;
    }

    /// <summary>Registra o trecho da tarefa atual e segue com a nova (ver <see cref="TimerService.SwitchTaskAsync"/>).</summary>
    [RelayCommand]
    private Task ConfirmSwitchTaskAsync(TodayGoalViewModel? item)
    {
        if (item is not { IsTimerRunning: true, IsSwitchingTask: true })
            return Task.CompletedTask;
        return RunAsync(item, async _ =>
        {
            await timers.SwitchTaskAsync(item.TaskInput);
            // Trocou: o modo de troca acaba, e o recarregamento não o devolve como rascunho.
            item.IsSwitchingTask = false;
            item.TaskInput = TodayGoalViewModel.NormalizeTask(item.TaskInput);
        });
    }

    // ---- Trechos de hoje (specs/Tarefas_no_cronometro_v1.md §7.2) ----

    [RelayCommand]
    private static void ToggleChunks(TodayGoalViewModel? item)
    {
        if (item is { HasChunks: true })
            item.IsChunksOpen = !item.IsChunksOpen;
    }

    /// <summary>Abre o editor do trecho; só um editor fica aberto na página.</summary>
    [RelayCommand]
    private void EditChunk(TodayChunkViewModel? chunk)
    {
        if (chunk is not { CanEdit: true })
            return;
        foreach (var other in Goals.SelectMany(g => g.Chunks).Where(c => c != chunk && c.IsEditing))
            other.CloseEditor();
        chunk.OpenEditor();
    }

    [RelayCommand]
    private static void CancelChunkEdit(TodayChunkViewModel? chunk) => chunk?.CloseEditor();

    [RelayCommand]
    private Task SaveChunkAsync(TodayChunkViewModel? chunk)
    {
        if (chunk is not { IsEditing: true, CheckinId: { } id })
            return Task.CompletedTask;
        if (chunk.Seconds() is not { } seconds)
        {
            chunk.Error = "Informe um valor.";
            return Task.CompletedTask;
        }
        return RunChunkAsync(chunk, () => checkins.ReplaceAsync(chunk.GoalId, id, seconds, chunk.NoteInput));
    }

    [RelayCommand]
    private Task DeleteChunkAsync(TodayChunkViewModel? chunk) =>
        chunk is { IsEditing: true, CheckinId: { } id }
            ? RunChunkAsync(chunk, () => checkins.DeleteAsync(chunk.GoalId, id))
            : Task.CompletedTask;

    /// <summary>
    /// Os erros ficam no editor, que continua aberto; só a meta excluída vai para a faixa da página. Depois de gravar,
    /// fecha o editor antes de recarregar, para a recarga não reabri-lo.
    /// </summary>
    private async Task RunChunkAsync(TodayChunkViewModel chunk, Func<Task> action)
    {
        ErrorMessage = null;
        chunk.Error = null;
        try
        {
            await action();
        }
        catch (GoalValidationException ex)
        {
            chunk.Error = ex.Errors[0].Message;
            return;
        }
        catch (GoalStateException ex)
        {
            chunk.Error = ex.Message;
            return;
        }
        catch (GoalNotFoundException)
        {
            chunk.CloseEditor();
            await LoadCommand.ExecuteAsync(null);
            ErrorMessage ??= "Esta meta não existe mais.";
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao ajustar um trecho de hoje: {TraceText.Of(ex)}");
            chunk.Error = "Não foi possível salvar.";
            return;
        }
        chunk.CloseEditor();
        await LoadCommand.ExecuteAsync(null);
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
        var t => $"Cronômetro parado em \"{_offscreenTitle}\" · {GoalFormatting.Duration(TimerService.Seconds(t, time.GetUtcNow()))}",
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

/// <summary>Barra de um dia em "Esta semana": rótulo ("Seg"), altura em px e se é hoje.</summary>
public sealed record TodayWeekBarViewModel(string Label, double BarHeight, bool IsToday);
