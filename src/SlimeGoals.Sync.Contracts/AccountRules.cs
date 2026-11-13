namespace SlimeGoals.Sync.Contracts;

/// <summary>
/// Regras dos campos da conta, as mesmas no app e no servidor: o app recusa antes de enviar exatamente o que o
/// servidor recusaria. As telas leem os limites de tamanho daqui.
/// </summary>
public static class AccountRules
{
    public const int MaxEmailLength = 254;
    public const int MaxDeviceNameLength = 100;

    /// <summary>
    /// O e-mail sem espaços nas pontas e em minúsculas, ou nulo se não for aceito. Aceita o formato comum
    /// <c>nome@dominio.tld</c>: um só <c>@</c>, algo antes dele, e um domínio com pelo menos um ponto e sem partes
    /// vazias. Quem prova que o endereço existe é o e-mail de confirmação, não esta regra.
    /// </summary>
    public static string? NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxEmailLength
            || normalized.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
        {
            return null;
        }

        var at = normalized.IndexOf('@');
        if (at <= 0 || at != normalized.LastIndexOf('@'))
        {
            return null;
        }

        var labels = normalized[(at + 1)..].Split('.');
        return labels.Length >= 2 && labels.All(label => label.Length > 0) ? normalized : null;
    }

    /// <summary>O nome do aparelho sem espaços nas pontas, ou nulo se vazio, grande demais ou com caractere de controle.</summary>
    public static string? NormalizeDeviceName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxDeviceNameLength || trimmed.Any(char.IsControl)
            ? null
            : trimmed;
    }
}
