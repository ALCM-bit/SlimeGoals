using System.Diagnostics;

namespace SlimeGoals.Platform.Linux;

/// <summary>Pasta que só o dono acessa (0700). Protege também os arquivos dentro dela, inclusive os temporários do SQLite.</summary>
public static class PrivateDirectory
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Cria a pasta em 0700 ou, se ela já existe, ajusta o modo (instalações antigas nasceram em 0755).</summary>
    public static void Ensure(string path)
    {
        // Este projeto só roda no Linux; a guarda satisfaz o analisador de plataforma (CA1416).
        if (OperatingSystem.IsWindows())
            return;

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path, OwnerOnly);
        else if (File.GetUnixFileMode(path) != OwnerOnly)
            File.SetUnixFileMode(path, OwnerOnly);
    }

    /// <summary>Como <see cref="Ensure"/>, mas uma falha só vai para o Trace: o app abre mesmo assim.</summary>
    public static void TryEnsure(string path)
    {
        try
        {
            Ensure(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"Falha ao restringir a pasta {path}: {ex}");
        }
    }
}
