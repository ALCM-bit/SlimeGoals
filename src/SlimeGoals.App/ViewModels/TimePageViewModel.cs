using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Aba Tempo (spec Pomodoro §4.1), com a subaba Pomodoro. Abrir a aba recarrega a sessão e cala o som do aviso de fim
/// de bloco, que fica na central (§4.5). Sem <see cref="FocusAlerts"/> registrado, só recarrega.
/// </summary>
public sealed class TimePageViewModel(PomodoroViewModel pomodoro, FocusAlerts? alerts = null) : PageViewModel
{
    public override string Title => "Tempo";

    public override string IconKey => "NavTime";

    public IReadOnlyList<string> Tabs { get; } = ["Pomodoro"];

    public PomodoroViewModel Pomodoro => pomodoro;

    public override void Activate()
    {
        pomodoro.LoadCommand.Execute(null);
        alerts?.Silence();
    }

    /// <summary>Voltar do sistema: fecha o editor de meta, a conferência ou a confirmação abertos no Pomodoro.</summary>
    public override bool TryGoBack() => pomodoro.TryGoBack();
}
