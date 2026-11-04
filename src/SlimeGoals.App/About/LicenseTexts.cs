namespace SlimeGoals.App.About;

/// <summary>Lê os textos de licença embutidos no assembly do App.</summary>
public static class LicenseTexts
{
    private const string Prefix = "SlimeGoals.Licenses.";

    /// <summary>Lê o recurso embutido <c>SlimeGoals.Licenses.&lt;file&gt;</c>; lança <see cref="FileNotFoundException"/> se não existir.</summary>
    public static string Read(string file)
    {
        using var stream = typeof(LicenseTexts).Assembly.GetManifestResourceStream(Prefix + file)
            ?? throw new FileNotFoundException($"Texto de licença não encontrado: {file}", file);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
