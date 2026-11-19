namespace SlimeGoals.Core.Tests;

/// <summary>
/// Protege a regra de dependência da arquitetura hexagonal (spec §5): o Core é o centro e não depende de nada além da
/// biblioteca base. UI, banco, plataforma e os outros projetos ficam fora, inclusive pacotes que ainda não existem aqui.
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void Core_references_only_the_base_library()
    {
        var refs = CoreAssembly.Reference.GetReferencedAssemblies().Select(a => a.Name!);
        refs.Should().OnlyContain(r => r.StartsWith("System.", StringComparison.Ordinal) || r == "netstandard");
    }
}
