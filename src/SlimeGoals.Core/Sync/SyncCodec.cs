using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Sync;

/// <summary>
/// Converte metas e registros no conteúdo (JSON) que o envelope carrega (spec de sincronização §6).
/// Formatos invariantes: datas yyyy-MM-dd, horas HH:mm, instantes "O". <c>DeletedAt</c> não entra. Toda falha de leitura é <see cref="FormatException"/>.
/// Formato 2: metas de tempo e os registros delas em segundos. O formato 1 (minutos) ainda chega de aparelhos antigos e
/// é lido com conversão ×60 (spec Segundos_nas_metas_de_tempo_v1 §4.5); só se envia o formato 2.
/// </summary>
public static partial class SyncCodec
{
    /// <summary>Versão que este aparelho envia.</summary>
    public const int FormatVersion = 2;

    /// <summary>Versão mais antiga que este aparelho ainda lê.</summary>
    public const int MinReadableVersion = 1;

    private const int SecondsPerMinute = 60;

    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";
    private const string InstantFormat = "O";

    public static string Encode(Goal goal) => JsonSerializer.Serialize(new GoalDto
    {
        Id = goal.Id,
        Title = goal.Title,
        Recurrence = goal.Recurrence.ToString(),
        Target = goal.Target,
        Measure = goal.Measure.ToString(),
        Unit = goal.Unit,
        Icon = goal.Icon,
        Color = goal.Color.ToString(),
        ReminderEveryMinutes = goal.ReminderEveryMinutes,
        ActiveFrom = goal.ActiveFrom.ToString(TimeFormat, CultureInfo.InvariantCulture),
        ActiveUntil = goal.ActiveUntil.ToString(TimeFormat, CultureInfo.InvariantCulture),
        ActiveDays = (int)goal.ActiveDays,
        StartDate = FormatDate(goal.StartDate),
        EndDate = FormatDate(goal.EndDate),
        CreatedAt = FormatInstant(goal.CreatedAt),
        UpdatedAt = FormatInstant(goal.UpdatedAt),
        ArchivedAt = FormatInstant(goal.ArchivedAt),
    }, SyncJsonContext.Default.GoalDto);

    /// <summary>No formato 1, o alvo da meta de tempo vem em minutos e sai em segundos.</summary>
    /// <exception cref="FormatException">Conteúdo inválido, versão fora de [<see cref="MinReadableVersion"/>, <see cref="FormatVersion"/>] ou alvo que não cabe em segundos.</exception>
    public static Goal DecodeGoal(string json, int version) => Read(() =>
    {
        EnsureReadable(version);
        var dto = JsonSerializer.Deserialize(json, SyncJsonContext.Default.GoalDto) ?? throw new FormatException("Conteúdo vazio.");
        var goal = new Goal
        {
            Id = Required(dto.Id, "id"),
            Title = Required(dto.Title, "title"),
            Recurrence = ParseEnum<Recurrence>(dto.Recurrence, "recurrence"),
            Target = Required(dto.Target, "target"),
            Measure = ParseEnum<Measure>(dto.Measure, "measure"),
            Unit = dto.Unit,
            Icon = Required(dto.Icon, "icon"),
            Color = ParseEnum<GoalColor>(dto.Color, "color"),
            ReminderEveryMinutes = Required(dto.ReminderEveryMinutes, "reminderEveryMinutes"),
            ActiveFrom = ParseTime(Required(dto.ActiveFrom, "activeFrom")),
            ActiveUntil = ParseTime(Required(dto.ActiveUntil, "activeUntil")),
            ActiveDays = (WeekDays)Required(dto.ActiveDays, "activeDays"),
            StartDate = ParseDate(dto.StartDate),
            EndDate = ParseDate(dto.EndDate),
            CreatedAt = ParseInstant(Required(dto.CreatedAt, "createdAt")),
            UpdatedAt = ParseInstant(Required(dto.UpdatedAt, "updatedAt")),
            ArchivedAt = ParseOptionalInstant(dto.ArchivedAt),
        };
        return version == 1 && goal.Measure == Measure.Duration
            ? goal with { Target = ToSeconds(goal.Target) }
            : goal;
    });

    public static string Encode(Checkin checkin) => JsonSerializer.Serialize(new CheckinDto
    {
        Id = checkin.Id,
        GoalId = checkin.GoalId,
        DoneAt = FormatInstant(checkin.DoneAt),
        Amount = checkin.Amount,
        Note = checkin.Note,
        UpdatedAt = FormatInstant(checkin.UpdatedAt),
        Compensation = checkin.Compensation is null
            ? null
            : new CompensationDto
            {
                Period = FormatDate(checkin.Compensation.Period),
                Seen = checkin.Compensation.Seen.ToList(),
            },
    }, SyncJsonContext.Default.CheckinDto);

    public static Checkin DecodeCheckin(string json) => Read(() =>
    {
        var dto = JsonSerializer.Deserialize(json, SyncJsonContext.Default.CheckinDto) ?? throw new FormatException("Conteúdo vazio.");
        return new Checkin
        {
            Id = Required(dto.Id, "id"),
            GoalId = Required(dto.GoalId, "goalId"),
            DoneAt = ParseInstant(Required(dto.DoneAt, "doneAt")),
            Amount = Required(dto.Amount, "amount"),
            Note = dto.Note,
            UpdatedAt = ParseInstant(Required(dto.UpdatedAt, "updatedAt")),
            Compensation = dto.Compensation is null
                ? null
                : new Compensation(
                    ParseDate(Required(dto.Compensation.Period, "compensation.period")) ?? throw new FormatException("Período ausente."),
                    (dto.Compensation.Seen ?? throw new FormatException("Campo ausente: compensation.seen.")).ToList()),
        };
    });

    /// <summary>
    /// Registro do formato 1 de uma meta de tempo: minutos para segundos. Só quem conhece a medida da meta sabe chamar;
    /// <see cref="DecodeCheckin"/> não converte.
    /// </summary>
    /// <exception cref="FormatException">O valor não cabe em segundos.</exception>
    public static Checkin FromFormat1Duration(Checkin checkin)
    {
        ArgumentNullException.ThrowIfNull(checkin);
        return checkin with { Amount = ToSeconds(checkin.Amount) };
    }

    private static void EnsureReadable(int version)
    {
        if (version is < MinReadableVersion or > FormatVersion)
            throw new FormatException($"Versão de formato desconhecida: {version}.");
    }

    private static int ToSeconds(int minutes)
    {
        try
        {
            return checked(minutes * SecondsPerMinute);
        }
        catch (OverflowException e)
        {
            throw new FormatException("O valor do formato 1 não cabe em segundos.", e);
        }
    }

    private static T Read<T>(Func<T> read)
    {
        try { return read(); }
        catch (FormatException) { throw; }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new FormatException("Conteúdo de sincronização inválido.", e);
        }
    }

    private static T Required<T>(T? value, string field) where T : struct =>
        value ?? throw new FormatException($"Campo ausente: {field}.");

    private static string Required(string? value, string field) =>
        value ?? throw new FormatException($"Campo ausente: {field}.");

    private static TEnum ParseEnum<TEnum>(string? value, string field) where TEnum : struct, Enum
    {
        if (value is null) throw new FormatException($"Campo ausente: {field}.");
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
            throw new FormatException($"Valor desconhecido em {field}: {value}.");
        return parsed;
    }

    private static string? FormatDate(DateOnly? date) => date?.ToString(DateFormat, CultureInfo.InvariantCulture);
    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
    private static string? FormatInstant(DateTimeOffset? at) => at?.ToString(InstantFormat, CultureInfo.InvariantCulture);
    private static string FormatInstant(DateTimeOffset at) => at.ToString(InstantFormat, CultureInfo.InvariantCulture);

    private static DateOnly? ParseDate(string? value) =>
        value is null ? null : DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);

    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.ParseExact(value, InstantFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? ParseOptionalInstant(string? value) => value is null ? null : ParseInstant(value);

    internal sealed class GoalDto
    {
        public Guid? Id { get; set; }
        public string? Title { get; set; }
        public string? Recurrence { get; set; }
        public int? Target { get; set; }
        public string? Measure { get; set; }
        public string? Unit { get; set; }
        public string? Icon { get; set; }
        public string? Color { get; set; }
        public int? ReminderEveryMinutes { get; set; }
        public string? ActiveFrom { get; set; }
        public string? ActiveUntil { get; set; }
        public int? ActiveDays { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
        public string? ArchivedAt { get; set; }
    }

    internal sealed class CheckinDto
    {
        public Guid? Id { get; set; }
        public Guid? GoalId { get; set; }
        public string? DoneAt { get; set; }
        public int? Amount { get; set; }
        public string? Note { get; set; }
        public string? UpdatedAt { get; set; }
        public CompensationDto? Compensation { get; set; }
    }

    internal sealed class CompensationDto
    {
        public string? Period { get; set; }
        public List<Guid>? Seen { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(GoalDto))]
    [JsonSerializable(typeof(CheckinDto))]
    internal sealed partial class SyncJsonContext : JsonSerializerContext;
}
