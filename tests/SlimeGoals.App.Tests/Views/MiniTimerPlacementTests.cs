using Avalonia;
using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

/// <summary>Três telas de 1920×1080 lado a lado, a principal no meio, com a barra do GNOME (32 px) no alto.</summary>
public class MiniTimerPlacementTests
{
    private static readonly (int Width, int Height) Size = (280, 66);
    private static readonly MiniTimerScreen Left = new(new PixelRect(0, 32, 1920, 1048), false);
    private static readonly MiniTimerScreen Primary = new(new PixelRect(1920, 32, 1920, 1048), true);
    private static readonly MiniTimerScreen Right = new(new PixelRect(3840, 32, 1920, 1048), false);

    [Fact]
    public void Without_a_saved_position_it_goes_to_the_bottom_right_of_the_primary_screen() =>
        MiniTimerWindow.PlaceWithin(null, Size, [Left, Primary, Right]).Should().Be((3536, 990));

    [Fact]
    public void A_saved_position_inside_a_screen_is_kept() =>
        MiniTimerWindow.PlaceWithin((4000, 300), Size, [Left, Primary, Right]).Should().Be((4000, 300));

    [Fact]
    public void PlaceWithin_falls_back_when_the_saved_position_is_off_every_screen() =>
        // A tela da direita foi desligada: 5000, 300 não cai em tela nenhuma.
        MiniTimerWindow.PlaceWithin((5000, 300), Size, [Left, Primary]).Should().Be((3536, 990));

    [Fact]
    public void A_window_half_off_the_edge_is_pulled_back_into_its_screen() =>
        MiniTimerWindow.PlaceWithin((5700, 1060), Size, [Left, Primary, Right]).Should().Be((5760 - 280, 1080 - 66));

    [Fact]
    public void Without_a_primary_screen_the_first_one_is_used() =>
        MiniTimerWindow.PlaceWithin(null, Size, [Left with { IsPrimary = false }]).Should().Be((1920 - 280 - 24, 1080 - 66 - 24));

    [Fact]
    public void Without_screens_the_saved_position_or_the_origin_is_used()
    {
        MiniTimerWindow.PlaceWithin((10, 20), Size, []).Should().Be((10, 20));
        MiniTimerWindow.PlaceWithin(null, Size, []).Should().Be((0, 0));
    }
}
