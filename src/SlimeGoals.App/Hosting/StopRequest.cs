using System.Diagnostics;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// "Parar" de fora do app (janelinha do Linux, notificação do Android): congela o cronômetro e deixa o app em Hoje,
/// onde a pessoa confere os minutos e registra. Nunca lança: sem cronômetro, só abre Hoje.
/// </summary>
public static class StopRequest
{
    /// <summary>Chamado na thread da interface (muda a página atual).</summary>
    public static async Task RunAsync(TimerService timers, ShellViewModel shell)
    {
        try
        {
            await timers.StopAsync();
        }
        catch (GoalStateException)
        {
            // Nenhum cronômetro (parado e registrado, ou descartado, por outro caminho): só abre Hoje.
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao parar o cronômetro de fora do app: {TraceText.Of(e)}");
        }
        shell.CurrentPage = shell.Pages[0];
    }
}
