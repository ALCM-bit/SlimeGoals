using System.Reflection;

namespace SlimeGoals.Core;

/// <summary>Marcador para referenciar o assembly do Core (usado em testes de arquitetura).</summary>
public static class CoreAssembly
{
    public static Assembly Reference => typeof(CoreAssembly).Assembly;
}
