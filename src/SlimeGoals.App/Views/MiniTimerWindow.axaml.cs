using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Views;

/// <summary>Área útil de uma tela, em pixels, e se é a principal.</summary>
public readonly record struct MiniTimerScreen(PixelRect WorkingArea, bool IsPrimary);

/// <summary>
/// Janela pequena, sem borda, sempre por cima e sem foco, com o cronômetro rodando (spike
/// <c>specs/spikes/janelinha-cronometro.md</c>). Arrastar move; clicar sem arrastar abre o app.
/// </summary>
public partial class MiniTimerWindow : Window
{
    /// <summary>Distância do canto da tela na posição padrão, em pixels.</summary>
    public const int EdgeMargin = 24;

    /// <summary>Quanto o ponteiro anda, em DIPs, antes de um clique virar arrasto.</summary>
    private const double DragThreshold = 4;

    private readonly MiniTimerDrop _drop;
    private PointerPressedEventArgs? _press;
    private Point _pressPoint;

    public MiniTimerWindow()
    {
        InitializeComponent();
        _drop = new MiniTimerDrop(TimeProvider.System, p => Dispatcher.UIThread.Post(() => Moved?.Invoke(this, p)));
        PositionChanged += (_, e) => _drop.Moved((e.Point.X, e.Point.Y));
    }

    /// <summary>A pessoa arrastou e soltou a janela; a posição nova, em pixels.</summary>
    public event EventHandler<(int X, int Y)>? Moved;

    /// <summary>Mostra sem roubar o foco. A posição vale só se definida antes de <see cref="Window.Show()"/>.</summary>
    public void ShowAt((int X, int Y)? saved)
    {
        if (IsVisible)
            return;
        var scaling = Screens.Primary?.Scaling ?? 1;
        Measure(Size.Infinity);
        var size = DesiredSize;
        var pixels = ((int)Math.Ceiling(size.Width * scaling), (int)Math.Ceiling(size.Height * scaling));
        var screens = Screens.All.Select(s => new MiniTimerScreen(s.WorkingArea, s.IsPrimary)).ToList();
        var (x, y) = PlaceWithin(saved, pixels, screens);
        // A posição que o app mesmo define não é um arrasto da pessoa.
        _drop.Reset();
        Position = new PixelPoint(x, y);
        Show();
    }

    /// <summary>
    /// Onde pôr a janela: na posição gravada, puxada para dentro da tela que ela mais cobre; se ela não cobre
    /// tela nenhuma (ou não há posição), no canto inferior direito da área útil da tela principal.
    /// </summary>
    public static (int X, int Y) PlaceWithin((int X, int Y)? saved, (int Width, int Height) size, IReadOnlyList<MiniTimerScreen> screens)
    {
        if (screens.Count == 0)
            return saved ?? (0, 0);

        if (saved is { } s)
        {
            var window = new PixelRect(s.X, s.Y, size.Width, size.Height);
            var best = screens.MaxBy(screen => Area(screen.WorkingArea.Intersect(window)));
            if (Area(best.WorkingArea.Intersect(window)) > 0)
            {
                var area = best.WorkingArea;
                return (Clamp(s.X, area.X, area.Right - size.Width), Clamp(s.Y, area.Y, area.Bottom - size.Height));
            }
        }

        var home = screens.FirstOrDefault(screen => screen.IsPrimary, screens[0]).WorkingArea;
        return (Math.Max(home.X, home.Right - size.Width - EdgeMargin), Math.Max(home.Y, home.Bottom - size.Height - EdgeMargin));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _press = e;
        _pressPoint = e.GetPosition(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_press is not { } press)
            return;
        var delta = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;
        _press = null;
        _drop.Begin();
        BeginMoveDrag(press);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_press is null)
            return;
        _press = null;
        if (DataContext is MiniTimerViewModel vm)
            vm.OpenCommand.Execute(null);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (HidesInsteadOfClosing(e.CloseReason, e.IsProgrammatic))
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// Alt+F4 ou o menu da janela: a janelinha se esconde em vez de fechar, para poder aparecer de novo. Só o
    /// encerramento do app ou do sistema, ou um fechamento pelo código, fecha de verdade.
    /// </summary>
    public static bool HidesInsteadOfClosing(WindowCloseReason reason, bool isProgrammatic) =>
        !isProgrammatic && reason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown);

    private static long Area(PixelRect rect) => (long)rect.Width * rect.Height;

    // Janela maior que a tela: encosta no começo dela.
    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);
}
