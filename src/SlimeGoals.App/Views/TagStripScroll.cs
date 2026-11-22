namespace SlimeGoals.App.Views;

/// <summary>Contas da rolagem lateral da linha de filtros do Hoje (seta "›" e esmaecido na ponta).</summary>
public static class TagStripScroll
{
    public static bool CanScrollRight(double offset, double extent, double viewport) => extent - viewport - offset > 1;

    /// <summary>Um passo de cerca de uma página (80% da janela), sem passar do fim.</summary>
    public static double NextOffset(double offset, double extent, double viewport) =>
        Math.Clamp(offset + viewport * 0.8, 0, Math.Max(0, extent - viewport));
}
