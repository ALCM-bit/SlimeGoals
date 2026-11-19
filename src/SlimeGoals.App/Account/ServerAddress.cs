using SlimeGoals.Sync;

namespace SlimeGoals.App.Account;

/// <summary>Confere o endereço do servidor digitado, com a mesma regra do coordenador (<see cref="ServerUrlRules"/>).</summary>
public static class ServerAddress
{
    /// <summary>Espaços nas pontas são ignorados; o resto segue <see cref="ServerUrlRules.Check"/>.</summary>
    public static bool TryParse(string? text, out Uri? server, out string? error)
    {
        error = Uri.TryCreate(text?.Trim() ?? "", UriKind.Absolute, out var parsed)
            ? ServerUrlRules.Check(parsed)
            : ServerUrlRules.InvalidText;
        server = error is null ? parsed : null;
        return error is null;
    }

    /// <summary>Endereço para mostrar: sem a barra final que o <see cref="Uri"/> acrescenta.</summary>
    public static string Display(Uri server) => server.AbsoluteUri.TrimEnd('/');
}
