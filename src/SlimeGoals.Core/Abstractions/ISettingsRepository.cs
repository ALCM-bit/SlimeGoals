namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para configurações chave/valor (tabela settings).</summary>
public interface ISettingsRepository
{
    /// <summary>Valor da chave, ou null se não existe.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Grava a chave (insere ou substitui).</summary>
    Task SetAsync(string key, string value, CancellationToken ct = default);

    /// <summary>Apaga a chave (sem erro se não existe).</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
