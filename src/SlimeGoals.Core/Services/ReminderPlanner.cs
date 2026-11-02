using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>Fatos de hoje (dia local) de uma meta, já levantados pelo ReminderService.</summary>
/// <param name="PeriodMet">Soma do período de avaliação atual ≥ alvo.</param>
/// <param name="HasTimer">Há cronômetro desta meta (rodando ou parado).</param>
/// <param name="LastFiredAt">Último disparo de hoje.</param>
/// <param name="LastCheckinAt">Último registro de hoje.</param>
/// <param name="SnoozedAt">RespondedAt do último disparo de hoje, se a resposta foi Adiar.</param>
/// <param name="CreatedTodayAt">Instante de criação, se a meta foi criada hoje: o intervalo conta a partir dele,
/// para a meta recém-salva não notificar na hora.</param>
public sealed record ReminderState(
    bool PeriodMet,
    bool HasTimer,
    DateTimeOffset? LastFiredAt,
    DateTimeOffset? LastCheckinAt,
    DateTimeOffset? SnoozedAt,
    DateTimeOffset? CreatedTodayAt = null);

/// <summary>Função pura que decide quando uma meta deve lembrar o usuário (spec §9.1–9.2).</summary>
public static class ReminderPlanner
{
    private const int LookaheadDays = 14;

    /// <summary>Próximo disparo (UTC) ou null se a meta não terá lembrete nos próximos 14 dias.</summary>
    public static DateTimeOffset? Next(Goal goal, ReminderState state, int snoozeMinutes,
        DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        if (goal.ArchivedAt is not null || goal.DeletedAt is not null || state.HasTimer)
            return null;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);

        if (!state.PeriodMet && GoalPeriod.RemindsOn(goal, today))
        {
            var start = LocalTime.ToUtc(today, goal.ActiveFrom, zone);
            var end = LocalTime.ToUtc(today, goal.ActiveUntil, zone);
            var candidate = TodayCandidate(goal, state, snoozeMinutes, start);
            if (candidate < nowUtc)
                candidate = nowUtc;
            if (candidate <= end)
                return candidate;
        }

        var thisWeek = WeekMath.StartOfWeek(today);
        for (var offset = 1; offset <= LookaheadDays; offset++)
        {
            var day = today.AddDays(offset);
            if (!GoalPeriod.RemindsOn(goal, day))
                continue;
            if (state.PeriodMet && goal.Recurrence == Recurrence.Weekly && WeekMath.StartOfWeek(day) <= thisWeek)
                continue;
            return LocalTime.ToUtc(day, goal.ActiveFrom, zone);
        }

        return null;
    }

    private static DateTimeOffset TodayCandidate(Goal goal, ReminderState state, int snoozeMinutes,
        DateTimeOffset windowStart)
    {
        if (state.SnoozedAt is { } snoozedAt && (state.LastCheckinAt is not { } checkin || snoozedAt > checkin))
            return snoozedAt.AddMinutes(snoozeMinutes);

        var anchor = new[] { state.LastFiredAt, state.LastCheckinAt, state.CreatedTodayAt }
            .Where(t => t is { } v && v >= windowStart)
            .Max();
        return anchor is { } baseTime ? baseTime.AddMinutes(goal.ReminderEveryMinutes) : windowStart;
    }
}
