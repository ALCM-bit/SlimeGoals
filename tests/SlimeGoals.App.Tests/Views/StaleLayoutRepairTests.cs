using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

/// <summary>
/// No Android, com o app em segundo plano a superfície some e o Avalonia mede a árvore com 1×1 px; na volta ele só mede
/// de novo a raiz, e os filhos ficam com o arranjo daquela passada (a barra de navegação com altura 0). As passadas são
/// simuladas à mão, como o LayoutManager as faz: a raiz arrumada sempre no tamanho da janela, que não muda.
/// </summary>
public class StaleLayoutRepairTests
{
    private static readonly Size Full = new(411, 914);
    private static readonly Size Gone = new(0.38, 0.38);

    private sealed class Shell
    {
        // Tamanho vindo do conteúdo, como a barra de navegação: um Height fixo valeria mesmo medido com 1×1 px.
        public Border Nav { get; } = new() { Child = new Border { Height = 56 } };
        public Border Inner { get; } = new() { Height = 100 };
        public Border Content { get; }
        public DockPanel Panel { get; }
        public Decorator Root { get; }

        public Shell()
        {
            DockPanel.SetDock(Nav, Dock.Bottom);
            Content = new Border { Child = Inner };
            Panel = new DockPanel { Children = { Nav, Content } };
            Root = new Decorator { Child = Panel };
        }

        /// <summary>Uma mudança no conteúdo invalida o caminho dela até a raiz, como faz o LayoutManager.</summary>
        public void Grow()
        {
            Inner.Height = 120;
            Inner.InvalidateMeasure();
            Content.InvalidateMeasure();
            Panel.InvalidateMeasure();
            Root.InvalidateMeasure();
        }

        public void Pass(Size measureAt)
        {
            Root.Measure(measureAt);
            Root.Arrange(new Rect(Full));
        }

        /// <summary>
        /// A passada seguinte para o shell invalidado com a raiz válida: o LayoutManager mede e arruma o controle com a
        /// medida e o retângulo anteriores dele.
        /// </summary>
        public void PassFromInvalidated()
        {
            Panel.Measure(LayoutInformation.GetPreviousMeasureConstraint(Panel)!.Value);
            Panel.Arrange(LayoutInformation.GetPreviousArrangeBounds(Panel)!.Value);
        }
    }

    /// <summary>
    /// A janela visível; em segundo plano uma mudança invalida o caminho até a raiz e a passada mede com 1×1 px; na volta,
    /// só a raiz é invalidada (o que o Avalonia faz ao recriar a superfície).
    /// </summary>
    private static Shell ThroughBackgroundChange(StaleLayoutRepair? repair)
    {
        var shell = new Shell();
        shell.Pass(Full);
        repair?.OnLayoutUpdated(shell.Panel);

        shell.Grow();
        shell.Pass(Gone);
        repair?.OnLayoutUpdated(shell.Panel);

        shell.Root.InvalidateMeasure();
        shell.Pass(Full);
        if (repair?.OnLayoutUpdated(shell.Panel) == true)
            shell.PassFromInvalidated();
        return shell;
    }

    [Fact]
    public void Without_the_repair_the_avalonia_pass_on_return_keeps_the_collapsed_arrangement()
    {
        var shell = ThroughBackgroundChange(repair: null);

        shell.Nav.DesiredSize.Height.Should().Be(56);
        shell.Nav.Bounds.Height.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void After_a_pass_without_a_surface_the_next_real_pass_lays_out_the_whole_tree_again()
    {
        var shell = ThroughBackgroundChange(new StaleLayoutRepair());

        shell.Nav.Bounds.Should().Be(new Rect(0, Full.Height - 56, Full.Width, 56));
        shell.Inner.Bounds.Height.Should().Be(120);
    }

    /// <summary>Num aparelho mdpi (densidade 1), o pixel da superfície que sumiu mede exatamente 1 unidade.</summary>
    [Fact]
    public void A_pass_of_exactly_one_unit_also_counts_as_without_a_surface()
    {
        var repair = new StaleLayoutRepair();
        var shell = new Shell();
        shell.Root.InvalidateMeasure();
        shell.Pass(new Size(1, 1));
        repair.OnLayoutUpdated(shell.Panel).Should().BeFalse();
        shell.Root.InvalidateMeasure();
        shell.Pass(Full);

        repair.OnLayoutUpdated(shell.Panel).Should().BeTrue();
    }

    [Fact]
    public void Normal_passes_change_nothing()
    {
        var repair = new StaleLayoutRepair();
        var shell = new Shell();

        shell.Pass(Full);
        repair.OnLayoutUpdated(shell.Panel).Should().BeFalse();
        shell.Grow();
        shell.Pass(Full);
        repair.OnLayoutUpdated(shell.Panel).Should().BeFalse();
        shell.Panel.IsMeasureValid.Should().BeTrue();
    }

    [Fact]
    public void Repairs_once_per_return()
    {
        var repair = new StaleLayoutRepair();
        var shell = new Shell();
        shell.Root.InvalidateMeasure();
        shell.Pass(Gone);
        repair.OnLayoutUpdated(shell.Panel);
        shell.Root.InvalidateMeasure();
        shell.Pass(Full);

        repair.OnLayoutUpdated(shell.Panel).Should().BeTrue();
        shell.PassFromInvalidated();
        repair.OnLayoutUpdated(shell.Panel).Should().BeFalse();
    }
}
