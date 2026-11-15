using SlimeGoals.Core.Services;

namespace SlimeGoals.Core.Abstractions;

/// <summary>
/// Porta de saída da notificação fixa com o cronômetro rodando (implementada no Android).
/// Chamada de qualquer thread; as implementações não guardam estado além da notificação publicada.
/// </summary>
public interface ITimerNotification
{
    /// <summary>Publica a notificação, ou atualiza a que já está à mostra.</summary>
    void Show(RunningTimer timer);

    /// <summary>Retira a notificação; sem notificação publicada, não faz nada.</summary>
    void Cancel();
}
