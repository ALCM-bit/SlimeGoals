using System.Diagnostics;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Regra da janelinha do cronômetro: ela fica à mostra quando a opção está ligada, há cronômetro rodando e a janela
/// principal não está à vista. Qualquer mudança num dos três reaplica a regra na thread da interface
/// (<paramref name="dispatch"/>). Sem cronômetro rodando, a janela nunca é pedida.
/// </summary>
public sealed class MiniTimerController(RunningTimerWatcher watcher, IMainWindowPresence main, IMiniTimerWindow window,
    AppSettings settings, DataChangeNotifier changes, Action<Action> dispatch) : IAppHostedService
{
    private readonly Lock _lock = new();
    private (int X, int Y)? _position;
    private volatile bool _enabled = true;
    private bool _shown; // só na thread da interface
    private int _started;

    public async Task StartAsync()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            return;
        watcher.Changed += OnInputChanged;
        main.Changed += OnInputChanged;
        changes.Changed += OnSettingsChanged;
        window.Moved += OnMoved;
        try
        {
            var position = await settings.GetMiniTimerPositionAsync();
            lock (_lock)
                _position = position;
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao ler a posição da janela do cronômetro: {TraceText.Of(e)}");
        }
        await ReloadEnabledAsync();
        await watcher.StartAsync();
        dispatch(Apply); // o watcher pode já estar iniciado (sem aviso novo): a regra vale desde já
    }

    public Task StopAsync()
    {
        if (Interlocked.CompareExchange(ref _started, 2, 1) != 1)
            return Task.CompletedTask;
        watcher.Changed -= OnInputChanged;
        main.Changed -= OnInputChanged;
        changes.Changed -= OnSettingsChanged;
        window.Moved -= OnMoved;
        dispatch(() =>
        {
            if (_shown)
                Set(false);
        });
        return Task.CompletedTask;
    }

    private void OnInputChanged(object? sender, EventArgs e) => dispatch(Apply);

    private void OnSettingsChanged(object? sender, EventArgs e) => _ = ReloadEnabledAsync();

    private async Task ReloadEnabledAsync()
    {
        try
        {
            _enabled = await settings.GetMiniTimerEnabledAsync();
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao ler a opção da janela do cronômetro: {TraceText.Of(e)}");
        }
        dispatch(Apply);
    }

    private void OnMoved(object? sender, (int X, int Y) position)
    {
        lock (_lock)
            _position = position;
        _ = SavePositionAsync(position);
    }

    private async Task SavePositionAsync((int X, int Y) position)
    {
        try
        {
            await settings.SetMiniTimerPositionAsync(position.X, position.Y);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao gravar a posição da janela do cronômetro: {TraceText.Of(e)}");
        }
    }

    private void Apply()
    {
        if (Volatile.Read(ref _started) != 1)
            return;
        var show = _enabled && watcher.Current is not null && !main.IsOnScreen;
        if (show != _shown)
            Set(show);
    }

    private void Set(bool show)
    {
        try
        {
            if (show)
            {
                (int X, int Y)? position;
                lock (_lock)
                    position = _position;
                window.Show(position);
            }
            else
                window.Hide();
            _shown = show;
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao {(show ? "mostrar" : "esconder")} a janela do cronômetro: {TraceText.Of(e)}");
        }
    }
}
