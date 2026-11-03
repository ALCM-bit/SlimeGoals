namespace SlimeGoals.Core.Abstractions;

/// <summary>Um único alarme do sistema, que sobrevive ao processo (Android: AlarmManager). Armar substitui o anterior.</summary>
public interface ISystemAlarm
{
    /// <summary>Arma o alarme para o instante dado; um instante no passado dispara assim que possível.</summary>
    void Set(DateTimeOffset at);

    /// <summary>Cancela o alarme armado, se houver.</summary>
    void Cancel();
}
