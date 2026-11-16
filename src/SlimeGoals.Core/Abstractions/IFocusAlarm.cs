namespace SlimeGoals.Core.Abstractions;

/// <summary>
/// Alarme próprio do fim de bloco do Pomodoro, separado do alarme dos lembretes (Linux: no processo; Android:
/// <c>setAlarmClock</c>). Armar substitui o anterior. Chamado de qualquer thread.
/// </summary>
public interface IFocusAlarm
{
    /// <summary>Arma o alarme para o instante dado; um instante no passado dispara assim que possível.</summary>
    void Set(DateTimeOffset at);

    /// <summary>Cancela o alarme armado; sem alarme armado, não faz nada.</summary>
    void Cancel();
}
