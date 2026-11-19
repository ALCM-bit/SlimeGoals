namespace SlimeGoals.Server.Tests;

/// <summary>
/// Regra de dependência do servidor: ele só conhece os contratos da sincronização. O domínio, o banco local e as telas
/// do app ficam do outro lado da rede; o servidor guarda registros que não consegue ler.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] References =
        [.. typeof(Program).Assembly.GetReferencedAssemblies().Select(a => a.Name!)];

    [Fact]
    public void Server_references_only_the_sync_contracts()
    {
        References.Where(r => r.StartsWith("SlimeGoals.", StringComparison.Ordinal))
            .Should().BeSubsetOf(["SlimeGoals.Sync.Contracts"]);
    }

    [Fact]
    public void Server_does_not_reference_app_ui_local_database_or_platform_packages()
    {
        string[] forbidden = ["Avalonia", "CommunityToolkit", "Microsoft.Data.Sqlite", "Tmds", "Mono.Android"];
        References.Should().NotContain(r => forbidden.Any(f => r.StartsWith(f, StringComparison.Ordinal)));
    }
}
