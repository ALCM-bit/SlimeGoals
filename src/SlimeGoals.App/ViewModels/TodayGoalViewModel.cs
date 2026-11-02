using CommunityToolkit.Mvvm.ComponentModel;
using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Um cartão da tela Hoje: progresso do período, painel de valor (quantidade e tempo) e cronômetro (tempo).
/// Estado puro; os comandos ficam no <see cref="TodayPageViewModel"/>, que recebe o cartão como parâmetro.
/// </summary>
public sealed partial class TodayGoalViewModel : ObservableObject
{
    private readonly ActiveTimer? _timer;
    private readonly TimeProvider _time;

    public TodayGoalViewModel(GoalProgress progress, ActiveTimer? timer, TimeProvider time)
    {
        Goal = progress.Goal;
        ProgressText = GoalFormatting.Progress(progress);
        Fraction = Math.Clamp((double)progress.Done / Goal.Target, 0, 1);
        IsMet = progress.IsMet;
        // Só meta de tempo mostra cronômetro no cartão; qualquer outro caso vai para a faixa da página.
        _timer = timer?.GoalId == Goal.Id && Goal.Measure == Measure.Duration ? timer : null;
        _time = time;
        Elapsed = "";
        if (_timer is { StoppedAt: not null })
            StoppedMinutesInput = TimerService.Minutes(_timer, time.GetUtcNow());
        UpdateElapsed();
    }

    public Goal Goal { get; }
    public string Title => Goal.Title;
    public string IconKey => Goal.Icon;
    public GoalColor Color => Goal.Color;
    public bool IsCompletion => Goal.Measure == Measure.Completion;
    public bool IsQuantity => Goal.Measure == Measure.Quantity;
    public bool IsDuration => Goal.Measure == Measure.Duration;
    public string ProgressText { get; }
    public double Fraction { get; }
    public bool IsMet { get; }

    // ---- Painel de valor (quantidade e tempo) ----
    [ObservableProperty] public partial bool IsPanelOpen { get; set; }
    /// <summary>false = Adicionar; true = Remover.</summary>
    [ObservableProperty] public partial bool PanelRemoves { get; set; }
    [ObservableProperty] public partial decimal? PanelAmountInput { get; set; }
    [ObservableProperty] public partial decimal? PanelHoursInput { get; set; }
    [ObservableProperty] public partial decimal? PanelMinutesInput { get; set; }
    [ObservableProperty] public partial string? PanelNote { get; set; }
    [ObservableProperty] public partial string? PanelError { get; set; }

    public bool PanelAdds { get => !PanelRemoves; set => PanelRemoves = !value; }
    partial void OnPanelRemovesChanged(bool value) => OnPropertyChanged(nameof(PanelAdds));

    /// <summary>Abre o painel limpo.</summary>
    public void ResetPanel(bool open)
    {
        PanelRemoves = false;
        PanelAmountInput = PanelHoursInput = PanelMinutesInput = null;
        PanelNote = PanelError = null;
        IsPanelOpen = open;
    }

    /// <summary>Valor do painel na unidade base (com sinal), ou null se nada foi digitado.</summary>
    public int? PanelAmount()
    {
        decimal? value = IsDuration
            ? (PanelHoursInput is null && PanelMinutesInput is null ? null : (PanelHoursInput ?? 0) * 60 + (PanelMinutesInput ?? 0))
            : PanelAmountInput;
        if (value is not { } v)
            return null;
        var amount = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return PanelRemoves ? -amount : amount;
    }

    // ---- Cronômetro (só tempo) ----
    public bool IsTimerRunning => _timer is { StoppedAt: null };
    public bool IsTimerStopped => _timer is { StoppedAt: not null };
    public bool HasNoTimer => _timer is null;
    [ObservableProperty] public partial string Elapsed { get; set; }
    [ObservableProperty] public partial decimal? StoppedMinutesInput { get; set; }
    [ObservableProperty] public partial bool CanStartTimer { get; set; }

    /// <summary>Atualiza o tempo exibido do cronômetro rodando (chamado a cada segundo pela página).</summary>
    public void UpdateElapsed()
    {
        if (_timer is not null)
            Elapsed = GoalFormatting.Elapsed((_timer.StoppedAt ?? _time.GetUtcNow()) - _timer.StartedAt);
    }
}
