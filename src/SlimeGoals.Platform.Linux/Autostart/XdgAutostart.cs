using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Platform.Linux.Autostart;

/// <summary>
/// Iniciar com o sistema pelo autostart XDG: um <c>slimegoals.desktop</c> na pasta de autostart.
/// O arquivo é a fonte da verdade.
/// </summary>
public sealed class XdgAutostart(string autostartDirectory, string executablePath) : IAutostart
{
    public const string FileName = "slimegoals.desktop";

    private string EntryPath => Path.Combine(autostartDirectory, FileName);

    private string ExecLine => $"Exec={QuoteExec(executablePath)} --background";

    public bool IsEnabled()
    {
        if (!File.Exists(EntryPath)) return false;
        var expectedExec = ExecLine;
        var execMatches = false;
        foreach (var raw in File.ReadLines(EntryPath))
        {
            var line = raw.Trim();
            if (line == "Hidden=true" || line == "X-GNOME-Autostart-enabled=false")
                return false;
            if (line.StartsWith("Exec=", StringComparison.Ordinal))
                execMatches = line == expectedExec;
        }
        // Entrada que aponta para outro executável (instalação movida) conta como desligada.
        return execMatches;
    }

    public void Enable()
    {
        Directory.CreateDirectory(autostartDirectory);
        var content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=SlimeGoals\n" +
            "Comment=Metas e lembretes\n" +
            ExecLine + "\n" +
            "Icon=slimegoals\n" +
            "Terminal=false\n" +
            "X-GNOME-Autostart-enabled=true\n";

        // Grava num temporário na mesma pasta e troca: uma queda não deixa entrada pela metade.
        var temp = Path.Combine(autostartDirectory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, EntryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void Disable()
    {
        if (File.Exists(EntryPath)) File.Delete(EntryPath);
    }

    /// <summary>
    /// Valor do <c>Exec=</c> como escrito no arquivo: o caminho entre aspas duplas. O Desktop Entry desfaz primeiro o
    /// escape de string (<c>\\</c> vira <c>\</c>) e só então aplica a regra de aspas do Exec; por isso <c>"</c>, <c>`</c>,
    /// <c>$</c> e <c>\</c> levam uma barra a mais no arquivo (<c>\\"</c>, <c>\\`</c>, <c>\\$</c>, <c>\\\\</c>) e <c>%</c> vira <c>%%</c>.
    /// </summary>
    public static string QuoteExec(string path)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in path)
        {
            switch (c)
            {
                case '"' or '`' or '$': sb.Append("\\\\").Append(c); break;
                case '\\': sb.Append("\\\\\\\\"); break;
                case '%': sb.Append("%%"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }
}
