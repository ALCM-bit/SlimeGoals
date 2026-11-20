namespace SlimeGoals.Platform.Linux.Tests;

/// <summary>
/// Regra de dependência (spec §5): a Platform.Linux implementa as portas do Core sobre o D-Bus e o XDG e não conhece
/// tela, banco, motor de sincronização nem servidor.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] References =
        [.. typeof(XdgPaths).Assembly.GetReferencedAssemblies().Select(a => a.Name!)];

    [Fact]
    public void Platform_references_only_the_core()
    {
        References.Where(r => r.StartsWith("SlimeGoals.", StringComparison.Ordinal))
            .Should().BeSubsetOf(["SlimeGoals.Core"]);
    }

    [Fact]
    public void Platform_does_not_reference_ui_database_or_server_packages()
    {
        string[] forbidden = ["Avalonia", "CommunityToolkit", "Dapper", "Microsoft.Data.Sqlite", "Mono.Android", "Npgsql",
            "Microsoft.AspNetCore", "MailKit"];
        References.Should().NotContain(r => forbidden.Any(f => r.StartsWith(f, StringComparison.Ordinal)));
    }
}
