namespace SlimeGoals.App.Account;

/// <summary>Valores que o head dá aos formulários da conta (decisão 18 da 9.5a).</summary>
/// <param name="DeviceName">Nome sugerido para este aparelho (no desktop, <c>Environment.MachineName</c>).</param>
public sealed record AccountDefaults(string DeviceName);
