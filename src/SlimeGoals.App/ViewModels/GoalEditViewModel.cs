using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Goals;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Página de criar/editar meta. Fala só com o <see cref="GoalService"/>; quem a abriu
/// recebe o resultado pelo callback <c>close(saved)</c>.
/// </summary>
public sealed partial class GoalEditViewModel : ObservableObject
{
    private static readonly int[] StandardIntervals = [15, 30, 60, 90, 120, 180, 240, 360, 720, 1440];
    private const string MissingTime = "Informe o horário.";
    private const string MissingTarget = "Informe o alvo.";

    private readonly GoalService _service;
    private readonly Goal? _existing;
    private readonly Action<bool> _close;
    private readonly Dictionary<string, string> _errors = [];
    private bool _submitted;
    private int _rememberedTarget;
    private bool _targetMissing;

    public GoalEditViewModel(GoalService service, Goal? existing, Action<bool> close)
    {
        _service = service;
        _existing = existing;
        _close = close;

        Icons = GoalIcons.All.Select(IconOption.From).ToList();
        Colors =
        [
            new(GoalColor.Purple, "Roxo"), new(GoalColor.Pink, "Rosa"),
            new(GoalColor.Amber, "Âmbar"), new(GoalColor.Green, "Verde"),
        ];
        Days =
        [
            new(WeekDays.Monday, "S", "Segunda"), new(WeekDays.Tuesday, "T", "Terça"),
            new(WeekDays.Wednesday, "Q", "Quarta"), new(WeekDays.Thursday, "Q", "Quinta"),
            new(WeekDays.Friday, "S", "Sexta"), new(WeekDays.Saturday, "S", "Sábado"),
            new(WeekDays.Sunday, "D", "Domingo"),
        ];

        var interval = existing?.ReminderEveryMinutes ?? 60;
        Intervals = StandardIntervals.Append(interval).Distinct().Order().Select(m => new IntervalOption(m)).ToList();

        Title = existing?.Title ?? "";
        Recurrence = existing?.Recurrence ?? Recurrence.Daily;
        Target = existing?.Target ?? 1;
        _rememberedTarget = Target;
        SelectedIcon = IconOption.From(GoalIcons.Get(existing?.Icon ?? GoalIcons.DefaultKey));
        SelectedColor = Colors.Single(c => c.Color == (existing?.Color ?? GoalColor.Purple));
        SelectedInterval = new IntervalOption(interval);
        ActiveFrom = (existing?.ActiveFrom ?? new TimeOnly(8, 0)).ToTimeSpan();
        ActiveUntil = (existing?.ActiveUntil ?? new TimeOnly(20, 0)).ToTimeSpan();
        StartDate = existing?.StartDate?.ToDateTime(TimeOnly.MinValue);
        EndDate = existing?.EndDate?.ToDateTime(TimeOnly.MinValue);
        var activeDays = existing?.ActiveDays ?? WeekDays.All;
        foreach (var day in Days)
        {
            day.IsSelected = activeDays.HasFlag(day.Day);
            day.PropertyChanged += (_, _) => RevalidateIfSubmitted();
        }

        PropertyChanged += OnInputChanged;
    }

    public bool IsNew => _existing is null;
    public string PageTitle => IsNew ? "Nova meta" : "Editar meta";

    public IReadOnlyList<IconOption> Icons { get; }
    public IReadOnlyList<ColorOption> Colors { get; }
    public IReadOnlyList<IntervalOption> Intervals { get; }
    public IReadOnlyList<DayOption> Days { get; }

    [ObservableProperty] public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTarget), nameof(TargetSuffix), nameof(IsOnce), nameof(IsDaily), nameof(IsWeekly))]
    public partial Recurrence Recurrence { get; set; }

    [ObservableProperty] public partial int Target { get; set; }

    /// <summary>
    /// Ponte para o NumericUpDown, que trabalha com decimal? e devolve null quando o campo é apagado.
    /// Sem ela, o null não chegaria a <see cref="Target"/> e a meta seria salva com o alvo anterior.
    /// </summary>
    public decimal? TargetInput
    {
        get => _targetMissing ? null : Target;
        set
        {
            _targetMissing = value is null;
            if (value is { } v)
                Target = (int)Math.Round(v, MidpointRounding.AwayFromZero);
            OnPropertyChanged();
            RevalidateIfSubmitted();
        }
    }

    partial void OnTargetChanged(int value)
    {
        _targetMissing = false;
        OnPropertyChanged(nameof(TargetInput));
    }
    [ObservableProperty] public partial IconOption SelectedIcon { get; set; }
    [ObservableProperty] public partial ColorOption SelectedColor { get; set; }
    [ObservableProperty] public partial IntervalOption SelectedInterval { get; set; }
    [ObservableProperty] public partial TimeSpan? ActiveFrom { get; set; }
    [ObservableProperty] public partial TimeSpan? ActiveUntil { get; set; }
    [ObservableProperty] public partial DateTime? StartDate { get; set; }
    [ObservableProperty] public partial DateTime? EndDate { get; set; }
    [ObservableProperty] public partial string? GeneralError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool IsConfirmingDelete { get; set; }

    public bool ShowsTarget => Recurrence != Recurrence.Once;
    public string TargetSuffix => Recurrence == Recurrence.Weekly ? "por semana" : "por dia";

    // Atalhos para os botões de segmento da View.
    public bool IsOnce { get => Recurrence == Recurrence.Once; set { if (value) Recurrence = Recurrence.Once; } }
    public bool IsDaily { get => Recurrence == Recurrence.Daily; set { if (value) Recurrence = Recurrence.Daily; } }
    public bool IsWeekly { get => Recurrence == Recurrence.Weekly; set { if (value) Recurrence = Recurrence.Weekly; } }

    public string? TitleError => ErrorFor(nameof(GoalDraft.Title));
    public string? TargetError => ErrorFor(nameof(GoalDraft.Target));
    public string? ReminderError => ErrorFor(nameof(GoalDraft.ReminderEveryMinutes));
    public string? ActiveFromError => ErrorFor(nameof(GoalDraft.ActiveFrom));
    public string? ActiveUntilError => ErrorFor(nameof(GoalDraft.ActiveUntil));
    public string? ActiveDaysError => ErrorFor(nameof(GoalDraft.ActiveDays));
    public string? EndDateError => ErrorFor(nameof(GoalDraft.EndDate));
    public string? IconError => ErrorFor(nameof(GoalDraft.Icon));

    public string? ErrorFor(string field) => _errors.GetValueOrDefault(field);

    /// <summary>Única: alvo fixo em 1, lembrando o anterior para quando voltar a diária/semanal.</summary>
    partial void OnRecurrenceChanged(Recurrence oldValue, Recurrence newValue)
    {
        if (newValue == Recurrence.Once && oldValue != Recurrence.Once)
        {
            _rememberedTarget = Target;
            Target = 1;
        }
        else if (oldValue == Recurrence.Once && newValue != Recurrence.Once)
        {
            Target = _rememberedTarget;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _submitted = true;
        GeneralError = null;
        if (!Revalidate(out var draft))
            return;

        try
        {
            if (_existing is null)
                await _service.CreateAsync(draft);
            else
                await _service.UpdateAsync(_existing.Id, draft);
            _close(true);
        }
        catch (GoalValidationException ex)
        {
            SetErrors(ex.Errors.Select(e => (e.Field, e.Message)));
        }
        catch (GoalStateException ex)
        {
            GeneralError = ex.Message;
        }
        catch (GoalNotFoundException)
        {
            GeneralError = "Esta meta não existe mais.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Erro inesperado (banco travado, disco cheio): exceção solta num AsyncRelayCommand derrubaria o app.
            GeneralError = "Não foi possível salvar a meta.";
        }
    }

    [RelayCommand]
    private void Cancel() => _close(false);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete() => IsConfirmingDelete = true;

    private bool CanDelete() => !IsNew && !IsConfirmingDelete;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (_existing is null)
            return;
        try
        {
            await _service.DeleteAsync(_existing.Id);
            _close(true);
        }
        catch (GoalNotFoundException)
        {
            GeneralError = "Esta meta não existe mais.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            GeneralError = "Não foi possível excluir a meta.";
        }
    }

    private static readonly HashSet<string> Inputs =
    [
        nameof(Title), nameof(Recurrence), nameof(Target), nameof(SelectedIcon), nameof(SelectedColor),
        nameof(SelectedInterval), nameof(ActiveFrom), nameof(ActiveUntil), nameof(StartDate), nameof(EndDate),
    ];

    private void OnInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && Inputs.Contains(name))
            RevalidateIfSubmitted();
    }

    private void RevalidateIfSubmitted()
    {
        if (_submitted)
            Revalidate(out _);
    }

    /// <summary>Valida como o serviço validaria, mais horários vazios (os seletores aceitam null).</summary>
    private bool Revalidate(out GoalDraft draft)
    {
        draft = new GoalDraft(
            Title, Recurrence, Recurrence == Recurrence.Once ? 1 : Target, SelectedIcon.Key, SelectedColor.Color,
            SelectedInterval.Minutes,
            TimeOnly.FromTimeSpan(ActiveFrom ?? TimeSpan.Zero),
            TimeOnly.FromTimeSpan(ActiveUntil ?? new TimeSpan(23, 59, 0)),
            Days.Where(d => d.IsSelected).Aggregate(WeekDays.None, (all, d) => all | d.Day),
            StartDate is { } s ? DateOnly.FromDateTime(s) : null,
            EndDate is { } e ? DateOnly.FromDateTime(e) : null);

        var errors = GoalValidator.Validate(draft).Select(x => (x.Field, x.Message)).ToList();
        if (ActiveFrom is null || ActiveUntil is null)
        {
            // Com um horário vazio, a comparação início < fim não faz sentido.
            errors.RemoveAll(x => x.Field == nameof(GoalDraft.ActiveUntil));
            if (ActiveFrom is null) errors.Add((nameof(GoalDraft.ActiveFrom), MissingTime));
            if (ActiveUntil is null) errors.Add((nameof(GoalDraft.ActiveUntil), MissingTime));
        }

        if (ShowsTarget && _targetMissing)
        {
            errors.RemoveAll(x => x.Field == nameof(GoalDraft.Target));
            errors.Add((nameof(GoalDraft.Target), MissingTarget));
        }

        SetErrors(errors);
        return errors.Count == 0;
    }

    private void SetErrors(IEnumerable<(string Field, string Message)> errors)
    {
        _errors.Clear();
        foreach (var (field, message) in errors)
            _errors.TryAdd(field, message);

        foreach (var name in new[]
                 {
                     nameof(TitleError), nameof(TargetError), nameof(ReminderError), nameof(ActiveFromError),
                     nameof(ActiveUntilError), nameof(ActiveDaysError), nameof(EndDateError), nameof(IconError),
                 })
            OnPropertyChanged(name);
    }
}
