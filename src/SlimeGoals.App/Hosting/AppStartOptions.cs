namespace SlimeGoals.App.Hosting;

/// <summary>Opções da linha de comando; Background = "--background" presente (abre sem mostrar a janela).</summary>
public sealed record AppStartOptions(bool Background)
{
    /// <summary>Argumento que pede para abrir sem mostrar a janela.</summary>
    public const string BackgroundArgument = "--background";

    public static AppStartOptions Parse(IReadOnlyList<string> args) =>
        new(args.Contains(BackgroundArgument, StringComparer.Ordinal));
}
