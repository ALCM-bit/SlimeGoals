namespace SlimeGoals.Core.Domain;

/// <summary>Contas de tempo de uma <see cref="FocusSession"/>; sem relógio próprio, o "agora" vem de fora.</summary>
public static class FocusClock
{
    /// <summary>Extra a partir do qual o último foco exige conferência.</summary>
    public static readonly TimeSpan ReviewThreshold = TimeSpan.FromMinutes(30);

    public static DateTimeOffset EndsAt(FocusSession s) => s.BlockStartedAt.AddMinutes(s.Block.Minutes);

    public static TimeSpan Remaining(FocusSession s, DateTimeOffset now) =>
        Max(EndsAt(s) - (s.PausedAt ?? now));

    public static TimeSpan Overtime(FocusSession s, DateTimeOffset now) =>
        Max((s.PausedAt ?? now) - EndsAt(s));

    public static bool IsWaiting(FocusSession s, DateTimeOffset now) =>
        s.FinishedAt is null && s.PausedAt is null && now >= EndsAt(s);

    /// <summary>Foco ainda não registrado: do corte até agora (ou até a pausa) mais o resto; numa pausa, só o resto.</summary>
    public static TimeSpan OpenFocus(FocusSession s, DateTimeOffset now) =>
        s.Block.Kind == FocusBlockKind.Focus ? (s.PausedAt ?? now) - s.CutAt + s.Carry : s.Carry;

    public static bool NeedsReview(FocusSession s, DateTimeOffset now) =>
        s.Block.Kind == FocusBlockKind.Focus && Overtime(s, now) >= ReviewThreshold;

    /// <summary>Minutos inteiros de um corte: no último, arredonda ao mais próximo; antes, trunca.</summary>
    public static int CutMinutes(TimeSpan open, bool last) =>
        last ? (int)Math.Round(open.TotalMinutes, MidpointRounding.AwayFromZero) : (int)Math.Floor(open.TotalMinutes);

    private static TimeSpan Max(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
}
