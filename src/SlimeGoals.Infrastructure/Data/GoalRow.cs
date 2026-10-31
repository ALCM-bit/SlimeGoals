using System.Globalization;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>
/// Linha crua da tabela goals. A conversão explícita evita armadilhas do Dapper + SQLite:
/// o Microsoft.Data.Sqlite grava Guid como BLOB por padrão, e DateOnly/TimeOnly não têm
/// mapeamento nativo. Aqui tudo vira TEXT nos formatos da spec §8.
/// </summary>
internal sealed class GoalRow
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";

    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Recurrence { get; init; }
    public required int Target { get; init; }
    public required string Icon { get; init; }
    public required string Color { get; init; }
    public required int ReminderEveryMin { get; init; }
    public required string ActiveFrom { get; init; }
    public required string ActiveUntil { get; init; }
    public required string ActiveDays { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public string? CompletedAt { get; init; }
    public string? ArchivedAt { get; init; }
    public string? DeletedAt { get; init; }

    public static GoalRow FromDomain(Goal g) => new()
    {
        Id = g.Id.ToString("D"),
        Title = g.Title,
        Recurrence = g.Recurrence switch
        {
            Core.Domain.Recurrence.Once => "once",
            Core.Domain.Recurrence.Daily => "daily",
            Core.Domain.Recurrence.Weekly => "weekly",
            _ => throw new ArgumentOutOfRangeException(nameof(g), g.Recurrence, null),
        },
        Target = g.Target,
        Icon = g.Icon,
        Color = g.Color.ToString().ToLowerInvariant(),
        ReminderEveryMin = g.ReminderEveryMinutes,
        ActiveFrom = g.ActiveFrom.ToString(TimeFormat, Inv),
        ActiveUntil = g.ActiveUntil.ToString(TimeFormat, Inv),
        ActiveDays = g.ActiveDays.ToStorage(),
        StartDate = g.StartDate?.ToString(DateFormat, Inv),
        EndDate = g.EndDate?.ToString(DateFormat, Inv),
        CreatedAt = Instant(g.CreatedAt),
        UpdatedAt = Instant(g.UpdatedAt),
        CompletedAt = g.CompletedAt is { } c ? Instant(c) : null,
        ArchivedAt = g.ArchivedAt is { } a ? Instant(a) : null,
        DeletedAt = g.DeletedAt is { } d ? Instant(d) : null,
    };

    public Goal ToDomain() => new()
    {
        Id = Guid.ParseExact(Id, "D"),
        Title = Title,
        Recurrence = Recurrence switch
        {
            "once" => Core.Domain.Recurrence.Once,
            "daily" => Core.Domain.Recurrence.Daily,
            "weekly" => Core.Domain.Recurrence.Weekly,
            _ => throw new FormatException($"Recorrência desconhecida no banco: '{Recurrence}'."),
        },
        Target = Target,
        Icon = Icon,
        Color = Enum.Parse<GoalColor>(Color, ignoreCase: true),
        ReminderEveryMinutes = ReminderEveryMin,
        ActiveFrom = TimeOnly.ParseExact(ActiveFrom, TimeFormat, Inv),
        ActiveUntil = TimeOnly.ParseExact(ActiveUntil, TimeFormat, Inv),
        ActiveDays = WeekDaysExtensions.ParseStorage(ActiveDays),
        StartDate = StartDate is null ? null : DateOnly.ParseExact(StartDate, DateFormat, Inv),
        EndDate = EndDate is null ? null : DateOnly.ParseExact(EndDate, DateFormat, Inv),
        CreatedAt = ParseInstant(CreatedAt),
        UpdatedAt = ParseInstant(UpdatedAt),
        CompletedAt = CompletedAt is null ? null : ParseInstant(CompletedAt),
        ArchivedAt = ArchivedAt is null ? null : ParseInstant(ArchivedAt),
        DeletedAt = DeletedAt is null ? null : ParseInstant(DeletedAt),
    };

    /// <summary>ISO 8601 round-trip em UTC: ordena corretamente como texto.</summary>
    private static string Instant(DateTimeOffset value) => value.ToUniversalTime().ToString("O", Inv);

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.ParseExact(value, "O", Inv, DateTimeStyles.RoundtripKind);
}
