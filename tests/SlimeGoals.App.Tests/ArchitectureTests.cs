using SlimeGoals.App.Hosting;

namespace SlimeGoals.App.Tests;

/// <summary>
/// Regra de dependência (spec §5): o App tem as telas e conversa com o resto pelas portas do Core e pelo motor de
/// sincronização; dos contratos, usa os limites da conta que o servidor também aplica. Banco, D-Bus, Android e servidor
/// ficam nos adaptadores e nos heads.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] References =
        [.. typeof(PlatformCapabilities).Assembly.GetReferencedAssemblies().Select(a => a.Name!)];

    [Fact]
    public void App_references_only_the_core_and_the_sync_projects()
    {
        References.Where(r => r.StartsWith("SlimeGoals.", StringComparison.Ordinal))
            .Should().BeSubsetOf(["SlimeGoals.Core", "SlimeGoals.Sync", "SlimeGoals.Sync.Contracts"]);
    }

    [Fact]
    public void App_does_not_reference_database_platform_or_server_packages()
    {
        string[] forbidden = ["Dapper", "Microsoft.Data.Sqlite", "Tmds", "Mono.Android", "Npgsql", "Microsoft.AspNetCore", "MailKit"];
        References.Should().NotContain(r => forbidden.Any(f => r.StartsWith(f, StringComparison.Ordinal)));
    }
}
