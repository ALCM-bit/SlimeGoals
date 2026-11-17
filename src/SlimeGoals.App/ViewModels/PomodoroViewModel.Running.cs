using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Goals;
using SlimeGoals.Core;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// A sessão rodando (spec Pomodoro §4.2 e §4.3): as bolinhas da rodada, a fase, o tempo que falta, o extra à espera do
/// toque, a subatividade (como no cartão do cronômetro, spec das tarefas §7.1), os controles e a conferência do foco
/// esquecido. O relógio de 1 s só anda com a sessão rodando e sem pausa; os instantes vêm todos da sessão guardada.
/// </summary>
public sealed partial class PomodoroViewModel
{
    private const string ReviewRequired = "Confira o tempo do último trecho antes de seguir.";
    private const string NoName = "Sem nome";

    // Limite só para o cast de decimal para int não estourar; o de verdade é o do registro (24 h), no serviço.
    private const decimal MaxReviewInput = 1_000_000;

    private ITimer? _ticker;
    private PendingCut _pendingCut;
    private bool _acting;

    /// <summary>O que fecha o foco aberto: o toque no fim do bloco e o ⏭ avançam; o ✕ encerra.</summary>
    private enum PendingCut { Advance, End }

    /// <summary>Os blocos da rodada atual.</summary>
    [ObservableProperty] public partial IReadOnlyList<FocusDotViewModel> Dots { get; set; } = [];

    private FocusSession? Live => _session is { FinishedAt: null } s ? s : null;

    private DateTimeOffset Now => _time.GetUtcNow();

    public bool IsPaused => Live is { PausedAt: not null };

    /// <summary>O bloco acabou e a sessão espera o toque, contando o extra.</summary>
    public bool IsWaiting => Live is { } s && FocusClock.IsWaiting(s, Now);

    private bool IsFocusBlock => Live is { } s && s.Block.Kind == FocusBlockKind.Focus;

    /// <summary>O bloco atual é uma pausa: a tela troca o roxo do foco pelo verde.</summary>
    public bool IsBreak => Live is { } s && s.Block.Kind == FocusBlockKind.Break;

    /// <summary>Há um bloco depois deste. No último, o toque do fim já é o "Encerrar", e a tela não repete o botão.</summary>
    public bool HasNextBlock => Live is { IsLastStep: false };

    /// <summary>"Foco · 1 de 2", "Pausa", "Pausado", "Foco concluído" ou "Pausa concluída".</summary>
    public string? PhaseText => Live switch
    {
        null => null,
        { PausedAt: not null } => "Pausado",
        var s when FocusClock.IsWaiting(s, Now) =>
            s.Block.Kind == FocusBlockKind.Focus ? "Foco concluído" : "Pausa concluída",
        { Block.Kind: FocusBlockKind.Focus } s =>
            $"Foco · {s.Plan.FocusNumberInRound(s.Step)} de {s.Plan.FocusCountInRound}",
        _ => "Pausa",
    };

    /// <summary>"Rodada 1 de 4"; nulo com "Repetir" 1.</summary>
    public string? RoundText => Live is { Plan.Repeat: > 1 } s ? $"Rodada {s.Plan.RoundOf(s.Step)} de {s.Plan.Repeat}" : null;

    /// <summary>Os minutos que faltam, todos ("75" num bloco de 1 h 15), com pelo menos dois dígitos.</summary>
    public string? MinutesText => RemainingSeconds() is { } total
        ? (total / 60).ToString("00", CultureInfo.InvariantCulture)
        : null;

    /// <summary>Os segundos que faltam, com dois dígitos.</summary>
    public string? SecondsText => RemainingSeconds() is { } total
        ? (total % 60).ToString("00", CultureInfo.InvariantCulture)
        : null;

    /// <summary>"+2 min": o extra desde o fim do bloco, enquanto a sessão espera o toque.</summary>
    public string? OvertimeText => Live is { } s && FocusClock.IsWaiting(s, Now)
        ? $"+{GoalFormatting.Duration((int)FocusClock.Overtime(s, Now).TotalMinutes)}"
        : null;

    /// <summary>A meta da sessão rodando, ou a do resumo na tela de fim.</summary>
    public string? GoalText => IsRunning ? _sessionGoalTitle : _summary is not null ? _summaryGoal : null;

    /// <summary>O botão do toque: "Começar pausa" ou "Voltar ao foco" conforme o próximo bloco, e "Encerrar" no último.</summary>
    public string? NextActionText => Live switch
    {
        null => null,
        { IsLastStep: true } => "Encerrar",
        var s => s.Plan.BlockAt(s.Step + 1).Kind == FocusBlockKind.Break ? "Começar pausa" : "Voltar ao foco",
    };

    // ---- Confirmação do ✕ e conferência do foco esquecido (§4.3) ----

    [ObservableProperty] public partial bool IsConfirmingEnd { get; set; }

    /// <summary>A conferência do trecho aberto: com 30 min ou mais de extra num foco, ninguém fecha o foco sem ela.</summary>
    [ObservableProperty] public partial bool IsReviewing { get; set; }

    [ObservableProperty] public partial decimal? ReviewHoursInput { get; set; }

    [ObservableProperty] public partial decimal? ReviewMinutesInput { get; set; }

    // ---- Subatividade (spec das tarefas §7.1) ----

    private string? SessionTask => Live?.Task;

    [ObservableProperty] public partial string? TaskInput { get; set; }

    /// <summary>A pessoa tocou ⇄: o campo volta vazio, com as sugestões, e grava o trecho com o nome atual ao trocar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTaskLabel), nameof(ShowsTaskField), nameof(ShowsConfirmTask), nameof(IsTaskFieldOpen))]
    public partial bool IsSwitchingTask { get; set; }

    /// <summary>A pessoa tocou ✎ para mudar o nome gravado: o campo volta, com ✓ e ✕.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTaskLabel), nameof(ShowsTaskField), nameof(ShowsConfirmTask), nameof(IsTaskFieldOpen))]
    public partial bool IsEditingTask { get; set; }

    /// <summary>Notas recentes da meta da sessão, ou de todas as metas na sessão sem meta.</summary>
    [ObservableProperty] public partial IReadOnlyList<string> TaskSuggestions { get; set; } = [];

    /// <summary>O nome gravado aparece como texto: é a confirmação de que ele está valendo. Só no foco.</summary>
    public bool ShowsTaskLabel => IsFocusBlock && SessionTask is not null && !IsEditingTask && !IsSwitchingTask;
    public bool ShowsTaskField => IsFocusBlock && !ShowsTaskLabel;
    public bool ShowsConfirmTask => ShowsTaskField && !IsSwitchingTask;

    /// <summary>
    /// O campo está aberto por um ✎ ou ⇄ e à vista: a tela dá a altura a ele (relógio numa linha, controles fora). Só no
    /// foco; fora dele, os controles nunca somem.
    /// </summary>
    public bool IsTaskFieldOpen => ShowsTaskField && (IsEditingTask || IsSwitchingTask);
    public string TaskLabel => $"▸ {SessionTask}";

    /// <summary>O ⇄ não leva conferência: esperando o toque (com extra), ele some até o próximo foco.</summary>
    public bool CanSwitchTask => IsFocusBlock && !IsWaiting;

    /// <summary>
    /// Nenhum toque que muda a sessão está gravando. O serviço relê a sessão a cada chamada, então "Começar pausa" e ⏭
    /// tocados antes da recarga avançariam duas vezes: um toque por vez (⏸, ⏭, o toque do fim, ✕, a conferência e o ⇄).
    /// </summary>
    public bool CanAct => !_acting;

    /// <summary>Pausar e retomar; esperando o toque, não há relógio a pausar.</summary>
    public bool CanPauseResume => IsRunning && !IsWaiting && CanAct;

    /// <summary>"Trocar" do ⇄: como o ⇄, e sem outro toque gravando.</summary>
    private bool CanConfirmSwitch => CanSwitchTask && CanAct;

    [RelayCommand(CanExecute = nameof(CanPauseResume))]
    private Task PauseResumeAsync() =>
        Live is null
            ? Task.CompletedTask
            : ActAsync(() => RunAsync(IsPaused ? () => _focus.ResumeAsync() : () => _focus.PauseAsync()));

    /// <summary>
    /// ⏭: fecha o bloco agora e começa o próximo. No último, encerraria a sessão: pede a mesma confirmação do ✕, para um
    /// toque solto não terminar a sessão de vez.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task SkipAsync()
    {
        if (Live is { IsLastStep: true })
        {
            AskEnd();
            return Task.CompletedTask;
        }
        return ActAsync(() => CutAsync(PendingCut.Advance));
    }

    /// <summary>O toque no fim do bloco: "Começar pausa", "Voltar ao foco" ou "Encerrar".</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task AdvanceAsync() => ActAsync(() => CutAsync(PendingCut.Advance));

    [RelayCommand]
    private void AskEnd()
    {
        if (IsRunning)
            IsConfirmingEnd = true;
    }

    [RelayCommand]
    private void CancelEnd() => IsConfirmingEnd = false;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ConfirmEndAsync()
    {
        if (!CanAct)
            return Task.CompletedTask;
        IsConfirmingEnd = false;
        return ActAsync(() => CutAsync(PendingCut.End));
    }

    /// <summary>Grava o trecho conferido e segue com o que a pessoa tinha pedido (avançar ou encerrar).</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ConfirmReviewAsync()
    {
        if (!IsReviewing || !CanAct)
            return Task.CompletedTask;
        if (ReviewHoursInput is null && ReviewMinutesInput is null)
        {
            Error = "Informe o tempo.";
            return Task.CompletedTask;
        }
        var minutes = (int)Math.Round(
            Math.Clamp((ReviewHoursInput ?? 0) * 60 + (ReviewMinutesInput ?? 0), -MaxReviewInput, MaxReviewInput),
            MidpointRounding.AwayFromZero);
        return ActAsync(() => RunCutAsync(_pendingCut, minutes));
    }

    [RelayCommand]
    private void CancelReview()
    {
        IsReviewing = false;
        Error = null;
    }

    /// <summary>✓, Enter ou sugestão escolhida: grava o nome digitado e fecha a edição. Sem mudança, só fecha.</summary>
    [RelayCommand]
    private Task SaveTaskAsync()
    {
        if (!IsRunning || IsSwitchingTask)
            return Task.CompletedTask;
        if (TodayGoalViewModel.NormalizeTask(TaskInput) == SessionTask)
        {
            IsEditingTask = false;
            TaskInput = SessionTask;
            return Task.CompletedTask;
        }
        // Fecha só se gravou: se falhar, o texto digitado continua no campo depois da recarga.
        return RunAsync(async () =>
        {
            await _focus.SetTaskAsync(TaskInput);
            IsEditingTask = false;
        });
    }

    /// <summary>✎: o nome gravado volta a ser um campo, preenchido com ele.</summary>
    [RelayCommand]
    private void BeginEditTask()
    {
        if (!ShowsTaskLabel)
            return;
        TaskInput = SessionTask;
        IsEditingTask = true;
    }

    /// <summary>✕ ou Esc: desiste da edição e volta ao nome gravado.</summary>
    [RelayCommand]
    private void CancelEditTask()
    {
        if (!IsEditingTask)
            return;
        IsEditingTask = false;
        TaskInput = SessionTask;
    }

    [RelayCommand(CanExecute = nameof(CanSwitchTask))]
    private Task BeginSwitchTaskAsync()
    {
        if (!CanSwitchTask)
            return Task.CompletedTask;
        // ⇄ tocado com o campo em edição: o nome digitado é o da subatividade atual, e grava antes de abrir a troca.
        var pending = !IsSwitchingTask && TodayGoalViewModel.NormalizeTask(TaskInput) != SessionTask;
        var typed = TaskInput;
        TaskInput = null;
        IsSwitchingTask = true;
        return pending ? RunAsync(() => _focus.SetTaskAsync(typed)) : Task.CompletedTask;
    }

    /// <summary>Fecha o trecho com o nome atual e segue o mesmo foco com o novo (ver <see cref="FocusService.SwitchTaskAsync"/>).</summary>
    [RelayCommand(CanExecute = nameof(CanConfirmSwitch))]
    private Task SwitchTaskAsync()
    {
        if (!IsSwitchingTask || !CanConfirmSwitch)
            return Task.CompletedTask;
        return ActAsync(() => RunAsync(async () =>
        {
            await _focus.SwitchTaskAsync(TaskInput);
            // Trocou: o modo de troca acaba, e a recarga não o devolve como rascunho.
            IsSwitchingTask = false;
            TaskInput = TodayGoalViewModel.NormalizeTask(TaskInput);
        }));
    }

    [RelayCommand]
    private void CancelSwitchTask()
    {
        IsSwitchingTask = false;
        TaskInput = SessionTask;
    }

    /// <summary>Voltar do sistema: fecha o que está aberto por cima da sessão. Falso se não havia nada.</summary>
    public bool TryGoBack()
    {
        if (Editor is not null)
            Editor.CancelCommand.Execute(null);
        else if (IsReviewing)
            CancelReview();
        else if (IsConfirmingEnd)
            CancelEnd();
        else if (IsConfirmingDiscard)
            CancelDiscard();
        else if (IsSwitchingTask)
            CancelSwitchTask();
        else if (IsEditingTask)
            CancelEditTask();
        else
            return false;
        return true;
    }

    /// <summary>Um toque que muda a sessão: os outros ficam desligados até ele terminar (com a recarga).</summary>
    private async Task ActAsync(Func<Task> work)
    {
        if (_acting)
            return;
        _acting = true;
        RaiseActions();
        try
        {
            await work();
        }
        finally
        {
            _acting = false;
            RaiseActions();
        }
    }

    private void RaiseActions()
    {
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(CanPauseResume));
        AdvanceCommand.NotifyCanExecuteChanged();
        SkipCommand.NotifyCanExecuteChanged();
        ConfirmEndCommand.NotifyCanExecuteChanged();
        ConfirmReviewCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();
        SwitchTaskCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Fecha o foco aberto, passando antes pela conferência quando o extra pede.</summary>
    private Task CutAsync(PendingCut cut)
    {
        if (Live is not { } session)
            return Task.CompletedTask;
        if (FocusClock.NeedsReview(session, Now))
        {
            OpenReview(cut);
            return Task.CompletedTask;
        }
        return RunCutAsync(cut, reviewedMinutes: null);
    }

    private async Task RunCutAsync(PendingCut cut, int? reviewedMinutes)
    {
        var goalTitle = _sessionGoalTitle;
        var review = false;
        await RunAsync(async () =>
        {
            FocusSummary? summary;
            try
            {
                // Como o ■ do cronômetro: o nome digitado e não confirmado vale para o trecho que fecha agora.
                await SaveTypedTaskAsync();
                summary = cut == PendingCut.Advance
                    ? await _focus.AdvanceAsync(reviewedMinutes)
                    : await _focus.EndAsync(reviewedMinutes);
            }
            catch (GoalStateException e) when (e.Message == ReviewRequired)
            {
                // O relógio passou dos 30 min de extra entre a leitura e o toque.
                review = true;
                return;
            }
            IsReviewing = false;
            if (summary is not null)
                ShowSummary(summary, summary.Assigned ? goalTitle : null);
        });
        if (review)
            OpenReview(cut);
    }

    /// <summary>Abre a conferência com o trecho aberto (do último corte até agora, com o extra), em horas e minutos.</summary>
    private void OpenReview(PendingCut cut)
    {
        if (Live is not { } session)
            return;
        var open = FocusClock.OpenFocus(session, Now);
        var minutes = Math.Max(0, (int)Math.Round(open.TotalMinutes, MidpointRounding.AwayFromZero));
        _pendingCut = cut;
        (ReviewHoursInput, ReviewMinutesInput) = (minutes / 60, minutes % 60);
        Error = null;
        IsConfirmingEnd = false;
        IsReviewing = true;
    }

    /// <summary>Grava o nome digitado e ainda não gravado, se houver (fora do modo de troca e só no foco).</summary>
    private Task SaveTypedTaskAsync() =>
        IsFocusBlock && !IsSwitchingTask && TodayGoalViewModel.NormalizeTask(TaskInput) != SessionTask
            ? _focus.SetTaskAsync(TaskInput)
            : Task.CompletedTask;

    /// <summary>
    /// Chama o serviço e recarrega. Os erros vão para <see cref="Error"/>, depois da recarga (que limpa o erro antigo); a
    /// conferência aberta continua aberta se o valor foi recusado.
    /// </summary>
    private async Task RunAsync(Func<Task> action)
    {
        Error = null;
        string? error = null;
        try
        {
            await action();
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
            Trace.TraceError($"Falha ao atualizar a sessão de Pomodoro: {TraceText.Of(ex)}");
            error = "Não foi possível atualizar a sessão.";
        }
        await LoadAsync();
        Error ??= error;
    }

    /// <summary>Sem sugestões se a leitura falhar: o campo continua aceitando texto livre.</summary>
    private async Task<IReadOnlyList<string>> TaskSuggestionsAsync()
    {
        try
        {
            return await _focus.TaskSuggestionsAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Trace.TraceWarning($"Sugestões de subatividade indisponíveis: {TraceText.Types(e)}");
            return [];
        }
    }

    /// <summary>O rascunho do campo da subatividade, guardado antes de uma recarga; nulo se o campo mostra o gravado.</summary>
    private (string? Input, bool Switching, bool Editing)? TaskDraft() =>
        IsRunning && (IsSwitchingTask || IsEditingTask || TodayGoalViewModel.NormalizeTask(TaskInput) != SessionTask)
            ? (TaskInput, IsSwitchingTask, IsEditingTask)
            : null;

    /// <summary>
    /// Depois da recarga, o texto digitado volta enquanto for a mesma sessão, ainda num foco, e ainda diferir do nome
    /// gravado; senão, o campo mostra o nome gravado. Na pausa o campo não aparece, então ✎ e ⇄ abertos fecham (os
    /// controles da sessão dependem disso), e o ⇄ fecha também quando a troca deixa de valer (esperando o toque).
    /// </summary>
    private void RestoreTaskDraft((string? Input, bool Switching, bool Editing)? draft)
    {
        if (draft is { } d && IsRunning && IsFocusBlock && (!d.Switching || CanSwitchTask)
            && (d.Switching || d.Editing || TodayGoalViewModel.NormalizeTask(d.Input) != SessionTask))
        {
            TaskInput = d.Input;
            IsSwitchingTask = d.Switching;
            // Com nome gravado, o rascunho só fica à vista no modo de edição.
            IsEditingTask = !d.Switching && SessionTask is not null;
            return;
        }
        IsSwitchingTask = false;
        IsEditingTask = false;
        TaskInput = SessionTask;
    }

    private IReadOnlyList<FocusDotViewModel> BuildDots()
    {
        if (Live is not { } s)
            return [];
        var current = s.Step % s.Plan.Blocks.Count;
        return s.Plan.Blocks
            .Select((b, i) => new FocusDotViewModel(b.Kind == FocusBlockKind.Focus, i < current, i == current))
            .ToList();
    }

    /// <summary>Segundos que faltam, arredondados para cima: o bloco começa em "25:00" e chega a "00:00" no fim.</summary>
    private long? RemainingSeconds() =>
        Live is { } s ? (long)Math.Ceiling(FocusClock.Remaining(s, Now).TotalSeconds) : null;

    /// <summary>A cada segundo. Se o foco chegou ao fim com o ⇄ aberto, a troca não vale mais: o modo de troca fecha.</summary>
    private void Tick()
    {
        if (IsSwitchingTask && !CanSwitchTask)
            CancelSwitchTask();
        RaiseClock();
    }

    /// <summary>Avisa a tela do que muda com o tempo: a contagem, a fase, o extra e o que pode ser tocado.</summary>
    private void RaiseClock()
    {
        OnPropertyChanged(nameof(MinutesText));
        OnPropertyChanged(nameof(SecondsText));
        OnPropertyChanged(nameof(PhaseText));
        OnPropertyChanged(nameof(OvertimeText));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsBreak));
        OnPropertyChanged(nameof(HasNextBlock));
        OnPropertyChanged(nameof(NextActionText));
        OnPropertyChanged(nameof(TaskLabel));
        OnPropertyChanged(nameof(ShowsTaskLabel));
        OnPropertyChanged(nameof(ShowsTaskField));
        OnPropertyChanged(nameof(ShowsConfirmTask));
        OnPropertyChanged(nameof(IsTaskFieldOpen));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanPauseResume));
        PauseResumeCommand.NotifyCanExecuteChanged();
        BeginSwitchTaskCommand.NotifyCanExecuteChanged();
        SwitchTaskCommand.NotifyCanExecuteChanged();
    }

    private void StopTicker()
    {
        _ticker?.Dispose();
        _ticker = null;
    }
}
