using SlimeGoals.Sync;

namespace SlimeGoals.App.Account;

/// <summary>
/// Quando a tela Hoje avisa que a sincronização parou (certificado vencido, servidor fora do ar, Tailscale desligado):
/// conta ligada e a última sincronização completa há mais de <see cref="After"/>.
/// </summary>
public static class SyncStaleWarning
{
    public static readonly TimeSpan After = TimeSpan.FromDays(3);

    public const string Text = "Não sincroniza há 3 dias. Confira a conexão com o servidor.";

    /// <summary>A conexão não é o problema: a conta está desligada pela conferência de integridade.</summary>
    public const string UntrustedText = "Não sincroniza há 3 dias: o app não passou na conferência de segurança.";

    /// <summary>O texto do aviso, ou nulo quando não há o que avisar.</summary>
    public static string? TextFor(AccountStatus status, DateTimeOffset now) =>
        !IsStale(status, now) ? null
        : status.State == AccountState.UntrustedApp ? UntrustedText
        : Text;

    /// <summary>Sem conta, sem sincronização completa ainda ou com a hora no futuro, não há o que avisar.</summary>
    public static bool IsStale(AccountStatus status, DateTimeOffset now) =>
        status.State != AccountState.SignedOut && status.LastSyncAt is { } last && now - last > After;
}
