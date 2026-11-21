namespace SlimeGoals.Core.Domain;

/// <summary>
/// Textos que substituem as notificações na tela de bloqueio quando o sistema esconde o conteúdo sensível: dizem o que
/// é a notificação sem dizer qual meta ou tarefa. Quem decide se a versão escondida aparece é a configuração do sistema,
/// não o app.
/// </summary>
public static class LockScreenText
{
    /// <summary>Título do lembrete na tela de bloqueio.</summary>
    public const string ReminderTitle = "Lembrete";

    /// <summary>Corpo do lembrete na tela de bloqueio.</summary>
    public const string ReminderBody = "Desbloqueie para ver a meta.";

    /// <summary>Título do cronômetro rodando na tela de bloqueio (a sessão de Pomodoro mostra a fase).</summary>
    public const string Timer = "Cronômetro";
}
