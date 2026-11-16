using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Janelinha do cronômetro: a meta, o tempo correndo segundo a segundo e o botão de parar. O relógio só bate
/// enquanto há cronômetro rodando. Criado na thread da interface; avisos de outras threads voltam para ela.
/// </summary>
public sealed partial class MiniTimerViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly RunningTimerWatcher _watcher;
    private readonly TimerService _timers;
    private readonly IWindowActivator _window;
    private readonly ShellViewModel _shell;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _ui;
    private RunningTimer? _timer;
    private ITimer? _tick;

    public MiniTimerViewModel(RunningTimerWatcher watcher, TimerService timers, IWindowActivator window, ShellViewModel shell,
        TimeProvider time)
    {
        _watcher = watcher;
        _timers = timers;
        _window = window;
        _shell = shell;
        _time = time;
        _ui = SynchronizationContext.Current;
        watcher.Changed += OnWatcherChanged;
        Update();
    }

    /// <summary>Nome da meta.</summary>
    [ObservableProperty] public partial string Title { get; private set; } = "";

    [ObservableProperty] public partial string IconKey { get; private set; } = "";

    [ObservableProperty] public partial GoalColor Color { get; private set; }

    /// <summary>Tempo desde o início, como "0:23:51".</summary>
    [ObservableProperty] public partial string Elapsed { get; private set; } = GoalFormatting.Elapsed(TimeSpan.Zero);

    public void Dispose()
    {
        _watcher.Changed -= OnWatcherChanged;
        _tick?.Dispose();
    }

    /// <summary>Clique na janelinha: mostra a janela principal.</summary>
    [RelayCommand]
    private void Open() => _window.Activate();

    /// <summary>Congela o cronômetro e abre a janela principal em Hoje, para conferir e registrar.</summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        await StopRequest.RunAsync(_timers, _shell);
        _window.Activate();
    }

    private void OnWatcherChanged(object? sender, EventArgs e) => OnUi(Update);

    private void Update()
    {
        _timer = _watcher.Current;
        if (_timer is null)
        {
            _tick?.Dispose();
            _tick = null;
            return;
        }
        Title = _timer.Label;
        IconKey = _timer.Icon;
        Color = _timer.Color;
        RefreshElapsed();
        _tick ??= _time.CreateTimer(_ => OnUi(RefreshElapsed), null, Second, Second);
    }

    private void RefreshElapsed()
    {
        // Relógio do sistema atrasado em relação ao início gravado: mostra zero, não um tempo negativo.
        if (_timer is { } timer)
            Elapsed = GoalFormatting.Elapsed(TimeSpan.FromTicks(Math.Max(0, (_time.GetUtcNow() - timer.StartedAt).Ticks)));
    }

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            action();
        else
            _ui.Post(_ => action(), null);
    }
}
