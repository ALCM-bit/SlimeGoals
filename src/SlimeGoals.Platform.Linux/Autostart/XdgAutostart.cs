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

    public bool IsEnabled()
    {
        if (!File.Exists(EntryPath)) return false;
        foreach (var raw in File.ReadLines(EntryPath))
        {
            var line = raw.Trim();
            if (line == "Hidden=true" || line == "X-GNOME-Autostart-enabled=false")
                return false;
        }
        return true;
    }

    public void Enable()
    {
        Directory.CreateDirectory(autostartDirectory);
        var content =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=SlimeGoals\n" +
            "Comment=Metas e lembretes\n" +
            $"Exec={QuoteExec(executablePath)} --background\n" +
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

    /// <summary>Aspas duplas e escape de <c>"</c>, <c>`</c>, <c>$</c> e <c>\</c> (Desktop Entry Spec).</summary>
    public static string QuoteExec(string path)
    {
        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in path)
        {
            if (c is '"' or '`' or '$' or '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
