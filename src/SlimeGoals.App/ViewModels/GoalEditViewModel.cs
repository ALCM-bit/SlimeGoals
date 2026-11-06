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
    private const string MissingTime = "Informe o horário.";
    private const string MissingTarget = "Informe o alvo.";
    private const string MissingDuration = "Informe o tempo.";
    private const string MissingInterval = "Informe o intervalo.";

    private readonly GoalService _service;
    private readonly Goal? _existing;
    private readonly Action<bool> _close;
    private readonly Dictionary<string, string> _errors = [];

    /// <summary>Erros que só o serviço conhece (ex.: fim no passado). Valem até o campo mudar ou até salvar de novo.</summary>
    private readonly Dictionary<string, string> _serviceErrors = [];
    private bool _submitted;
    private bool _targetMissing;

    /// <param name="measureLocked">A meta já tem registros: a medida não pode mudar (spec §5.2).</param>
    public GoalEditViewModel(GoalService service, Goal? existing, bool measureLocked, Action<bool> close)
    {
        _service = service;
        _existing = existing;
        _close = close;
        IsMeasureLocked = measureLocked;

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
        _reminderHours = interval / 60;
        _reminderMinutes = interval % 60;

        Title = existing?.Title ?? "";
        Recurrence = existing?.Recurrence ?? Recurrence.Daily;
        Measure = existing?.Measure ?? Measure.Quantity;
        // Cada medida guarda o próprio alvo: trocar e voltar no formulário não perde o que foi digitado.
        Target = existing is { Measure: Measure.Quantity } ? existing.Target : 1;
        Unit = existing?.Unit ?? "vezes";
        if (existing is { Measure: Measure.Duration })
        {
            _durationHours = existing.Target / 60;
            _durationMinutes = existing.Target % 60;
        }
        SelectedIcon = IconOption.From(GoalIcons.Get(existing?.Icon ?? GoalIcons.DefaultKey));
        SelectedColor = Colors.Single(c => c.Color == (existing?.Color ?? GoalColor.Purple));
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
    public IReadOnlyList<DayOption> Days { get; }

    [ObservableProperty] public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetSuffix), nameof(IsDaily), nameof(IsWeekly))]
    public partial Recurrence Recurrence { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompletion), nameof(IsQuantity), nameof(IsDuration))]
    public partial Measure Measure { get; set; }

    public bool IsMeasureLocked { get; }
    public string MeasureLockedHint => "A medida não muda depois do primeiro registro.";

    // Atalhos para o segmento da medida; com a medida travada, não mudam nada.
    public bool IsCompletion { get => Measure == Measure.Completion; set => SetMeasure(value, Measure.Completion); }
    public bool IsQuantity { get => Measure == Measure.Quantity; set => SetMeasure(value, Measure.Quantity); }
    public bool IsDuration { get => Measure == Measure.Duration; set => SetMeasure(value, Measure.Duration); }

    private void SetMeasure(bool selected, Measure measure)
    {
        if (selected && !IsMeasureLocked)
            Measure = measure;
    }

    [ObservableProperty] public partial string Unit { get; set; }

    private decimal? _durationHours;
    private decimal? _durationMinutes;

    /// <summary>Horas do alvo de tempo (NumericUpDown: null quando apagado).</summary>
    public decimal? DurationHoursInput
    {
        get => _durationHours;
        set { if (SetProperty(ref _durationHours, value)) RevalidateIfSubmitted(); }
    }

    /// <summary>Minutos do alvo de tempo (0–59).</summary>
    public decimal? DurationMinutesInput
    {
        get => _durationMinutes;
        set { if (SetProperty(ref _durationMinutes, value)) RevalidateIfSubmitted(); }
    }

    /// <summary>Alvo da quantidade (o alvo do tempo vem de horas + minutos; o da conclusão é sempre 1).</summary>
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
    private decimal? _reminderHours;
    private decimal? _reminderMinutes;

    /// <summary>Horas do intervalo de lembrete (NumericUpDown: null quando apagado).</summary>
    public decimal? ReminderHoursInput
    {
        get => _reminderHours;
        set { if (SetProperty(ref _reminderHours, value)) RevalidateIfSubmitted(); }
    }

    /// <summary>Minutos do intervalo de lembrete (0–59).</summary>
    public decimal? ReminderMinutesInput
    {
        get => _reminderMinutes;
        set { if (SetProperty(ref _reminderMinutes, value)) RevalidateIfSubmitted(); }
    }
    [ObservableProperty] public partial TimeSpan? ActiveFrom { get; set; }
    [ObservableProperty] public partial TimeSpan? ActiveUntil { get; set; }
    [ObservableProperty] public partial DateTime? StartDate { get; set; }
    [ObservableProperty] public partial DateTime? EndDate { get; set; }
    [ObservableProperty] public partial string? GeneralError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool IsConfirmingDelete { get; set; }

    public string TargetSuffix => Recurrence == Recurrence.Weekly ? "por semana" : "por dia";

    // Atalhos para os botões de segmento da View.
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
    public string? UnitError => ErrorFor(nameof(GoalDraft.Unit));
    public string? MeasureError => ErrorFor(nameof(GoalDraft.Measure));

    public string? ErrorFor(string field) => _errors.GetValueOrDefault(field);

    [RelayCommand]
    private async Task SaveAsync()
    {
        _submitted = true;
        GeneralError = null;
        _serviceErrors.Clear();
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
            foreach (var error in ex.Errors)
                _serviceErrors.TryAdd(error.Field, error.Message);
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
        nameof(Title), nameof(Recurrence), nameof(Measure), nameof(Unit), nameof(Target), nameof(SelectedIcon), nameof(SelectedColor),
        nameof(ActiveFrom), nameof(ActiveUntil), nameof(StartDate), nameof(EndDate),
    ];

    private void OnInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not { } name || !Inputs.Contains(name))
            return;
        _serviceErrors.Remove(name);
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
        var durationMissing = DurationHoursInput is null && DurationMinutesInput is null;
        var intervalMissing = ReminderHoursInput is null && ReminderMinutesInput is null;
        var target = Measure switch
        {
            Measure.Completion => 1,
            Measure.Duration => (int)Math.Round((DurationHoursInput ?? 0) * 60 + (DurationMinutesInput ?? 0), MidpointRounding.AwayFromZero),
            _ => Target,
        };
        draft = new GoalDraft(
            Title, Recurrence, target, SelectedIcon.Key, SelectedColor.Color,
            (int)Math.Round((ReminderHoursInput ?? 0) * 60 + (ReminderMinutesInput ?? 0), MidpointRounding.AwayFromZero),
            TimeOnly.FromTimeSpan(ActiveFrom ?? TimeSpan.Zero),
            TimeOnly.FromTimeSpan(ActiveUntil ?? new TimeSpan(23, 59, 0)),
            Days.Where(d => d.IsSelected).Aggregate(WeekDays.None, (all, d) => all | d.Day),
            StartDate is { } s ? DateOnly.FromDateTime(s) : null,
            EndDate is { } e ? DateOnly.FromDateTime(e) : null,
            Measure,
            Measure == Measure.Quantity ? Unit : null);

        var errors = GoalValidator.Validate(draft).Select(x => (x.Field, x.Message)).ToList();
        if (ActiveFrom is null || ActiveUntil is null)
        {
            // Com um horário vazio, a comparação início < fim não faz sentido.
            errors.RemoveAll(x => x.Field == nameof(GoalDraft.ActiveUntil));
            if (ActiveFrom is null) errors.Add((nameof(GoalDraft.ActiveFrom), MissingTime));
            if (ActiveUntil is null) errors.Add((nameof(GoalDraft.ActiveUntil), MissingTime));
        }

        var targetMissing = Measure switch
        {
            Measure.Quantity => _targetMissing ? MissingTarget : null,
            Measure.Duration => durationMissing ? MissingDuration : null,
            _ => null,
        };
        if (intervalMissing)
        {
            errors.RemoveAll(x => x.Field == nameof(GoalDraft.ReminderEveryMinutes));
            errors.Add((nameof(GoalDraft.ReminderEveryMinutes), MissingInterval));
        }
        if (targetMissing is not null)
        {
            errors.RemoveAll(x => x.Field == nameof(GoalDraft.Target));
            errors.Add((nameof(GoalDraft.Target), targetMissing));
        }

        errors.AddRange(_serviceErrors.Select(x => (x.Key, x.Value)));
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
                     nameof(UnitError), nameof(MeasureError),
                 })
            OnPropertyChanged(name);
    }
}
