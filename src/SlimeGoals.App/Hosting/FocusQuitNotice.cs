using System.Diagnostics;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// O "Sair" do Linux com uma sessão de Pomodoro guardada (spec Pomodoro §4.5): avisa que a sessão continua guardada e
/// que o aviso de fim de bloco só toca com o app aberto, e então encerra. Envolve o <see cref="IAppLifetime"/> de verdade,
/// então vale para a bandeja, o Ctrl+Q e o fechar que encerra. O aviso nunca segura o encerramento: falha ou demora
/// além de <see cref="NoticeTimeout"/>, o app sai assim mesmo.
/// </summary>
/// <param name="notify">Envia uma notificação comum (sem urgência crítica) com título e corpo.</param>
public sealed class FocusQuitNotice(IAppLifetime inner, FocusService focus,
    Func<string, string, CancellationToken, Task> notify, TimeProvider time) : IAppLifetime
{
    public const string Title = "Pomodoro guardado";
    public const string Body = "A sessão continua guardada, mas o aviso de fim de bloco só toca com o SlimeGoals aberto.";

    /// <summary>Quanto o "Sair" espera pelo aviso.</summary>
    public static readonly TimeSpan NoticeTimeout = TimeSpan.FromSeconds(2);

    private int _quitting;

    public void Quit() => _ = QuitAsync();

    /// <summary>Avisa, se há sessão, e encerra. Um segundo pedido no meio do aviso não avisa de novo.</summary>
    public async Task QuitAsync()
    {
        if (Interlocked.Exchange(ref _quitting, 1) == 1)
            return;
        try
        {
            if (await focus.GetAsync().ConfigureAwait(false) is not null)
            {
                using var timeout = new CancellationTokenSource(NoticeTimeout, time);
                await notify(Title, Body, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning($"O aviso do Pomodoro ao Sair não saiu em {NoticeTimeout.TotalSeconds} s.");
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao avisar do Pomodoro guardado ao Sair: {TraceText.Of(e)}");
        }
        finally
        {
            Volatile.Write(ref _quitting, 0);
            inner.Quit();
        }
    }
}
