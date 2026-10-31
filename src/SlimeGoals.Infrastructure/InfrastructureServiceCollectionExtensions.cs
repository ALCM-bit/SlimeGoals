using Microsoft.Data.Sqlite;
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

        // O builder escapa o caminho (ex.: diretórios com ';'), ao contrário de interpolar a string.
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        services.AddSingleton(new SqliteConnectionFactory(connectionString));
        services.AddSingleton<IGoalRepository, GoalRepository>();
        return services;
    }
}
