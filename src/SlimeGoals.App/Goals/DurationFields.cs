namespace SlimeGoals.App.Goals;

/// <summary>
/// Os campos h · min · s dos editores de tempo medido (ajuste de trecho, cronômetro parado, conferência do Pomodoro):
/// separar um tempo em segundos nos três campos e juntar de volta, sem perder nada.
/// </summary>
public static class DurationFields
{
    // Limite só para o cast de decimal para int não estourar; o de verdade é o do registro (24 h), no serviço.
    private const decimal MaxSeconds = 1_000_000_000;

    /// <summary>Horas, minutos (0 a 59) e segundos (0 a 59) de um tempo em segundos, sem sinal.</summary>
    public static (decimal Hours, decimal Minutes, decimal Seconds) Split(int seconds)
    {
        var abs = Math.Abs((long)seconds);
        return (abs / 3600, abs % 3600 / 60, abs % 60);
    }

    /// <summary>Total em segundos, ou null se os três campos estão vazios; qualquer um preenchido vale.</summary>
    public static int? Seconds(decimal? hours, decimal? minutes, decimal? seconds) =>
        hours is null && minutes is null && seconds is null
            ? null
            : (int)Math.Round(Math.Clamp(Clamp(hours) * 3600 + Clamp(minutes) * 60 + Clamp(seconds), -MaxSeconds, MaxSeconds),
                MidpointRounding.AwayFromZero);

    // Cada campo antes da conta, para a multiplicação em decimal também não estourar.
    private static decimal Clamp(decimal? value) => Math.Clamp(value ?? 0, -MaxSeconds, MaxSeconds);
}
