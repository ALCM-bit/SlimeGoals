namespace SlimeGoals.Platform.Linux;

/// <summary>
/// Faz o Avalonia usar o XIM em vez do IME via DBus.
/// Com o IBus via DBus, a composição de teclas mortas se perde (ex.: ' + c → ç no layout US internacional);
/// pelo XIM (ibus-x11) ela funciona. Um valor já definido pelo usuário é respeitado.
/// Precisa rodar antes de o Avalonia inicializar a plataforma.
/// </summary>
public static class InputMethodEnvironment
{
    public const string Variable = "AVALONIA_IM_MODULE";

    public static void Apply(Func<string, string?> getEnv, Action<string, string> setEnv)
    {
        if (string.IsNullOrEmpty(getEnv(Variable)))
            setEnv(Variable, "none");
    }
}
