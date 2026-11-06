using System.Globalization;

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

    public static string Id(Guid id) => id.ToString("D");
}
