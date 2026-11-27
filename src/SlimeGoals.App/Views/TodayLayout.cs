namespace SlimeGoals.App.Views;

/// <summary>Forma do Hoje: cartões no celular; no desktop, linhas com o resumo numa faixa acima ou numa coluna ao lado.</summary>
public enum TodayLayoutMode { Phone, Top, Side }

/// <summary>Escolhe a forma do Hoje pela largura (decisão 16 da interface no Android).</summary>
public static class TodayLayout
{
    /// <summary>Largura da página a partir da qual o resumo vai para a coluna da direita.</summary>
    public const double SideBreakpoint = 980;

    /// <summary>
    /// <paramref name="windowWidth"/> decide o celular, com a mesma regra da classe <c>compact</c> do shell;
    /// <paramref name="pageWidth"/>, que encolhe com a barra lateral, decide onde fica o resumo.
    /// </summary>
    public static TodayLayoutMode For(double windowWidth, double pageWidth) =>
        windowWidth < ShellView.CompactBreakpoint ? TodayLayoutMode.Phone
        : pageWidth >= SideBreakpoint ? TodayLayoutMode.Side
        : TodayLayoutMode.Top;
}
