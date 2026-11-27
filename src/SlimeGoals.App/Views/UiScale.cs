namespace SlimeGoals.App.Views;

/// <summary>
/// Zoom do app no desktop: as telas foram desenhadas para uma janela de até 1280×800; numa janela maior, tudo cresce na
/// proporção dela, pela menor razão entre largura e altura, para a página não sobrar nem cortar embaixo.
/// </summary>
public static class UiScale
{
    /// <summary>Tamanho da janela até o qual o app fica no tamanho desenhado.</summary>
    public const double DesignWidth = 1280;

    /// <inheritdoc cref="DesignWidth"/>
    public const double DesignHeight = 800;

    /// <summary>Teto do zoom: acima dele, a janela grande ganha espaço em vez de letras ainda maiores.</summary>
    public const double Maximum = 1.75;

    /// <summary>O zoom para uma janela de <paramref name="width"/>×<paramref name="height"/>, entre 1 e <see cref="Maximum"/>.</summary>
    public static double For(double width, double height) =>
        Math.Clamp(Math.Min(width / DesignWidth, height / DesignHeight), 1, Maximum);
}
