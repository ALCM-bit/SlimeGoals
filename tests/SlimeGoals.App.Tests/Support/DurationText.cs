using System.Text.RegularExpressions;

namespace SlimeGoals.App.Tests.Support;

/// <summary>
/// O tempo como a tela mostra: <c>Dur("2 h 30 min")</c> vira "2&#160;h 30&#160;min", com espaço inquebrável entre o
/// número e a unidade (GoalFormatting.Duration). Deixa os textos esperados legíveis sem esconder o caractere real.
/// </summary>
internal static partial class DurationText
{
    public static string Dur(string text) => NumberAndUnit().Replace(text, "$1 $2");

    [GeneratedRegex(@"(\d) (h|min|s)\b")]
    private static partial Regex NumberAndUnit();
}
