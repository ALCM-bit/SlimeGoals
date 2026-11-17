namespace SlimeGoals.App.Hosting;

/// <summary>
/// O <c>Chronometer</c> do Android conta a partir de um instante no relógio "desde o boot"
/// (<c>SystemClock.elapsedRealtime</c>), e o cronômetro do app guarda o início na hora do dia. Converte um no outro.
/// </summary>
public static class ChronometerBase
{
    /// <summary>
    /// O instante, em ms do relógio desde o boot, em que o cronômetro marcaria zero. Início no futuro (relógio atrás)
    /// conta de zero, como na janelinha.
    /// </summary>
    public static long Of(DateTimeOffset startedAt, DateTimeOffset now, long bootClockMs)
    {
        var elapsed = now - startedAt;
        return elapsed <= TimeSpan.Zero ? bootClockMs : bootClockMs - (long)elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Para a contagem regressiva (<c>setChronometerCountDown</c>): o instante, em ms do relógio desde o boot, em que o
    /// cronômetro chega a zero. Fim que já passou fica em zero agora.
    /// </summary>
    public static long CountdownOf(DateTimeOffset endsAt, DateTimeOffset now, long bootClockMs)
    {
        var remaining = endsAt - now;
        return remaining <= TimeSpan.Zero ? bootClockMs : bootClockMs + (long)remaining.TotalMilliseconds;
    }
}
