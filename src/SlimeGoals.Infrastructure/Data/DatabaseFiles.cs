using System.Diagnostics;
using SlimeGoals.Core.Diagnostics;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>
/// Deixa o banco e os arquivos ao lado dele (WAL, cópias antigas) legíveis só pelo dono. A pasta de dados já é
/// privada; isto é a segunda barreira, para o caso de a pasta ser aberta ou o arquivo ser copiado para outro lugar.
/// </summary>
internal static class DatabaseFiles
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void RestrictToOwner(string databasePath)
    {
        // Banco em memória não tem arquivo; no Windows a permissão é a da pasta do usuário.
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(databasePath) || !File.Exists(databasePath))
            return;

        var full = Path.GetFullPath(databasePath);
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(full)!, Path.GetFileName(full) + "*"))
        {
            try
            {
                if (File.GetUnixFileMode(file) != OwnerOnly)
                    File.SetUnixFileMode(file, OwnerOnly);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Não impede o app de abrir: a pasta privada continua valendo.
                Trace.TraceWarning($"Não consegui restringir um arquivo do banco: {TraceText.Types(e)}");
            }
        }
    }
}
