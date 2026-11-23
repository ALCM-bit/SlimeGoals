namespace SlimeGoals.App.Account;

/// <summary>
/// Esconde a tela de capturas e da miniatura dos apps recentes enquanto uma página de conta (senhas, código de
/// recuperação) está à vista. No Android é o <c>FLAG_SECURE</c>; o Linux não tem equivalente e não registra nada.
/// </summary>
public interface ISecureScreen
{
    /// <summary>Chamado só quando o estado muda, na thread da interface.</summary>
    void SetProtected(bool value);
}
