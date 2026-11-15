namespace SlimeGoals.App.Hosting;

/// <summary>Se a janela principal está à vista: mostrada e não minimizada.</summary>
public interface IMainWindowPresence
{
    /// <summary>Visível e não minimizada. Lido na thread da interface.</summary>
    bool IsOnScreen { get; }

    /// <summary>Disparado quando <see cref="IsOnScreen"/> muda, na thread da interface.</summary>
    event EventHandler? Changed;
}
