namespace SlimeGoals.Core.Diagnostics;

/// <summary>
/// O que uma falha pode deixar no Trace. No Linux o Trace vai para o journal do sistema e fica gravado, então a
/// mensagem da exceção não entra: é nela que viajam títulos de metas, caminhos e respostas do servidor.
/// </summary>
public static class TraceText
{
    private const int MaxDepth = 8;

    /// <summary>Os tipos da cadeia de exceções, o status HTTP se houver, e a pilha de onde falhou.</summary>
    public static string Of(Exception e)
    {
        var types = Types(e);
        return e.StackTrace is { Length: > 0 } stack ? types + Environment.NewLine + stack : types;
    }

    /// <summary>Só os tipos da cadeia, para avisos de situações esperadas (linha inválida, sinal perdido).</summary>
    public static string Types(Exception e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var names = new List<string>();
        for (var current = e; current is not null && names.Count < MaxDepth; current = current.InnerException)
            names.Add(Name(current));
        return string.Join(" <- ", names);
    }

    private static string Name(Exception e) => e is HttpRequestException { StatusCode: { } status }
        ? $"{e.GetType().Name} (HTTP {(int)status})"
        : e.GetType().Name;
}
