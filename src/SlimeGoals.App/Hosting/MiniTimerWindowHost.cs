using SlimeGoals.App.ViewModels;
using SlimeGoals.App.Views;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// <see cref="IMiniTimerWindow"/> do desktop. A <see cref="MiniTimerWindow"/> e o ViewModel dela só são criados no
/// primeiro <see cref="Show"/>: sem cronômetro rodando, o app não ganha janela nenhuma.
/// </summary>
public sealed class MiniTimerWindowHost(Func<MiniTimerViewModel> viewModel) : IMiniTimerWindow
{
    private MiniTimerWindow? _window;

    public event EventHandler<(int X, int Y)>? Moved;

    public void Show((int X, int Y)? position)
    {
        if (_window is null)
        {
            _window = new MiniTimerWindow { DataContext = viewModel() };
            _window.Moved += (_, p) => Moved?.Invoke(this, p);
        }
        _window.ShowAt(position);
    }

    public void Hide() => _window?.Hide();
}
