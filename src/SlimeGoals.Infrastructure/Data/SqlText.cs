using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Formatos de texto usados no SQLite (spec §8): instantes em UTC, ISO 8601 round-trip.</summary>
internal static class SqlText
{
    /// <summary>SQLITE_CONSTRAINT_FOREIGNKEY: a meta referenciada não existe (mais).</summary>
    public const int ForeignKeyViolation = 787;

    /// <summary>ISO 8601 round-trip em UTC: ordena corretamente como texto.</summary>
    public static string Instant(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Lista de ids em JSON. Sem reflexão, para funcionar com trimming no Android.</summary>
    public static string IdList(IEnumerable<Guid> ids)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var id in ids)
                writer.WriteStringValue(Id(id));
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static IReadOnlyList<Guid> ParseIdList(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return [];
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => Guid.ParseExact(e.GetString()!, "D")).ToList();
    }

    public static string Id(Guid id) => id.ToString("D");
}
