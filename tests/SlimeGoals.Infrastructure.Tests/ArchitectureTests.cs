using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests;

/// <summary>
/// Regra de dependência (spec §5): a Infrastructure implementa as portas de dados do Core sobre o SQLite e não conhece
/// tela, plataforma, motor de sincronização nem servidor.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] References =
        [.. typeof(SqliteConnectionFactory).Assembly.GetReferencedAssemblies().Select(a => a.Name!)];

    [Fact]
    public void Infrastructure_references_only_the_core()
    {
        References.Where(r => r.StartsWith("SlimeGoals.", StringComparison.Ordinal))
            .Should().BeSubsetOf(["SlimeGoals.Core"]);
    }

    [Fact]
    public void Infrastructure_does_not_reference_ui_platform_or_server_packages()
    {
        string[] forbidden = ["Avalonia", "CommunityToolkit", "Tmds", "Mono.Android", "Npgsql", "Microsoft.AspNetCore", "MailKit"];
        References.Should().NotContain(r => forbidden.Any(f => r.StartsWith(f, StringComparison.Ordinal)));
    }
}
