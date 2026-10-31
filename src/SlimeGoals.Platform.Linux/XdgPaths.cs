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
}
