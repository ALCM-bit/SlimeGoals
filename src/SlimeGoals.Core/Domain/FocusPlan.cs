using System.Text.Json;
using System.Text.Json.Serialization;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Domain;

/// <summary>Tipo de um bloco da sequência do Pomodoro.</summary>
public enum FocusBlockKind { Focus, Break }

/// <summary>Um bloco da sequência: foco ou pausa, com duração em minutos.</summary>
public sealed record FocusBlock(FocusBlockKind Kind, int Minutes);

/// <summary>
/// Sequência do Pomodoro: a lista de blocos repetida <see cref="Repeat"/> vezes.
/// A igualdade compara os blocos um a um, não a referência da lista.
/// </summary>
public sealed record FocusPlan(IReadOnlyList<FocusBlock> Blocks, int Repeat)
{
    public const int MaxBlockMinutes = 240;
    public const int MaxBlocks = 12;
    public const int MaxRepeat = 10;

    // Cópia defensiva: o plano não muda se a lista de quem o criou mudar.
    public IReadOnlyList<FocusBlock> Blocks { get; } = Blocks.ToArray();

    /// <summary>Sequência da primeira vez: 25 min de foco, 5 de pausa, 4 vezes.</summary>
    public static FocusPlan Default { get; } =
        new([new(FocusBlockKind.Focus, 25), new(FocusBlockKind.Break, 5)], 4);

    public int TotalSteps => Blocks.Count * Repeat;

    public int TotalMinutes => Blocks.Sum(b => b.Minutes) * Repeat;

    public FocusBlock BlockAt(int step) => Blocks[step % Blocks.Count];

    /// <summary>Rodada do passo, contada a partir de 1.</summary>
    public int RoundOf(int step) => step / Blocks.Count + 1;

    /// <summary>Quantos focos há em cada rodada.</summary>
    public int FocusCountInRound => Blocks.Count(b => b.Kind == FocusBlockKind.Focus);

    /// <summary>Posição (a partir de 1) do foco do passo dentro da rodada; numa pausa, a do último foco antes dela.</summary>
    public int FocusNumberInRound(int step)
    {
        var index = step % Blocks.Count;
        return Blocks.Take(index + 1).Count(b => b.Kind == FocusBlockKind.Focus);
    }

    public IReadOnlyList<ValidationError> Validate()
    {
        var errors = new List<ValidationError>();
        if (Blocks.Any(b => b.Minutes < 1 || b.Minutes > MaxBlockMinutes))
            errors.Add(new ValidationError("Blocks", "O bloco deve ter de 1 min a 4 h."));
        if (Blocks.Count > MaxBlocks)
            errors.Add(new ValidationError("Blocks", "A sequência pode ter até 12 blocos."));
        if (!Blocks.Any(b => b.Kind == FocusBlockKind.Focus))
            errors.Add(new ValidationError("Blocks", "Inclua pelo menos um bloco de foco."));
        if (Repeat < 1 || Repeat > MaxRepeat)
            errors.Add(new ValidationError("Repeat", "Repita de 1 a 10 vezes."));
        return errors;
    }

    public string ToJson() => JsonSerializer.Serialize(new Dto(
        Blocks.Select(b => (BlockDto?)new BlockDto(b.Kind == FocusBlockKind.Focus ? "focus" : "break", b.Minutes)).ToList(),
        Repeat));

    /// <summary>Lê o JSON de <see cref="ToJson"/>; texto inválido devolve <c>null</c>.</summary>
    public static FocusPlan? FromJson(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(json);
            if (dto?.Blocks is null) return null;
            var blocks = new List<FocusBlock>();
            foreach (var b in dto.Blocks)
            {
                if (b is null) return null;
                var kind = b.Kind switch
                {
                    "focus" => FocusBlockKind.Focus,
                    "break" => FocusBlockKind.Break,
                    _ => (FocusBlockKind?)null,
                };
                if (kind is null) return null;
                blocks.Add(new FocusBlock(kind.Value, b.Minutes));
            }
            return new FocusPlan(blocks, dto.Repeat);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public bool Equals(FocusPlan? other) =>
        other is not null && Repeat == other.Repeat && Blocks.SequenceEqual(other.Blocks);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Repeat);
        foreach (var b in Blocks) hash.Add(b);
        return hash.ToHashCode();
    }

    private sealed record Dto(
        [property: JsonPropertyName("blocks")] List<BlockDto?>? Blocks,
        [property: JsonPropertyName("repeat")] int Repeat);

    private sealed record BlockDto(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("minutes")] int Minutes);
}
