namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Uma bolinha da rodada atual do Pomodoro (spec Pomodoro §4.2): os blocos feitos cheios, o atual destacado e as pausas
/// com outra forma ou cor.
/// </summary>
public sealed record FocusDotViewModel(bool IsFocus, bool IsDone, bool IsCurrent);
