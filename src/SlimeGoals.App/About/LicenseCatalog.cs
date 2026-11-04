namespace SlimeGoals.App.About;

/// <summary>Um componente com a sua licença. <c>Where</c>: "Linux e Android", "Linux" ou "Android".</summary>
public sealed record LicenseEntry(string Component, string License, string File, string Where);

/// <summary>Componentes distribuídos com o app e o texto de licença de cada um (segue THIRD-PARTY-NOTICES.md).</summary>
public static class LicenseCatalog
{
    private const string Both = "Linux e Android";
    private const string Linux = "Linux";
    private const string Android = "Android";

    private const string RuntimeMit = "MIT-dotnet-runtime-and-Microsoft.Extensions.txt";

    /// <summary>O primeiro item é o próprio SlimeGoals.</summary>
    public static IReadOnlyList<LicenseEntry> All { get; } =
    [
        new("SlimeGoals", "PolyForm Noncommercial 1.0.0", "LICENSE.md", Both),
        new("Fonte Figtree", "SIL OFL 1.1", "OFL-1.1-Figtree.txt", Both),
        new("Ícones do Lucide", "ISC e MIT (Feather)", "ISC-Lucide.txt", Both),
        new("Runtime .NET", "MIT", RuntimeMit, Both),
        new("Runtime .NET (avisos de terceiros)", "Licenças variadas", "dotnet-runtime-THIRD-PARTY-NOTICES.txt", Both),
        new("Avalonia", "MIT", "MIT-Avalonia.txt", Both),
        new("SkiaSharp e HarfBuzzSharp", "MIT", "MIT-SkiaSharp-HarfBuzzSharp.txt", Both),
        new("Skia", "BSD-3-Clause", "BSD-3-Clause-skia.txt", Both),
        new("HarfBuzz", "Old MIT", "HarfBuzz-Old-MIT.txt", Both),
        new("Bibliotecas embutidas no Skia e no HarfBuzz", "Licenças variadas", "SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt", Both),
        new("CommunityToolkit.Mvvm", "MIT", "MIT-CommunityToolkit.txt", Both),
        new("Microsoft.Extensions.DependencyInjection", "MIT", RuntimeMit, Both),
        new("Microsoft.Data.Sqlite", "MIT", "MIT-Microsoft.Data.Sqlite.txt", Both),
        new("Dapper", "Apache-2.0", "Apache-2.0.txt", Both),
        new("SQLitePCLRaw", "Apache-2.0", "Apache-2.0.txt", Both),
        new("Dapper e SQLitePCLRaw (aviso NOTICE)", "Apache-2.0", "Apache-2.0-NOTICE.txt", Both),
        new("SQLite", "Domínio público", "SQLite-public-domain.txt", Both),
        new("Tmds.DBus.Protocol", "MIT", "MIT-Tmds.DBus.Protocol.txt", Linux),
        new("Avalonia.Android", "MIT", "MIT-Avalonia.txt", Android),
        new(".NET para Android", "MIT", RuntimeMit, Android),
        new(".NET para Android (avisos de terceiros)", "Licenças variadas", "dotnet-android-THIRD-PARTY-NOTICES.txt", Android),
        new("Bindings Xamarin (AndroidX, Kotlin e outros)", "MIT", "MIT-Xamarin-bindings.txt", Android),
        new("AndroidX", "Apache-2.0", "Apache-2.0.txt", Android),
        new("Kotlin e kotlinx.coroutines", "Apache-2.0", "Apache-2.0.txt", Android),
        new("Guava ListenableFuture", "Apache-2.0", "Apache-2.0.txt", Android),
        new("JSpecify", "Apache-2.0", "Apache-2.0.txt", Android),
        new("JetBrains Annotations", "Apache-2.0", "Apache-2.0.txt", Android),
    ];
}
