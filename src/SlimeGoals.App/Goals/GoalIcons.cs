namespace SlimeGoals.App.Goals;

/// <summary>Ícone de meta: chave gravada no banco, nome em pt-BR (acessibilidade) e o desenho em path data 24×24.</summary>
public sealed record GoalIcon(string Key, string Name, string PathData);

/// <summary>
/// Conjunto fixo de ícones de meta (spec §15, Fase 2). Desenhos de traço do Lucide
/// (lucide-static 1.66.0, licença ISC, © Lucide Icons and Contributors — ver THIRD-PARTY-NOTICES.md),
/// convertidos para um único path (círculos viram arcos; flags de arco normalizadas).
/// </summary>
public static class GoalIcons
{
    public const string DefaultKey = "target";

    public static IReadOnlyList<GoalIcon> All { get; } =
    [
        new("book", "Livro", "M 12 5 v 16 M 20.001 19 A 2 2 0 0 0 22 17 V 5 a 2 2 0 0 0 -1.999 -2 L 16 3.002 A 5 5 0 0 0 12 5 a 5 5 0 0 0 -4 -2 H 4 a 2 2 0 0 0 -2 2 v 12 a 2 2 0 0 0 1.999 2 H 8 a 5 5 0 0 1 4 2 a 5 5 0 0 1 4 -2 z"),
        new("run", "Caminhada", "M 4 16 v -2.38 C 4 11.5 2.97 10.5 3 8 c 0.03 -2.72 1.49 -6 4.5 -6 C 9.37 2 10 3.8 10 5.5 c 0 3.11 -2 5.66 -2 8.68 V 16 a 2 2 0 1 1 -4 0 Z M 20 20 v -2.38 c 0 -2.12 1.03 -3.12 1 -5.62 c -0.03 -2.72 -1.49 -6 -4.5 -6 C 14.63 6 14 7.8 14 9.5 c 0 3.11 2 5.66 2 8.68 V 20 a 2 2 0 1 0 4 0 Z M 16 17 h 4 M 4 13 h 4"),
        new("water", "Água", "M 12 22 a 7 7 0 0 0 7 -7 c 0 -2 -1 -3.9 -3 -5.5 s -3.5 -4 -4 -6.5 c -0.5 2.5 -2 4.9 -4 6.5 C 6 11.1 5 13 5 15 a 7 7 0 0 0 7 7 z"),
        new("workout", "Exercício", "M 17.596 12.768 a 2 2 0 1 0 2.829 -2.829 l -1.768 -1.767 a 2 2 0 0 0 2.828 -2.829 l -2.828 -2.828 a 2 2 0 0 0 -2.829 2.828 l -1.767 -1.768 a 2 2 0 1 0 -2.829 2.829 z m 2.5 21.5 l 1.4 -1.4 m 20.1 3.9 l 1.4 -1.4 M 5.343 21.485 a 2 2 0 1 0 2.829 -2.828 l 1.767 1.768 a 2 2 0 1 0 2.829 -2.829 l -6.364 -6.364 a 2 2 0 1 0 -2.829 2.829 l 1.768 1.767 a 2 2 0 0 0 -2.828 2.829 z m 9.6 14.4 l 4.8 -4.8"),
        new("sleep", "Sono", "M 2 4 v 16 M 2 8 h 18 a 2 2 0 0 1 2 2 v 10 M 2 17 h 20 M 6 8 v 9"),
        new("health", "Saúde", "M 2 9.5 a 5.5 5.5 0 0 1 9.591 -3.676 a 0.56 0.56 0 0 0 0.818 0 A 5.49 5.49 0 0 1 22 9.5 c 0 2.29 -1.5 4 -3 5.5 l -5.492 5.313 a 2 2 0 0 1 -3 0.019 L 5 15 c -1.5 -1.5 -3 -3.2 -3 -5.5"),
        new("language", "Idiomas", "m 5 8 l 6 6 m 4 14 l 6 -6 l 2 -3 M 2 5 h 12 M 7 2 h 1 m 22 22 l -5 -10 l -5 10 M 14 18 h 6"),
        new("code", "Código", "m 16 18 l 6 -6 l -6 -6 m 8 6 l -6 6 l 6 6"),
        new("music", "Música", "M 21 16 V 3 a 1 1 0 0 0 -1.28 -0.96 l -10 2.917 a 1 1 0 0 0 -0.72 0.96 V 19 M 15 16 A 3 3 0 1 0 21 16 A 3 3 0 1 0 15 16 Z M 3 19 A 3 3 0 1 0 9 19 A 3 3 0 1 0 3 19 Z"),
        new("art", "Arte", "m 14.622 17.897 l -10.68 -2.913 M 18.376 2.622 a 1 1 0 1 1 3.002 3.002 L 17.36 9.643 a 0.5 0.5 0 0 0 0 0.707 l 0.944 0.944 a 2.41 2.41 0 0 1 0 3.408 l -0.944 0.944 a 0.5 0.5 0 0 1 -0.707 0 L 8.354 7.348 a 0.5 0.5 0 0 1 0 -0.707 l 0.944 -0.944 a 2.41 2.41 0 0 1 3.408 0 l 0.944 0.944 a 0.5 0.5 0 0 0 0.707 0 z M 9 8 c -1.804 2.71 -3.97 3.46 -6.583 3.948 a 0.507 0.507 0 0 0 -0.302 0.819 l 7.32 8.883 a 1 1 0 0 0 1.185 0.204 C 12.735 20.405 16 16.792 16 15"),
        new("money", "Dinheiro", "M 19 7 V 4 a 1 1 0 0 0 -1 -1 H 5 a 2 2 0 0 0 0 4 h 15 a 1 1 0 0 1 1 1 v 4 h -3 a 2 2 0 0 0 0 4 h 3 a 1 1 0 0 0 1 -1 v -2 a 1 1 0 0 0 -1 -1 M 3 5 v 14 a 2 2 0 0 0 2 2 h 15 a 1 1 0 0 0 1 -1 v -4"),
        new("nature", "Natureza", "M 11 20 a 10 10 0 0 0 10 -10 a 25.9 25.9 0 0 0 -1.04 -7.281 a 1 1 0 0 0 -1.755 -0.325 C 15.833 5.5 13 5.5 9.8 6.1 A 7 7 0 0 0 11 20 M 2 21 a 5 5 0 0 1 2.911 -4.544 C 7.613 15.212 8.351 15.24 11 13"),
        new("coffee", "Pausa", "M 10 2 v 2 M 14 2 v 2 M 16 8 a 1 1 0 0 1 1 1 v 8 a 4 4 0 0 1 -4 4 H 7 a 4 4 0 0 1 -4 -4 V 9 a 1 1 0 0 1 1 -1 h 14 a 4 4 0 1 1 0 8 h -1 M 6 2 v 2"),
        new("medicine", "Remédio", "m 10.5 20.5 l 10 -10 a 4.95 4.95 0 1 0 -7 -7 l -10 10 a 4.95 4.95 0 1 0 7 7 Z m 8.5 8.5 l 7 7"),
        new("star", "Favorito", "M 11.525 2.295 a 0.53 0.53 0 0 1 0.95 0 l 2.31 4.679 a 2.123 2.123 0 0 0 1.595 1.16 l 5.166 0.756 a 0.53 0.53 0 0 1 0.294 0.904 l -3.736 3.638 a 2.123 2.123 0 0 0 -0.611 1.878 l 0.882 5.14 a 0.53 0.53 0 0 1 -0.771 0.56 l -4.618 -2.428 a 2.122 2.122 0 0 0 -1.973 0 L 6.396 21.01 a 0.53 0.53 0 0 1 -0.77 -0.56 l 0.881 -5.139 a 2.122 2.122 0 0 0 -0.611 -1.879 L 2.16 9.795 a 0.53 0.53 0 0 1 0.294 -0.906 l 5.165 -0.755 a 2.122 2.122 0 0 0 1.597 -1.16 z"),
        new("target", "Objetivo", "M 2 12 A 10 10 0 1 0 22 12 A 10 10 0 1 0 2 12 Z M 6 12 A 6 6 0 1 0 18 12 A 6 6 0 1 0 6 12 Z M 10 12 A 2 2 0 1 0 14 12 A 2 2 0 1 0 10 12 Z"),
    ];

    private static readonly Dictionary<string, GoalIcon> ByKey = All.ToDictionary(i => i.Key);

    /// <summary>Ícone da chave; chave desconhecida ou vazia (ex.: banco antigo) cai no ícone padrão.</summary>
    public static GoalIcon Get(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var icon) ? icon : ByKey[DefaultKey];
}
