using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

/// <summary>
/// O teclado virtual do Android visto pelas telas. O Avalonia grava o estado do teclado a cada entrega das áreas da
/// janela, mas só avisa a troca quando o teclado anima. Fechado sem animação (o app foi para segundo plano com o teclado
/// aberto, e ele sumiu), o estado muda calado, e o único aviso é o das áreas da janela, que chega antes de o estado novo
/// ser gravado. O teclado é simulado à mão; a fila do dispatcher também.
/// </summary>
public class KeyboardWatchTests
{
    private sealed class Pane
    {
        public bool Open { get; set; }
        public double Covered { get; set; }
        public int Changes { get; private set; }
        public KeyboardWatch Watch { get; }
        private readonly List<Action> _queued = [];

        public Pane()
        {
            Watch = new KeyboardWatch(() => Open, () => Covered, _queued.Add);
            Watch.Changed += (_, _) => Changes++;
        }

        /// <summary>O dispatcher roda o que ficou na fila, depois de o Avalonia terminar a entrega.</summary>
        public void RunQueued()
        {
            var queued = _queued.ToArray();
            _queued.Clear();
            foreach (var action in queued)
                action();
        }

        public static Pane Opened()
        {
            var pane = new Pane { Open = true, Covered = 312.38 };
            pane.Watch.Refresh();
            return pane;
        }
    }

    [Fact]
    public void Opening_reserves_the_height_the_keyboard_covers()
    {
        var pane = Pane.Opened();

        pane.Watch.IsOpen.Should().BeTrue();
        pane.Watch.Height.Should().Be(312.38);
        pane.Changes.Should().Be(1);
    }

    [Fact]
    public void A_keyboard_closed_without_animating_frees_the_space_on_the_next_insets_delivery()
    {
        var pane = Pane.Opened();

        // O aviso das áreas chega antes de o Avalonia gravar o estado novo: ali o teclado ainda consta aberto.
        pane.Watch.OnInsetsChanged();
        pane.Open = false;
        pane.Covered = 0;
        pane.RunQueued();

        pane.Watch.IsOpen.Should().BeFalse();
        pane.Watch.Height.Should().Be(0);
        pane.Changes.Should().Be(2);
    }

    [Fact]
    public void A_closed_keyboard_reserves_nothing_even_before_the_covered_area_updates()
    {
        var pane = Pane.Opened();

        pane.Open = false;
        pane.Watch.Refresh();

        pane.Watch.IsOpen.Should().BeFalse();
        pane.Watch.Height.Should().Be(0);
        pane.Changes.Should().Be(2);
    }

    [Fact]
    public void A_delivery_that_changes_nothing_raises_nothing()
    {
        var pane = Pane.Opened();

        pane.Watch.OnInsetsChanged();
        pane.RunQueued();
        pane.Watch.Refresh();

        pane.Changes.Should().Be(1);
    }

    [Fact]
    public void A_taller_keyboard_while_open_is_reported()
    {
        var pane = Pane.Opened();

        pane.Watch.OnInsetsChanged();
        pane.Covered = 360;
        pane.RunQueued();

        pane.Watch.IsOpen.Should().BeTrue();
        pane.Watch.Height.Should().Be(360);
        pane.Changes.Should().Be(2);
    }
}
