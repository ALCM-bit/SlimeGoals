using Avalonia.Layout;
using Avalonia.VisualTree;

namespace SlimeGoals.App.Views;

/// <summary>
/// Refaz o layout inteiro depois de uma passada sem superfície. No Android, com o app em segundo plano, o Avalonia 12 dá
/// à janela o tamanho de 1×1 px sem avisar a mudança (<c>InvalidationAwareSurfaceView.SurfaceDestroyed</c>); uma mudança
/// nesse meio-tempo (o bloco do Pomodoro que chega ao fim) mede a árvore com esse tamanho e arruma a barra de navegação e
/// os botões com altura 0. Na volta ele só mede de novo a raiz: os filhos recebem o mesmo retângulo de antes e não são
/// arrumados outra vez, e a tela fica cortada até uma rotação.
/// </summary>
public sealed class StaleLayoutRepair
{
    private bool _stale;

    /// <summary>
    /// Chamado a cada passada de layout. Uma passada que mediu <paramref name="shell"/> com até 1 unidade (o pixel da
    /// superfície que sumiu vale 1 unidade num aparelho de densidade 1) marca o layout como velho; a primeira passada com
    /// o tamanho de verdade depois dela invalida a árvore toda, para a próxima medir e arrumar tudo de novo.
    /// </summary>
    /// <returns>Se invalidou a árvore.</returns>
    public bool OnLayoutUpdated(Layoutable shell)
    {
        if (LayoutInformation.GetPreviousMeasureConstraint(shell) is not { } constraint)
            return false;
        if (constraint.Width <= 1 || constraint.Height <= 1)
        {
            _stale = true;
            return false;
        }
        if (!_stale)
            return false;
        _stale = false;
        foreach (var visual in shell.GetSelfAndVisualDescendants())
            (visual as Layoutable)?.InvalidateMeasure();
        return true;
    }
}
