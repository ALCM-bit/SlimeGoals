using System.Collections.Concurrent;
using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Testing;

/// <summary>Implementação em memória de <see cref="ISettingsRepository"/>.</summary>
public sealed class InMemorySettingsRepository : ISettingsRepository
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
