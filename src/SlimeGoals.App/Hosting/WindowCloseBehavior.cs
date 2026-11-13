using System.Diagnostics;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Escolha "ao fechar a janela" em cache, para o OnClosing (síncrono) decidir na hora.
/// Lê do <see cref="AppSettings"/> e recarrega a cada <see cref="DataChangeNotifier.Changed"/>.
/// Falha: registra em Trace e mantém o último valor (padrão <see cref="CloseAction.Hide"/>).
/// </summary>
public sealed class WindowCloseBehavior(AppSettings settings, DataChangeNotifier changes) : IAppHostedService
{
    private int _current = (int)CloseAction.Hide;
    private int _subscribed;

    /// <summary>Valor atual; seguro de ler de qualquer thread.</summary>
    public CloseAction Current => (CloseAction)Volatile.Read(ref _current);

    public async Task StartAsync()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
            changes.Changed += OnChanged;
        await ReloadAsync().ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        if (Interlocked.Exchange(ref _subscribed, 0) == 1)
            changes.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged(object? sender, EventArgs e) => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        try
        {
            var action = await settings.GetCloseActionAsync().ConfigureAwait(false);
            Volatile.Write(ref _current, (int)action);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao ler a ação ao fechar a janela; mantendo {Current}: {TraceText.Of(e)}");
        }
    }
}
