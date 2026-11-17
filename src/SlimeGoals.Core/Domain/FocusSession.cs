namespace SlimeGoals.Core.Domain;

/// <summary>Trecho de foco já fechado, pronto para virar registro numa meta.</summary>
public sealed record FocusChunk(Guid Id, int Seconds, string? Task, DateTimeOffset EndedAt, Guid? CheckinId);

/// <summary>
/// Sessão de Pomodoro aberta; no máximo uma em todo o app.
/// <see cref="CutAt"/> marca onde o foco ainda não registrado começa e <see cref="Carry"/> guarda a fração
/// de segundo que sobrou do corte anterior. <see cref="PausedAt"/> preenchido congela o relógio.
/// </summary>
public sealed record FocusSession(
    FocusPlan Plan,
    Guid? GoalId,
    DateTimeOffset StartedAt,
    int Step,
    DateTimeOffset BlockStartedAt,
    DateTimeOffset CutAt,
    TimeSpan Carry,
    DateTimeOffset? PausedAt,
    string? Task,
    DateTimeOffset? FinishedAt,
    int? AlertedStep,
    int Revision)
{
    public FocusBlock Block => Plan.BlockAt(Step);

    public bool IsLastStep => Step == Plan.TotalSteps - 1;
}
