using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Sync.Tests;

/// <summary>
/// Regra de dependência da sincronização: o motor fala com o banco local pelas portas do Core e com o servidor pelos
/// contratos, sem conhecer tela nem plataforma. Os contratos são o formato da conversa entre o app e o servidor e só
/// usam a biblioteca base, para os dois lados poderem referenciá-los sem levar mais nada junto.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] Engine = ReferencesOf(typeof(SyncHttp));
    private static readonly string[] Contracts = ReferencesOf(typeof(RegisterRequest));

    [Fact]
    public void Engine_references_only_the_core_and_the_contracts()
    {
        Engine.Where(r => r.StartsWith("SlimeGoals.", StringComparison.Ordinal))
            .Should().BeSubsetOf(["SlimeGoals.Core", "SlimeGoals.Sync.Contracts"]);
    }

    [Fact]
    public void Engine_does_not_reference_ui_database_platform_or_server_packages()
    {
        string[] forbidden = ["Avalonia", "CommunityToolkit", "Dapper", "Microsoft.Data.Sqlite", "Tmds", "Mono.Android",
            "Npgsql", "Microsoft.AspNetCore", "MailKit"];
        Engine.Should().NotContain(r => forbidden.Any(f => r.StartsWith(f, StringComparison.Ordinal)));
    }

    [Fact]
    public void Contracts_reference_only_the_base_library()
    {
        Contracts.Should().OnlyContain(r => r.StartsWith("System.", StringComparison.Ordinal) || r == "netstandard");
    }

    private static string[] ReferencesOf(Type type) => [.. type.Assembly.GetReferencedAssemblies().Select(a => a.Name!)];
}
