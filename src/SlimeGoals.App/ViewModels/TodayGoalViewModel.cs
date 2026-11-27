using System.Globalization;
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

    public TodayGoalViewModel(GoalProgress progress, ActiveTimer? timer, TimeProvider time,
        IReadOnlyList<Checkin>? todayCheckins = null, IReadOnlyDictionary<string, GoalColor>? tagColors = null)
    {
        Goal = progress.Goal;
        // Tag que só existe como ligação (ainda não chegou) fica em Purple.
        Tags = [.. Goal.Tags.Select(n => new TagChipViewModel(n,
            tagColors is not null && tagColors.TryGetValue(n, out var c) ? c : GoalColor.Purple))];
        ProgressText = GoalFormatting.Progress(progress);
        Fraction = Math.Clamp((double)progress.Done / Goal.Target, 0, 1);
        IsMet = progress.IsMet;
        // Só meta de tempo mostra cronômetro no cartão; qualquer outro caso vai para a faixa da página.
        _timer = timer?.GoalId == Goal.Id && Goal.Measure == Measure.Duration ? timer : null;
        _time = time;
        Elapsed = "";
        if (_timer is { StoppedAt: not null })
        {
            (StoppedHoursInput, StoppedMinutesInput, StoppedSecondsInput) =
                DurationFields.Split(TimerService.Seconds(_timer, time.GetUtcNow()));
            PanelNote = _timer.Task; // a tarefa vira a nota do registro, e a pessoa pode editar
        }
        TaskInput = TimerTask;
        var records = IsDuration ? todayCheckins ?? [] : [];
        HasChunks = records.Count > 0;
        ChunksHeader = $"Hoje · {GoalFormatting.Duration(Math.Max(0, records.Sum(c => c.Amount)))}";
        if (IsTimerRunning)
            _runningChunk = TodayChunkViewModel.Running(Goal.Id, _timer!.Task);
        Chunks = [.. _runningChunk is null ? [] : new[] { _runningChunk }, .. records.Select(TodayChunkViewModel.Of)];
        UpdateElapsed();
    }

    private readonly TodayChunkViewModel? _runningChunk;

    // ---- Trechos de hoje (specs/Tarefas_no_cronometro_v1.md §7.2) ----
    /// <summary>Registros do dia, do mais recente para o mais antigo; com o cronômetro rodando, o trecho dele vem antes.</summary>
    public IReadOnlyList<TodayChunkViewModel> Chunks { get; }
    /// <summary>Só meta de tempo com algum registro hoje.</summary>
    public bool HasChunks { get; }
    /// <summary>Soma só dos registros gravados, limitada a 0; o trecho em andamento fica de fora.</summary>
    public string ChunksHeader { get; }
    [ObservableProperty] public partial bool IsChunksOpen { get; set; }

    public Goal Goal { get; }

    /// <summary>Chips pequenos abaixo da barra de progresso.</summary>
    public IReadOnlyList<TagChipViewModel> Tags { get; }
    public bool HasTags => Tags.Count > 0;

    /// <summary>Na linha do desktop, só a primeira tag aparece (decisão 18); as outras viram "+N".</summary>
    public TagChipViewModel? PrimaryTag => Tags.Count > 0 ? Tags[0] : null;
    public string? MoreTagsText => Tags.Count > 1 ? $"+{Tags.Count - 1}" : null;
    public bool HasMoreTags => Tags.Count > 1;

    /// <summary>Hora local ("15:00") do próximo lembrete de hoje; nulo se não há (cumprida, pausada, fora da janela).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNextReminder))]
    public partial string? NextReminder { get; set; }
    public bool HasNextReminder => NextReminder is not null;

    /// <summary>false quando o filtro de tags do Hoje esconde o cartão.</summary>
    [ObservableProperty] public partial bool IsShown { get; set; } = true;
    public string Title => Goal.Title;
    public string IconKey => Goal.Icon;
    public GoalColor Color => Goal.Color;
    public bool IsCompletion => Goal.Measure == Measure.Completion;
    public bool IsQuantity => Goal.Measure == Measure.Quantity;
    public bool IsDuration => Goal.Measure == Measure.Duration;
    public string ProgressText { get; }
    public double Fraction { get; }
    public bool IsMet { get; }

    // ---- Painel de valor (só tempo; a quantidade usa só −/+) ----
    [ObservableProperty] public partial bool IsPanelOpen { get; set; }
    /// <summary>false = Adicionar; true = Remover.</summary>
    [ObservableProperty] public partial bool PanelRemoves { get; set; }
    [ObservableProperty] public partial decimal? PanelHoursInput { get; set; }
    [ObservableProperty] public partial decimal? PanelMinutesInput { get; set; }
    [ObservableProperty] public partial string? PanelNote { get; set; }
    [ObservableProperty] public partial string? PanelError { get; set; }

    public bool PanelAdds { get => !PanelRemoves; set => PanelRemoves = !value; }
    partial void OnPanelRemovesChanged(bool value) => OnPropertyChanged(nameof(PanelAdds));

    /// <summary>Abre (só em meta de tempo) ou fecha o painel, limpo.</summary>
    public void ResetPanel(bool open)
    {
        open &= IsDuration;
        PanelRemoves = false;
        PanelHoursInput = PanelMinutesInput = null;
        PanelNote = PanelError = null;
        IsPanelOpen = open;
    }

    /// <summary>Segundos do painel (com sinal), ou null se nada foi digitado.</summary>
    public int? PanelAmount()
    {
        if (PanelHoursInput is null && PanelMinutesInput is null)
            return null;
        var v = (PanelHoursInput ?? 0) * 3600 + (PanelMinutesInput ?? 0) * 60;
        var amount = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return PanelRemoves ? -amount : amount;
    }

    // ---- Cronômetro (só tempo) ----
    public bool IsTimerRunning => _timer is { StoppedAt: null };
    public bool IsTimerStopped => _timer is { StoppedAt: not null };
    public bool HasNoTimer => _timer is null;

    /// <summary>Aviso do cronômetro parado; o de outro dia diz quando parou, para não parecer de agora.</summary>
    public string StoppedText
    {
        get
        {
            const string check = "Confira o tempo antes de registrar.";
            if (_timer?.StoppedAt is not { } at)
                return "";
            var stopped = TimeZoneInfo.ConvertTime(at, _time.LocalTimeZone);
            return stopped.Date == _time.GetLocalNow().Date
                ? $"Cronômetro parado. {check}"
                : $"Cronômetro parado em {stopped.ToString("dd/MM, HH:mm", CultureInfo.InvariantCulture)}. {check}";
        }
    }

    // ---- Tarefa do cronômetro (specs/Tarefas_no_cronometro_v1.md) ----
    public string? TimerTask => _timer?.Task;

    /// <summary>
    /// O que a pessoa está digitando no campo da tarefa difere do que está gravado (ou ela está trocando de tarefa):
    /// a tela guarda isso ao recarregar, para não apagar o texto no meio da digitação.
    /// </summary>
    public bool HasTaskDraft => IsTimerRunning && (IsSwitchingTask || IsEditingTask || NormalizeTask(TaskInput) != TimerTask);

    internal static string? NormalizeTask(string? task) => string.IsNullOrWhiteSpace(task) ? null : task.Trim();
    [ObservableProperty] public partial string? TaskInput { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTaskLabel), nameof(ShowsTaskField), nameof(ShowsConfirmTask))]
    public partial bool IsSwitchingTask { get; set; }

    /// <summary>A pessoa tocou ✎ para mudar o nome gravado: o campo volta, com ✓ e ✕.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTaskLabel), nameof(ShowsTaskField), nameof(ShowsConfirmTask))]
    public partial bool IsEditingTask { get; set; }

    /// <summary>O nome gravado aparece como texto: é a confirmação de que ele está valendo (spec §7.1).</summary>
    public bool ShowsTaskLabel => IsTimerRunning && TimerTask is not null && !IsEditingTask && !IsSwitchingTask;
    public bool ShowsTaskField => IsTimerRunning && !ShowsTaskLabel;
    public bool ShowsConfirmTask => ShowsTaskField && !IsSwitchingTask;
    public string TaskLabel => $"▸ {TimerTask}";
    [ObservableProperty] public partial IReadOnlyList<string> TaskSuggestions { get; set; } = [];

    [ObservableProperty] public partial string Elapsed { get; set; }
    [ObservableProperty] public partial decimal? StoppedHoursInput { get; set; }
    [ObservableProperty] public partial decimal? StoppedMinutesInput { get; set; }
    [ObservableProperty] public partial decimal? StoppedSecondsInput { get; set; }

    /// <summary>Tempo conferido do cronômetro parado, em segundos, ou null se horas, minutos e segundos estão vazios.</summary>
    public int? StoppedAmount() => DurationFields.Seconds(StoppedHoursInput, StoppedMinutesInput, StoppedSecondsInput);
    [ObservableProperty] public partial bool CanStartTimer { get; set; }

    /// <summary>Atualiza o tempo exibido do cronômetro rodando (chamado a cada segundo pela página).</summary>
    public void UpdateElapsed()
    {
        if (_timer is not null)
            Elapsed = GoalFormatting.Elapsed((_timer.StoppedAt ?? _time.GetUtcNow()) - _timer.StartedAt);
        _runningChunk?.Text = Elapsed;
    }
}
