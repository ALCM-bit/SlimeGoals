namespace SlimeGoals.Core.Abstractions;

/// <summary>
/// Porta de saída para o cofre de segredos do sistema (Secret Service no Linux, Keystore no Android).
/// Guarda bytes opacos por nome. Sem cofre, a sincronização não liga: nada vai para arquivo sem proteção (§6).
/// </summary>
public interface ISecretVault
{
    /// <summary>Diz se o serviço do cofre responde. Não pede desbloqueio à pessoa.</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Lê o segredo, ou <c>null</c> se não houver nenhum com esse nome.</summary>
    /// <exception cref="SecretVaultUnavailableException">Cofre ausente ou desbloqueio recusado.</exception>
    Task<byte[]?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Grava o segredo, substituindo o que houver com o mesmo nome.</summary>
    /// <exception cref="SecretVaultUnavailableException">Cofre ausente ou desbloqueio recusado.</exception>
    Task SetAsync(string name, byte[] secret, CancellationToken ct = default);

    /// <summary>Apaga o segredo. Não é erro se ele não existir.</summary>
    /// <exception cref="SecretVaultUnavailableException">Cofre ausente ou desbloqueio recusado.</exception>
    Task DeleteAsync(string name, CancellationToken ct = default);
}

/// <summary>O cofre de segredos não está disponível: serviço ausente, falha de comunicação ou desbloqueio recusado.</summary>
public sealed class SecretVaultUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
