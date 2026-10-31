using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registra o SQLite no arquivo indicado, criando a pasta se necessário.</summary>
    public static IServiceCollection AddSlimeGoalsInfrastructure(this IServiceCollection services, string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        services.AddSingleton(new SqliteConnectionFactory($"Data Source={databasePath}"));
        services.AddSingleton<IGoalRepository, GoalRepository>();
        return services;
    }
}
