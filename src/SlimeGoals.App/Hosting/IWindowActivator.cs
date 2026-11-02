namespace SlimeGoals.App.Hosting;

/// <summary>Traz a janela principal para a frente. Pode ser chamado de qualquer thread.</summary>
public interface IWindowActivator
{
    /// <summary>Mostra e ativa a janela (restaurando se minimizada), na thread da interface.</summary>
    void Activate();
}
