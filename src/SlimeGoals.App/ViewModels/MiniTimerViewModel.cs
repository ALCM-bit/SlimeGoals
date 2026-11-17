using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Janelinha do cronômetro: a meta, o tempo correndo segundo a segundo e o botão de parar. Na sessão de Pomodoro, a fase
/// e o tempo que falta do bloco, sem o botão de parar (a sessão termina na aba Tempo). O relógio só bate enquanto há
/// cronômetro ou sessão rodando. Criado na thread da interface; avisos de outras threads voltam para ela.
/// </summary>
public sealed partial class MiniTimerViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly RunningTimerWatcher _watcher;
    private readonly TimerService _timers;
    private readonly IWindowActivator _window;
    private readonly ShellViewModel _shell;
    private readonly PageNavigator _navigator;
    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _ui;
    private RunningTimer? _timer;
    private ITimer? _tick;

    public MiniTimerViewModel(RunningTimerWatcher watcher, TimerService timers, IWindowActivator window, ShellViewModel shell,
        PageNavigator navigator, TimeProvider time)
    {
        _watcher = watcher;
        _timers = timers;
        _window = window;
        _shell = shell;
        _navigator = navigator;
        _time = time;
        _ui = SynchronizationContext.Current;
        watcher.Changed += OnWatcherChanged;
        Update();
    }

    /// <summary>Nome da meta, e a tarefa; na sessão, a fase antes ("Foco · Estudar").</summary>
    [ObservableProperty] public partial string Title { get; private set; } = "";

    [ObservableProperty] public partial string IconKey { get; private set; } = "";

    [ObservableProperty] public partial GoalColor Color { get; private set; }

    /// <summary>
    /// Tempo desde o início, como "0:23:51". Na sessão, o que falta do bloco ("24:59", ou "1:15:00" com 1 h ou mais),
    /// parado enquanto pausada.
    /// </summary>
    [ObservableProperty] public partial string Elapsed { get; private set; } = GoalFormatting.Elapsed(TimeSpan.Zero);

    /// <summary>O botão de parar: só no cronômetro.</summary>
    [ObservableProperty] public partial bool IsStopVisible { get; private set; } = true;

    public void Dispose()
    {
        _watcher.Changed -= OnWatcherChanged;
        _tick?.Dispose();
    }

    /// <summary>Clique na janelinha: mostra a janela principal; na sessão, na aba Tempo.</summary>
    [RelayCommand]
    private void Open()
    {
        if (_timer?.Focus is not null)
            _navigator.Show<TimePageViewModel>();
        _window.Activate();
    }

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
        IsStopVisible = _timer.Focus is null;
        RefreshElapsed();
        _tick ??= _time.CreateTimer(_ => OnUi(RefreshElapsed), null, Second, Second);
    }

    private void RefreshElapsed()
    {
        if (_timer is not { } timer)
            return;
        if (timer.Focus is { } focus)
            Elapsed = GoalFormatting.Countdown(focus.PausedRemaining ?? focus.EndsAt - _time.GetUtcNow());
        else
            // Relógio do sistema atrasado em relação ao início gravado: mostra zero, não um tempo negativo.
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
