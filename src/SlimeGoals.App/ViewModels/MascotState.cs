namespace SlimeGoals.App.ViewModels;

/// <summary>Pose do slime na tela Hoje.</summary>
public enum MascotPose { Sleeping, Idle, Happy, Celebrating }

/// <summary>Pose e frase do slime na tela Hoje (decisões 1 e 2 do plano da Fase 8).</summary>
public sealed record MascotState(MascotPose Pose, string Message)
{
    /// <summary>Tudo feito comemora (mesmo pausado); sem metas ou pausado dorme; senão depende de haver progresso.</summary>
    public static MascotState For(int total, int done, bool paused) => (total, done, paused) switch
    {
        ( > 0, _, _) when done >= total => new(MascotPose.Celebrating, "Tudo feito hoje!"),
        (0, _, _) or (_, _, true) => new(MascotPose.Sleeping, "Zzz…"),
        (_, 0, _) => new(MascotPose.Idle, "Vamos começar?"),
        _ when total - done == 1 => new(MascotPose.Happy, "Falta pouco!"),
        _ => new(MascotPose.Happy, "Continue assim!"),
    };
}
