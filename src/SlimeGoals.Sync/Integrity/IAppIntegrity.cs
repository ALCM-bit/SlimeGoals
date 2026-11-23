namespace SlimeGoals.Sync.Integrity;

/// <summary>O que a conferência de integridade do app achou (MASVS-RESILIENCE).</summary>
public enum IntegrityIssue
{
    /// <summary>O APK não tem a assinatura oficial: foi refeito ou alterado.</summary>
    ForeignSignature,

    /// <summary>Há depurador conectado, ou o app de distribuição está marcado como depurável.</summary>
    Debugger,

    /// <summary>O aparelho parece ter root: outros apps podem ler os dados deste.</summary>
    Rooted,
}

/// <summary>
/// Resultado de uma conferência. Assinatura diferente e depurador desligam a conta, porque o token e a chave de dados
/// ficariam ao alcance de quem alterou ou inspeciona o app. Root só gera aviso: é o aparelho da pessoa.
/// </summary>
public sealed record IntegrityReport(IReadOnlySet<IntegrityIssue> Issues)
{
    public static IntegrityReport Clean { get; } = new(new HashSet<IntegrityIssue>());

    public bool BlocksSync => Issues.Contains(IntegrityIssue.ForeignSignature) || Issues.Contains(IntegrityIssue.Debugger);
}

/// <summary>Conferência de integridade do app; só o Release do Android registra uma.</summary>
public interface IAppIntegrity
{
    /// <summary>Barata o bastante para rodar a cada operação de conta; nunca lança.</summary>
    IntegrityReport Check();
}

/// <summary>A conta foi recusada porque o app não passou na conferência de integridade.</summary>
public sealed class UntrustedAppException()
    : InvalidOperationException("O app não passou na conferência de segurança: a conta fica desligada.");
