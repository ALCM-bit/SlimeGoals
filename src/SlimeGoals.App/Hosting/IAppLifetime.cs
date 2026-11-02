namespace SlimeGoals.App.Hosting;

/// <summary>Encerra o app de verdade (fechar a janela só a oculta). Pode ser chamado de qualquer thread.</summary>
public interface IAppLifetime
{
    /// <summary>Pede o encerramento do processo, na thread da interface.</summary>
    void Quit();
}
