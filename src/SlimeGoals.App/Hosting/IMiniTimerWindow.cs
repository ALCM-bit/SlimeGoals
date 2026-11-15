namespace SlimeGoals.App.Hosting;

/// <summary>A janela pequena com o cronômetro rodando. Chamada só na thread da interface.</summary>
public interface IMiniTimerWindow
{
    /// <summary>Mostra na posição gravada; nula, no canto padrão. Não rouba o foco.</summary>
    void Show((int X, int Y)? position);

    void Hide();

    /// <summary>A pessoa arrastou a janela para outro lugar e soltou.</summary>
    event EventHandler<(int X, int Y)>? Moved;

    /// <summary>A pessoa pediu para esconder a janela (botão "−").</summary>
    event EventHandler? HideRequested;
}
