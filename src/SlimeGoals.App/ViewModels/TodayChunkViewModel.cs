using CommunityToolkit.Mvvm.ComponentModel;
using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Uma linha de "Hoje · N min" no cartão de uma meta de tempo: um registro do dia, ou o trecho em andamento do cronômetro
/// (só para ver). O editor (minutos e nome) abre no lugar da linha (specs/Tarefas_no_cronometro_v1.md §7.2).
/// </summary>
public sealed partial class TodayChunkViewModel : ObservableObject
{
    private const string NoName = "Sem nome";

    private TodayChunkViewModel(Guid goalId, Guid? checkinId, int amount, string? note, bool isRunning, bool canEdit, string text)
    {
        GoalId = goalId;
        CheckinId = checkinId;
        Amount = amount;
        Note = note;
        Name = note ?? NoName;
        IsRunning = isRunning;
        CanEdit = canEdit;
        Text = text;
    }

    /// <summary>Um registro do dia. Compensação (junção de aparelhos) aparece, mas não se ajusta.</summary>
    public static TodayChunkViewModel Of(Checkin checkin) =>
        new(checkin.GoalId, checkin.Id, checkin.Amount, checkin.Note, isRunning: false,
            canEdit: checkin.Compensation is null, GoalFormatting.Duration(checkin.Amount));

    /// <summary>O trecho do cronômetro rodando: ainda não é registro, então não se ajusta.</summary>
    public static TodayChunkViewModel Running(Guid goalId, string? task) =>
        new(goalId, checkinId: null, amount: 0, task, isRunning: true, canEdit: false, text: "");

    public Guid GoalId { get; }
    /// <summary>Nulo no trecho em andamento.</summary>
    public Guid? CheckinId { get; }
    public int Amount { get; }
    public string? Note { get; }
    public string Name { get; }
    public bool IsRunning { get; }
    public bool CanEdit { get; }

    [ObservableProperty] public partial string Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEditButton))]
    public partial bool IsEditing { get; set; }

    [ObservableProperty] public partial decimal? HoursInput { get; set; }
    [ObservableProperty] public partial decimal? MinutesInput { get; set; }
    [ObservableProperty] public partial string? NoteInput { get; set; }
    [ObservableProperty] public partial string? Error { get; set; }

    public bool ShowsEditButton => CanEdit && !IsEditing;

    /// <summary>Abre o editor com o tempo atual em horas e minutos (sem sinal: o sinal do registro se mantém) e o nome.</summary>
    public void OpenEditor()
    {
        if (!CanEdit)
            return;
        (HoursInput, MinutesInput) = (Math.Abs(Amount) / 3600, Math.Abs(Amount) % 3600 / 60);
        NoteInput = Note;
        Error = null;
        IsEditing = true;
    }

    public void CloseEditor()
    {
        IsEditing = false;
        Error = null;
    }

    /// <summary>Total em minutos, com a mesma conta do painel de registro, ou null se horas e minutos estão vazios.</summary>
    public int? Minutes() =>
        HoursInput is null && MinutesInput is null
            ? null
            : (int)Math.Round((HoursInput ?? 0) * 60 + (MinutesInput ?? 0), MidpointRounding.AwayFromZero);
}
