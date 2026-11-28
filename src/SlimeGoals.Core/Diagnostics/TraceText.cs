namespace SlimeGoals.Core.Diagnostics;

/// <summary>
/// O que uma falha pode deixar no Trace. O Trace pode acabar gravado (no logcat do Android, no journal quando a saída
/// está ligada), então a mensagem da exceção não entra: é nela que viajam títulos de metas, caminhos e respostas do
/// servidor. Para caçar um defeito em que o tipo e a pilha não bastam, o head liga a mensagem com
/// <see cref="IncludeMessages"/> quando a variável <see cref="DetailVariable"/> vale 1.
/// </summary>
public static class TraceText
{
    /// <summary>A variável de ambiente que, com o valor 1, faz o Trace gravar também a mensagem das exceções.</summary>
    public const string DetailVariable = "SLIMEGOALS_TRACE_DETALHE";

    private const int MaxDepth = 8;

    private static volatile bool s_includeMessages;

    /// <summary>Se o ambiente pede o detalhe: só o valor 1 liga, para um erro de digitação não ligar sem querer.</summary>
    public static bool DetailRequested(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment(DetailVariable)?.Trim() == "1";
    }

    /// <summary>Daqui em diante, <see cref="Of(Exception)"/> e <see cref="Types(Exception)"/> incluem a mensagem.</summary>
    public static void IncludeMessages() => s_includeMessages = true;

    /// <summary>Os tipos da cadeia de exceções, o status HTTP se houver, e a pilha de onde falhou.</summary>
    public static string Of(Exception e) => Of(e, s_includeMessages);

    /// <inheritdoc cref="Of(Exception)"/>
    /// <param name="e">A exceção.</param>
    /// <param name="withMessages">Inclui a mensagem de cada exceção da cadeia.</param>
    public static string Of(Exception e, bool withMessages)
    {
        var types = Types(e, withMessages);
        return e.StackTrace is { Length: > 0 } stack ? types + Environment.NewLine + stack : types;
    }

    /// <summary>Só os tipos da cadeia, para avisos de situações esperadas (linha inválida, sinal perdido).</summary>
    public static string Types(Exception e) => Types(e, s_includeMessages);

    /// <inheritdoc cref="Types(Exception)"/>
    /// <param name="e">A exceção.</param>
    /// <param name="withMessages">Inclui a mensagem de cada exceção da cadeia.</param>
    public static string Types(Exception e, bool withMessages)
    {
        ArgumentNullException.ThrowIfNull(e);
        var names = new List<string>();
        for (var current = e; current is not null && names.Count < MaxDepth; current = current.InnerException)
            names.Add(withMessages ? $"{Name(current)}: {current.Message}" : Name(current));
        return string.Join(" <- ", names);
    }

    private static string Name(Exception e) => e is HttpRequestException { StatusCode: { } status }
        ? $"{e.GetType().Name} (HTTP {(int)status})"
        : e.GetType().Name;
}
