namespace SlimeGoals.Sync.Account;

/// <summary>Por que uma chamada à API de conta falhou (decisão 16).</summary>
public enum AccountErrorKind
{
    Unauthorized,
    InvalidToken,
    InvalidRequest,
    RateLimited,
    TooLarge,
    NotFound,

    /// <summary>Rede, erro 5xx ou resposta ilegível.</summary>
    Unavailable,
}

/// <summary>Falha da API de conta. A mensagem é técnica; o texto para a pessoa fica na camada de tela.</summary>
public sealed class AccountApiException(AccountErrorKind kind, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception($"Falha na API de conta: {kind}.", inner)
{
    public AccountErrorKind Kind { get; } = kind;

    /// <summary>Espera pedida pelo servidor; só vem em <see cref="AccountErrorKind.RateLimited"/>.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
