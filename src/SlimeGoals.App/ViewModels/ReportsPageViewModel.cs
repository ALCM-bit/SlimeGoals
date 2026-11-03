using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Goals;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Tela Relatórios (spec §10): semana ou mês, com navegação entre períodos. Recarrega quando os dados
/// mudam (<see cref="DataChangeNotifier"/>); se houver cargas sobrepostas, vale a última.
/// </summary>
public sealed partial class ReportsPageViewModel(ReportService reports, TimeProvider time, DataChangeNotifier changes)
    : PageViewModel
{
    private static readonly string[] WeekLabels = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];

    private bool _subscribed;
    private int _loadVersion;
    private SynchronizationContext? _ui;
    private DateOnly? _weekStart;
    private (int Year, int Month)? _month;

    public override string Title => "Relatórios";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeek), nameof(IsMonth))]
    public partial ReportMode Mode { get; set; }

    /// <summary>Segmentos "Semana"/"Mês" (RadioButton, dois sentidos): marcar um troca o modo.</summary>
    public bool IsWeek
    {
        get => Mode == ReportMode.Week;
        set
        {
            if (value)
                Mode = ReportMode.Week;
        }
    }

    public bool IsMonth
    {
        get => Mode == ReportMode.Month;
        set
        {
            if (value)
                Mode = ReportMode.Month;
        }
    }

    [ObservableProperty] public partial string PeriodTitle { get; set; } = "";
    [ObservableProperty] public partial string? PeriodSubtitle { get; set; }
    [ObservableProperty] public partial string PercentText { get; set; } = "—";
    [ObservableProperty] public partial string? RemindersText { get; set; }
    [ObservableProperty] public partial string BarsTitle { get; set; } = "Registros por dia";
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public ObservableCollection<BarViewModel> Bars { get; } = [];

    /// <summary>Barra cujo detalhe aparece embaixo do gráfico. Ao abrir um período, a de hoje (ou da semana atual).</summary>
    [ObservableProperty] public partial BarViewModel? SelectedBar { get; set; }

    private int? _selectedIndex;

    public ObservableCollection<ReportRowViewModel> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;
    public string EmptyMessage => "Nenhuma meta neste período.";

    public override void Activate()
    {
        ResetPeriod();
        LoadCommand.Execute(null);
    }

    partial void OnModeChanged(ReportMode value)
    {
        ResetPeriod();
        LoadCommand.Execute(null);
    }

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    private DateOnly CurrentWeekStart => WeekMath.StartOfWeek(Today);

    /// <summary>Mês do relatório atual: o da segunda-feira da semana de hoje (o mês é feito de semanas inteiras).</summary>
    private (int Year, int Month) CurrentMonth => (CurrentWeekStart.Year, CurrentWeekStart.Month);

    private void ResetPeriod()
    {
        _weekStart = null;
        _month = null;
        _selectedIndex = null;
        NextCommand.NotifyCanExecuteChanged();
    }

    private bool IsCurrent => Mode == ReportMode.Week
        ? (_weekStart ?? CurrentWeekStart) == CurrentWeekStart
        : (_month ?? CurrentMonth) == CurrentMonth;

    private bool CanGoNext() => !IsCurrent;

    [RelayCommand]
    private Task PreviousAsync() => MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextAsync() => MoveAsync(1);

    private Task MoveAsync(int step)
    {
        if (Mode == ReportMode.Week)
            _weekStart = (_weekStart ?? CurrentWeekStart).AddDays(7 * step);
        else
        {
            var (year, month) = _month ?? CurrentMonth;
            var moved = new DateOnly(year, month, 1).AddMonths(step);
            _month = (moved.Year, moved.Month);
        }
        _selectedIndex = null;
        NextCommand.NotifyCanExecuteChanged();
        return LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void SelectBar(BarViewModel? bar)
    {
        if (bar is null || !Bars.Contains(bar))
            return;
        _selectedIndex = Bars.IndexOf(bar);
        ApplySelection();
    }

    /// <summary>
    /// Mantém a barra escolhida na recarga do mesmo período; num período novo (ou sem escolha), seleciona a atual.
    /// </summary>
    private void ApplySelection()
    {
        var index = _selectedIndex is { } i && i < Bars.Count ? i : Bars.ToList().FindIndex(b => b.IsCurrent);
        for (var k = 0; k < Bars.Count; k++)
            Bars[k].IsSelected = k == index;
        SelectedBar = index >= 0 ? Bars[index] : null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
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
            if (Mode == ReportMode.Week)
            {
                var report = await reports.GetWeekAsync(_weekStart ?? CurrentWeekStart);
                if (version == _loadVersion)
                    ShowWeek(report);
            }
            else
            {
                var (year, month) = _month ?? CurrentMonth;
                var report = await reports.GetMonthAsync(year, month);
                if (version == _loadVersion)
                    ShowMonth(report);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao carregar o relatório: {ex}");
            ErrorMessage = "Não foi possível carregar o relatório.";
        }
        finally
        {
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void ShowWeek(WeekReport report)
    {
        var today = Today;
        PeriodTitle = ReportFormatting.WeekTitle(report.Week);
        PeriodSubtitle = null;
        PercentText = ReportFormatting.Percent(report.Percent);
        RemindersText = ReportFormatting.Reminders(report.RemindersFired, report.RemindersAnswered);
        BarsTitle = "Registros por dia";

        var max = Math.Max(1, report.Days.Max(d => d.Records));
        Bars.Clear();
        foreach (var day in report.Days)
            Bars.Add(new BarViewModel(WeekLabels[((int)day.Day.DayOfWeek + 6) % 7],
                day.Records.ToString(CultureInfo.InvariantCulture), (double)day.Records / max, day.Day == today,
                ReportFormatting.DayTitle(day.Day),
                day.Goals.Select(e => Detail(e.Goal, ReportFormatting.DayEntry(e), e.Met == true)).ToList(),
                "Nenhum registro neste dia."));
        ApplySelection();

        Rows.Clear();
        foreach (var row in report.Goals)
            Rows.Add(NewRow(row.Goal, ReportFormatting.WeekDetail(row), row.Percent, ReportFormatting.BestDay(row)));
        NextCommand.NotifyCanExecuteChanged();
    }

    private void ShowMonth(MonthReport report)
    {
        var currentWeek = CurrentWeekStart;
        PeriodTitle = ReportFormatting.MonthTitle(report.Year, report.Month);
        PeriodSubtitle = ReportFormatting.MonthSubtitle(report.Range);
        PercentText = ReportFormatting.Percent(report.Percent);
        RemindersText = null;
        BarsTitle = "Evolução por semana";

        Bars.Clear();
        for (var i = 0; i < report.Weeks.Count; i++)
        {
            var week = report.Weeks[i];
            var index = i;
            var details = report.Goals
                .Where(r => r.WeekPercents[index] is not null)
                .Select(r => Detail(r.Goal, ReportFormatting.Percent(r.WeekPercents[index]), r.WeekPercents[index] >= 100))
                .ToList();
            Bars.Add(new BarViewModel(ReportFormatting.DayMonth(week.Monday), ReportFormatting.Percent(week.Percent),
                (week.Percent ?? 0) / 100.0, week.Monday == currentWeek, ReportFormatting.WeekOf(week.Monday), details,
                "Nenhuma meta nesta semana."));
        }
        ApplySelection();

        Rows.Clear();
        foreach (var row in report.Goals)
            Rows.Add(NewRow(row.Goal, ReportFormatting.MonthDetail(row), row.Percent, ReportFormatting.Streak(row)));
        NextCommand.NotifyCanExecuteChanged();
    }

    private static BarDetailViewModel Detail(Goal goal, string value, bool met) =>
        new(goal.Title, goal.Icon, goal.Color, value, met);

    private static ReportRowViewModel NewRow(Goal goal, string detail, int? percent, string? extra) =>
        new(goal.Title, goal.Icon, goal.Color, detail, ReportFormatting.Percent(percent), (percent ?? 0) / 100.0,
            extra, goal.ArchivedAt is not null);

    private void OnUi(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }
}
