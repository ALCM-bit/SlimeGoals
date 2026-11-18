namespace SlimeGoals.Server.Limits;

/// <summary>O IP do cliente. É a única leitura do IP no servidor: atrás do proxy configurado, o <c>UseForwardedHeaders</c> já o trocou.</summary>
public static class ClientIp
{
    public static string Of(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
}
