namespace SlimeGoals.App.Account;

/// <summary>Valores que o head dá aos formulários da conta (decisão 18 da 9.5a).</summary>
/// <param name="DeviceName">Nome sugerido para este aparelho (no desktop, <c>Environment.MachineName</c>).</param>
/// <param name="DefaultServer">Servidor que o campo mostra sem nada gravado; o head escolhe, e nulo deixa o campo vazio.</param>
public sealed record AccountDefaults(string DeviceName, Uri? DefaultServer = null);
