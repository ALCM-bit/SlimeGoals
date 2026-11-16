using CommunityToolkit.Mvvm.ComponentModel;
using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Um bloco da sequência no preparo do Pomodoro (spec Pomodoro §4.1). O tempo se edita em horas e minutos, com a
/// mesma conta do editor do trecho; os comandos ficam no <see cref="PomodoroViewModel"/>.
/// </summary>
public sealed partial class FocusBlockViewModel : ObservableObject
{
    public FocusBlockViewModel(FocusBlock block)
    {
        Kind = block.Kind;
        (HoursInput, MinutesInput) = (block.Minutes / 60, block.Minutes % 60);
    }

    public FocusBlockKind Kind { get; }

    public bool IsFocus => Kind == FocusBlockKind.Focus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial decimal? HoursInput { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial decimal? MinutesInput { get; set; }

    [ObservableProperty] public partial bool IsEditing { get; set; }

    /// <summary>"40 min", "1 h" ou "1 h 15"; com os campos vazios, "0 min".</summary>
    public string Text => GoalFormatting.Duration(Minutes() ?? 0);

    // Limite só para o cast de decimal para int não estourar; o de verdade é o do FocusPlan.Validate.
    private const decimal MaxInput = 1_000_000;

    /// <summary>Minutos do bloco, ou null se horas e minutos estão vazios.</summary>
    public int? Minutes() =>
        HoursInput is null && MinutesInput is null
            ? null
            : (int)Math.Round(Math.Clamp((HoursInput ?? 0) * 60 + (MinutesInput ?? 0), -MaxInput, MaxInput),
                MidpointRounding.AwayFromZero);
}
