using System.Globalization;
using System.Text.Json;

namespace SlimeGoals.Sync.Crypto;

/// <summary>
/// Parâmetros do Argon2id guardados por conta. Os que vêm do servidor passam por <see cref="Parse"/>, que recusa
/// valores fora do piso e do teto antes de qualquer derivação.
/// </summary>
/// <param name="Version">Versão do esquema de derivação; só 1 existe.</param>
/// <param name="MemoryKiB">Memória do Argon2id, em KiB.</param>
/// <param name="Iterations">Passadas do Argon2id.</param>
/// <param name="Parallelism">Faixas do Argon2id.</param>
public sealed record KdfParams(int Version, int MemoryKiB, int Iterations, int Parallelism)
{
    // Piso: um servidor malicioso não consegue baratear o teste de senhas contra a auth_key.
    // Teto: um servidor não consegue pedir memória demais ao celular.
    private const int MinMemoryKiB = 65536;
    private const int MaxMemoryKiB = 262144;
    private const int MinIterations = 3;
    private const int MaxIterations = 10;
    private const int MinParallelism = 1;
    private const int MaxParallelism = 4;

    /// <summary>64 MiB, 3 passadas e 1 faixa, como no spike.</summary>
    public static KdfParams Default { get; } = new(1, 65536, 3, 1);

    /// <summary>JSON compacto, na ordem <c>v</c>, <c>m</c>, <c>t</c>, <c>p</c>.</summary>
    public string ToJson() => string.Create(
        CultureInfo.InvariantCulture,
        $$"""{"v":{{Version}},"m":{{MemoryKiB}},"t":{{Iterations}},"p":{{Parallelism}}}""");

    /// <summary>Lê os parâmetros e confere os limites.</summary>
    /// <exception cref="KdfParamsException">JSON inválido, campo ausente, repetido ou fora dos limites.</exception>
    public static KdfParams Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        int? v = null, m = null, t = null, p = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new KdfParamsException("Os parâmetros de derivação precisam ser um objeto JSON.");

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "v": Set(ref v, property); break;
                    case "m": Set(ref m, property); break;
                    case "t": Set(ref t, property); break;
                    case "p": Set(ref p, property); break;
                }
            }
        }
        catch (JsonException e)
        {
            throw new KdfParamsException("Os parâmetros de derivação não são um JSON válido.", e);
        }

        if (v is null || m is null || t is null || p is null)
            throw new KdfParamsException("Faltam campos nos parâmetros de derivação.");

        var result = new KdfParams(v.Value, m.Value, t.Value, p.Value);
        result.EnsureWithinLimits();
        return result;
    }

    /// <summary>Confere a versão e os limites de memória, passadas e faixas.</summary>
    /// <exception cref="KdfParamsException">Algum valor fora dos limites.</exception>
    public void EnsureWithinLimits()
    {
        if (Version != 1)
            throw new KdfParamsException($"Versão de derivação desconhecida: {Version}.");
        if (MemoryKiB is < MinMemoryKiB or > MaxMemoryKiB)
            throw new KdfParamsException($"Memória de derivação fora dos limites: {MemoryKiB} KiB.");
        if (Iterations is < MinIterations or > MaxIterations)
            throw new KdfParamsException($"Passadas de derivação fora dos limites: {Iterations}.");
        if (Parallelism is < MinParallelism or > MaxParallelism)
            throw new KdfParamsException($"Faixas de derivação fora dos limites: {Parallelism}.");
    }

    private static void Set(ref int? field, JsonProperty property)
    {
        if (field is not null)
            throw new KdfParamsException($"Campo repetido nos parâmetros de derivação: {property.Name}.");
        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var value))
            throw new KdfParamsException($"Campo inválido nos parâmetros de derivação: {property.Name}.");
        field = value;
    }
}

/// <summary>Parâmetros de derivação recusados: malformados, fracos demais ou pesados demais.</summary>
public sealed class KdfParamsException : Exception
{
    public KdfParamsException(string message) : base(message) { }

    public KdfParamsException(string message, Exception inner) : base(message, inner) { }
}
