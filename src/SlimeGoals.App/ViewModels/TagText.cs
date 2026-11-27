using System.Globalization;

namespace SlimeGoals.App.ViewModels;

/// <summary>Como o nome de uma tag aparece na tela.</summary>
public static class TagText
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// A primeira letra maiúscula ("saúde" → "Saúde"), em todo o app (decisão 22). O nome gravado continua o
    /// normalizado, em minúsculas: busca, sincronização e lembretes não mudam.
    /// </summary>
    public static string Display(string name) =>
        name.Length == 0 ? name : string.Concat(name[..1].ToUpper(PtBr), name[1..]);
}
