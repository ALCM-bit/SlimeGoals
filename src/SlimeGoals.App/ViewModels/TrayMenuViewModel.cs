using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Menu da bandeja: "Abrir", "Pausar lembretes"/"Retomar lembretes" e "Sair". O texto da pausa
/// acompanha o <see cref="DataChangeNotifier"/>, então reflete também a pausa feita na tela Hoje.
/// </summary>
public sealed partial class TrayMenuViewModel : ObservableObject
{
    private const string PauseLabel = "Pausar lembretes";
    private const string ResumeLabel = "Retomar lembretes";

    private readonly IWindowActivator _window;
    private readonly ReminderPause _pause;
    private readonly IAppLifetime _lifetime;
    private readonly SynchronizationContext? _ui;
    private int _refreshVersion;

    public TrayMenuViewModel(IWindowActivator window, ReminderPause pause, IAppLifetime lifetime, DataChangeNotifier changes)
    {
        _window = window;
        _pause = pause;
        _lifetime = lifetime;
        // Criado pela App na thread da interface: o aviso pode chegar de qualquer thread e volta para ela.
        _ui = SynchronizationContext.Current;
        changes.Changed += (_, _) => OnUi(() => _ = RefreshAsync());
    }

    /// <summary>"Pausar lembretes" ou "Retomar lembretes", conforme a pausa gravada.</summary>
    [ObservableProperty] public partial string PauseText { get; private set; } = PauseLabel;

    /// <summary>Lê a pausa e atualiza <see cref="PauseText"/>. Nunca lança: falhas vão para o Trace.</summary>
    public async Task RefreshAsync()
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        try
        {
            var paused = (await _pause.GetAsync()).IsPaused;
            if (version == Volatile.Read(ref _refreshVersion))
                OnUi(() => PauseText = paused ? ResumeLabel : PauseLabel);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao ler a pausa dos lembretes para a bandeja: {TraceText.Of(e)}");
        }
    }

    [RelayCommand]
    private void Open() => _window.Activate();

    [RelayCommand]
    private async Task TogglePauseAsync()
    {
        try
        {
            if ((await _pause.GetAsync()).IsPaused)
                await _pause.ResumeAsync();
            else
                await _pause.PauseAsync();
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao pausar/retomar os lembretes pela bandeja: {TraceText.Of(e)}");
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private void Quit() => _lifetime.Quit();

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            action();
        else
            _ui.Post(_ => action(), null);
    }
}
