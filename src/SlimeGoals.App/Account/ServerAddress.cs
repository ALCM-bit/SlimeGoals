namespace SlimeGoals.App.Account;

/// <summary>Confere o endereço do servidor digitado (decisão 5 da 9.5a).</summary>
public static class ServerAddress
{
    public const string InvalidText = "Informe um endereço http:// ou https:// completo.";

    /// <summary>
    /// Aceita só URL absoluta <c>http</c> ou <c>https</c>, sem credenciais embutidas, consulta nem fragmento. Espaços nas
    /// pontas são ignorados.
    /// </summary>
    public static bool TryParse(string? text, out Uri? server, out string? error)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.IndexOfAny(['?', '#']) < 0
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && parsed.UserInfo.Length == 0 && parsed.Host.Length > 0)
        {
            (server, error) = (parsed, null);
            return true;
        }

        (server, error) = (null, InvalidText);
        return false;
    }

    /// <summary>Endereço para mostrar: sem a barra final que o <see cref="Uri"/> acrescenta.</summary>
    public static string Display(Uri server) => server.AbsoluteUri.TrimEnd('/');
}
