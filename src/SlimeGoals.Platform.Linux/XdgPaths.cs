namespace SlimeGoals.Platform.Linux;

/// <summary>
/// Caminhos segundo a XDG Base Directory Specification (spec §8).
/// Ambiente e home entram como parâmetros para o teste não depender da máquina.
/// </summary>
public static class XdgPaths
{
    public const string AppFolder = "SlimeGoals";
    public const string DatabaseFile = "slimegoals.db";

    public static string DatabasePath(Func<string, string?> getEnv, string home)
    {
        var dataHome = getEnv("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
            dataHome = Path.Combine(home, ".local", "share");
        return Path.Combine(dataHome, AppFolder, DatabaseFile);
    }

    /// <summary>Pasta de autostart: <c>$XDG_CONFIG_HOME/autostart</c> ou <c>~/.config/autostart</c>.</summary>
    public static string AutostartDirectory(Func<string, string?> getEnv, string home)
    {
        var configHome = getEnv("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(home, ".config");
        return Path.Combine(configHome, "autostart");
    }

    /// <summary>
    /// Pasta de arquivos de execução (trava e socket da instância única):
    /// <c>$XDG_RUNTIME_DIR</c>, ou <c>{tempPath}/slimegoals-{userName}</c> se ele faltar ou estiver vazio.
    /// </summary>
    public static string RuntimeDirectory(Func<string, string?> getEnv, string userName, string tempPath)
    {
        var runtimeDir = getEnv("XDG_RUNTIME_DIR");
        return string.IsNullOrWhiteSpace(runtimeDir)
            ? Path.Combine(tempPath, $"slimegoals-{userName}")
            : runtimeDir;
    }
}
