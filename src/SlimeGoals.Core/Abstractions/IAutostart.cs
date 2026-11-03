namespace SlimeGoals.Core.Abstractions;

/// <summary>Iniciar o app com a sessão do usuário (spec RF08). Implementado por plataforma.</summary>
public interface IAutostart
{
    bool IsEnabled();

    /// <summary>Grava/reescreve a entrada com o executável atual.</summary>
    void Enable();

    /// <summary>Remove a entrada (sem erro se não existe).</summary>
    void Disable();
}
