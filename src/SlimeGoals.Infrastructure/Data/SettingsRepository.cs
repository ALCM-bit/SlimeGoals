using Dapper;
using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="ISettingsRepository"/>: tabela settings (key, value).</summary>
public sealed class SettingsRepository(SqliteConnectionFactory db) : ISettingsRepository
{
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var c = db.Open();
        return await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = @Key",
            new { Key = key }, cancellationToken: ct));
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await c.ExecuteAsync(new CommandDefinition(
            "INSERT OR REPLACE INTO settings (key, value) VALUES (@Key, @Value)",
            new { Key = key, Value = value }, cancellationToken: ct));
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await c.ExecuteAsync(new CommandDefinition(
            "DELETE FROM settings WHERE key = @Key",
            new { Key = key }, cancellationToken: ct));
    }
}
