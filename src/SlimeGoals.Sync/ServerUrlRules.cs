namespace SlimeGoals.Sync;

/// <summary>
/// Endereço do servidor aceito pela tela e pelo coordenador (decisão 5 da 9.5a): URL absoluta <c>https</c>, ou
/// <c>http</c> só em loopback (<c>localhost</c>, 127.0.0.0/8 e <c>::1</c>), sem credenciais, consulta nem fragmento.
/// </summary>
public static class ServerUrlRules
{
    public const string InvalidText = "Informe um endereço http:// ou https:// completo.";
    public const string HttpsRequiredText = "Use https://. O http:// só vale para 127.0.0.1.";

    /// <summary>O texto do erro, ou <c>null</c> quando o endereço vale.</summary>
    public static string? Check(Uri? server)
    {
        if (server is not { IsAbsoluteUri: true }
            || (server.Scheme != Uri.UriSchemeHttp && server.Scheme != Uri.UriSchemeHttps)
            || server.Host.Length == 0 || server.UserInfo.Length > 0
            || server.Query.Length > 0 || server.Fragment.Length > 0)
            return InvalidText;
        return server.Scheme == Uri.UriSchemeHttp && !server.IsLoopback ? HttpsRequiredText : null;
    }
}
