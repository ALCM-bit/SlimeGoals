using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SlimeGoals.Core.Domain;

/// <summary>Regras do nome de uma tag: normalização e validação.</summary>
public static partial class TagName
{
    /// <summary>Máximo de caracteres do nome, depois de normalizado.</summary>
    public const int MaxLength = 24;

    /// <summary>
    /// Normaliza o nome: NFC, sem espaços nas pontas, espaços repetidos viram um só e
    /// minúsculas invariantes (independe da cultura do aparelho).
    /// </summary>
    public static string Normalize(string raw)
    {
        var text = raw.Normalize(NormalizationForm.FormC).Trim();
        return Spaces().Replace(text, " ").ToLowerInvariant();
    }

    /// <summary>Valida um nome já normalizado. Devolve o texto do erro, ou null se for válido.</summary>
    public static string? Validate(string normalized)
    {
        if (normalized.Length == 0)
        {
            return "Dê um nome à tag.";
        }

        return normalized.Length > MaxLength ? $"A tag pode ter até {MaxLength.ToString(CultureInfo.InvariantCulture)} caracteres." : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
