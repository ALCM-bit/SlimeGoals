using System.Diagnostics;

namespace SlimeGoals.App.Account;

/// <summary>O pouco da área de transferência que o "Copiar" do código de recuperação usa; a View adapta a do Avalonia.</summary>
public interface ITextClipboard
{
    Task SetTextAsync(string text);

    Task<string?> TryGetTextAsync();

    Task ClearAsync();
}

/// <summary>
/// "Copiar" do código de recuperação: leva o código à área de transferência e, passado <see cref="ClearAfter"/>, a esvazia
/// se ela ainda tiver o mesmo código. O que a pessoa copiou depois fica onde está.
/// </summary>
public sealed class RecoveryCodeClipboard(TimeProvider time)
{
    public static readonly TimeSpan ClearAfter = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Copia o código e termina depois da limpeza. A cópia pode lançar (a View avisa no Trace); a limpeza nunca lança.
    /// Os awaits voltam ao contexto de quem chamou: na View, a thread da interface, onde a área de transferência vive.
    /// </summary>
    public async Task CopyAsync(ITextClipboard clipboard, string code)
    {
        await clipboard.SetTextAsync(code);
        await Task.Delay(ClearAfter, time);
        try
        {
            if (await clipboard.TryGetTextAsync() == code)
                await clipboard.ClearAsync();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Falha ao limpar o código de recuperação copiado: {ex.GetType().FullName}");
        }
    }
}
