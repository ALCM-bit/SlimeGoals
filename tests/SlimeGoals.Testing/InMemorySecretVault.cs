using System.Collections.Concurrent;
using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Testing;

/// <summary>
/// Implementação em memória de <see cref="ISecretVault"/>. <see cref="Available"/> desligado simula o cofre ausente;
/// <see cref="FailWritesWith"/> faz só a gravação falhar. Guarda e devolve cópias dos bytes.
/// </summary>
public sealed class InMemorySecretVault : ISecretVault
{
    private readonly ConcurrentDictionary<string, byte[]> _secrets = new();

    /// <summary>Quando falso, o cofre se comporta como ausente.</summary>
    public bool Available { get; set; } = true;

    /// <summary>Quando definido, <see cref="SetAsync"/> lança esta exceção e não grava nada.</summary>
    public Exception? FailWritesWith { get; set; }

    /// <summary>Nomes guardados no momento.</summary>
    public IReadOnlyCollection<string> Names => _secrets.Keys.ToArray();

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(Available);

    public Task<byte[]?> GetAsync(string name, CancellationToken ct = default)
    {
        EnsureAvailable();
        return Task.FromResult(_secrets.TryGetValue(name, out var secret) ? secret.ToArray() : null);
    }

    public Task SetAsync(string name, byte[] secret, CancellationToken ct = default)
    {
        EnsureAvailable();
        if (FailWritesWith is { } failure) throw failure;
        _secrets[name] = secret.ToArray();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        EnsureAvailable();
        _secrets.TryRemove(name, out _);
        return Task.CompletedTask;
    }

    private void EnsureAvailable()
    {
        if (!Available) throw new SecretVaultUnavailableException("Cofre de segredos simulado como ausente.");
    }
}
