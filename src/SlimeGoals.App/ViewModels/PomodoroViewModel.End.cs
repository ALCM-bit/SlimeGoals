using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// A tela de fim (spec Pomodoro §4.4): o foco total e os trechos por subatividade. A sessão com meta já gravou tudo e o
/// resumo fica na tela até "Nova sessão", mesmo sem sessão no banco. A sem meta espera um destino para o que está
/// guardado: somar numa meta de tempo, criar uma meta com ele ou descartar.
/// </summary>
public sealed partial class PomodoroViewModel
{
    private FocusSummary? _summary;
    private string? _summaryGoal;

    /// <summary>O tempo guardado de uma sessão sem meta ainda não tem destino.</summary>
    public bool IsAwaitingDestination => !IsRunning && _summary is { Assigned: false };

    /// <summary>O foco total da sessão ("1 h 38 min"), sob o rótulo "Foco feito".</summary>
    public string? SummaryText => IsFinished ? GoalFormatting.Duration(_summary!.Seconds) : null;

    /// <summary>Os minutos por subatividade, na ordem da primeira aparição: "Revisar capítulo 3 · 58 min".</summary>
    public IReadOnlyList<string> SummaryLines => IsFinished
        ? _summary!.Lines.Select(l => $"{l.Task ?? NoName} · {GoalFormatting.Duration(l.Seconds)}").ToList()
        : [];

    /// <summary>"Somar na meta…": as metas de tempo ativas hoje.</summary>
    [ObservableProperty] public partial IReadOnlyList<FocusGoalOption> AssignOptions { get; set; } = [];

    private FocusGoalOption? _assignGoal;

    /// <summary>A meta escolhida para somar. Ignora null: o ComboBox pode desmarcar ao trocar a lista de opções.</summary>
    public FocusGoalOption? AssignGoal
    {
        get => _assignGoal;
        set
        {
            if (value is null)
                OnPropertyChanged();
            else if (SetProperty(ref _assignGoal, value))
                AssignCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>"Criar uma meta com ele": o editor de meta nova, já como meta de tempo.</summary>
    [ObservableProperty] public partial GoalEditViewModel? Editor { get; set; }

    [ObservableProperty] public partial bool IsConfirmingDiscard { get; set; }

    /// <summary>
    /// Soma o que está guardado em <paramref name="target"/> (ou em <see cref="AssignGoal"/>), com a data e a hora de
    /// agora. Depois, a tela de fim fica com o resumo e a meta, como a de uma sessão com meta.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAssign))]
    private Task AssignAsync(FocusGoalOption? target)
    {
        target ??= AssignGoal;
        if (target?.Id is not { } id || _summary is not { Assigned: false } summary)
            return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await _focus.AssignAsync(id);
            ShowSummary(summary with { Assigned = true }, target.Title);
        });
    }

    private bool CanAssign(FocusGoalOption? target) => IsAwaitingDestination && (target ?? AssignGoal) is not null;

    [RelayCommand]
    private void CreateGoal()
    {
        if (!IsAwaitingDestination)
            return;
        GoalEditViewModel? editor = null;
        editor = new GoalEditViewModel(_goals, null, measureLocked: true, saved => OnEditorClosed(editor!, saved),
            initialMeasure: Measure.Duration) { BackText = "← Tempo" };
        Editor = editor;
    }

    /// <summary>Salvou: o tempo entra na meta nova. Cancelou: volta à tela de fim, sem perder nada.</summary>
    private void OnEditorClosed(GoalEditViewModel editor, bool saved)
    {
        Editor = null;
        if (saved && editor.SavedGoalId is { } id)
            AssignCommand.Execute(new FocusGoalOption(id, editor.Title.Trim()));
    }

    [RelayCommand]
    private void AskDiscard()
    {
        if (IsAwaitingDestination)
            IsConfirmingDiscard = true;
    }

    [RelayCommand]
    private void CancelDiscard() => IsConfirmingDiscard = false;

    /// <summary>Apaga os trechos guardados; o preparo volta.</summary>
    [RelayCommand]
    private Task ConfirmDiscardAsync()
    {
        if (!IsAwaitingDestination)
            return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await _focus.DiscardAsync();
            IsConfirmingDiscard = false;
            ShowSummary(null, null);
        });
    }

    /// <summary>"Nova sessão": tira o resumo da tela e volta ao preparo, com a sugestão nova.</summary>
    [RelayCommand]
    private void NewSession()
    {
        if (_summary is not { Assigned: true })
            return;
        ShowSummary(null, null);
        LoadCommand.Execute(null);
    }

    private void ShowSummary(FocusSummary? summary, string? goal)
    {
        _summary = summary;
        _summaryGoal = goal;
        if (_summary is null)
            _assignGoal = null;
        RaiseStates();
    }

    /// <summary>
    /// Decide o resumo depois de ler a sessão: uma sessão rodando o tira; uma terminada mostra o que ela guarda; sem
    /// sessão, só fica o resumo já somado (o da sessão com meta, ou o que a pessoa somou), até "Nova sessão".
    /// </summary>
    private void HoldSummary(FocusSession? session, IReadOnlyList<FocusChunk>? held)
    {
        if (session is { FinishedAt: null })
            (_summary, _summaryGoal) = (null, null);
        else if (held is not null)
            (_summary, _summaryGoal) = (FocusSummary.Of(held, assigned: false), null);
        else if (_summary is { Assigned: false })
            (_summary, _summaryGoal) = (null, null); // o destino foi dado em outro lugar
    }

    /// <summary>Troca as opções de "Somar" e mantém a escolhida enquanto ela continuar na lista.</summary>
    private void SetAssignOptions(IReadOnlyList<FocusGoalOption> options)
    {
        AssignOptions = options;
        _assignGoal = options.FirstOrDefault(o => o.Id == _assignGoal?.Id);
        OnPropertyChanged(nameof(AssignGoal));
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(IsAwaitingDestination));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(SummaryLines));
        AssignCommand.NotifyCanExecuteChanged();
    }
}
