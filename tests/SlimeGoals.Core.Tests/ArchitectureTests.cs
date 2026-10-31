using SlimeGoals.Core;

namespace SlimeGoals.Core.Tests;

/// <summary>
/// Protege a regra de dependência da arquitetura hexagonal (spec §5):
/// o Core é o centro e não pode conhecer UI, banco ou plataforma.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void Core_does_not_reference_other_projects_or_infrastructure_packages()
    {
        var forbidden = new[] { "SlimeGoals.", "Avalonia", "Dapper", "Microsoft.Data.Sqlite", "Tmds" };
        var refs = CoreAssembly.Reference.GetReferencedAssemblies().Select(a => a.Name!);
        refs.Should().NotContain(r => forbidden.Any(f => r.StartsWith(f)));
    }
}
