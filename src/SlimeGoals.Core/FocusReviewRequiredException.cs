namespace SlimeGoals.Core;

/// <summary>
/// O foco aberto passou dos 30 min de extra e não fecha sem a conferência do último trecho (spec Pomodoro §4.3). Um
/// <see cref="GoalStateException"/> com tipo próprio: a tela abre a conferência sem comparar o texto, e quem só conhece
/// a base mostra a mensagem.
/// </summary>
public sealed class FocusReviewRequiredException(Guid goalId)
    : GoalStateException(goalId, "Confira o tempo do último trecho antes de seguir.");
