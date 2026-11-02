using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Services;

public class ReminderPlannerTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly ReminderState Nothing = new(false, false, null, null, null);

    // Quarta, 04/11/2026, em São Paulo (UTC-3): hora local → UTC.
    private static DateTimeOffset Local(int day, int hour, int minute = 0) =>
        new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.Zero).AddHours(3);

    private static DateTimeOffset Today(int hour, int minute = 0) => Local(4, hour, minute);

    private static Goal Daily(WeekDays days = WeekDays.All) => G(Recurrence.Daily, days: days) with
    {
        ReminderEveryMinutes = 60,
        ActiveFrom = new TimeOnly(8, 0),
        ActiveUntil = new TimeOnly(20, 0),
    };

    private static DateTimeOffset? Next(Goal goal, ReminderState state, DateTimeOffset now) =>
        ReminderPlanner.Next(goal, state, 10, now, SaoPaulo);

    [Fact]
    public void First_reminder_is_window_start_when_nothing_happened_today() =>
        Next(Daily(), Nothing, Today(7)).Should().Be(Today(8));

    [Fact]
    public void Next_is_last_fire_plus_interval() =>
        Next(Daily(), Nothing with { LastFiredAt = Today(9) }, Today(9, 1)).Should().Be(Today(10));

    [Fact]
    public void Checkin_after_fire_restarts_interval() =>
        Next(Daily(), Nothing with { LastFiredAt = Today(9), LastCheckinAt = Today(9, 40) }, Today(9, 41))
            .Should().Be(Today(10, 40));

    [Fact]
    public void Overdue_reminder_fires_now() =>
        Next(Daily(), Nothing, Today(14)).Should().Be(Today(14));

    [Fact]
    public void After_window_end_goes_to_next_active_day()
    {
        var state = Nothing with { LastFiredAt = Today(19, 30) };
        Next(Daily(), state, Today(19, 31)).Should().Be(Local(5, 8));

        var noThursday = Daily(WeekDays.All & ~WeekDays.Thursday);
        Next(noThursday, state, Today(19, 31)).Should().Be(Local(6, 8));
    }

    [Fact]
    public void Window_end_is_inclusive() =>
        Next(Daily(), Nothing with { LastFiredAt = Today(19) }, Today(19, 1)).Should().Be(Today(20));

    [Fact]
    public void Met_daily_goal_waits_for_next_day() =>
        Next(Daily(), Nothing with { PeriodMet = true }, Today(10)).Should().Be(Local(5, 8));

    [Fact]
    public void Met_weekly_goal_waits_for_next_week()
    {
        var weekly = G(Recurrence.Weekly) with
        {
            ReminderEveryMinutes = 60,
            ActiveFrom = new TimeOnly(8, 0),
            ActiveUntil = new TimeOnly(20, 0),
        };
        Next(weekly, Nothing with { PeriodMet = true }, Today(10)).Should().Be(Local(9, 8));
    }

    [Fact]
    public void Snooze_replaces_interval() =>
        Next(Daily(), Nothing with { LastFiredAt = Today(9), SnoozedAt = Today(9, 5) }, Today(9, 6))
            .Should().Be(Today(9, 15));

    [Fact]
    public void Checkin_after_snooze_wins() =>
        Next(Daily(), Nothing with { LastFiredAt = Today(9), SnoozedAt = Today(9, 5), LastCheckinAt = Today(9, 10) },
                Today(9, 11))
            .Should().Be(Today(10, 10));

    [Fact]
    public void Timer_or_archived_goal_has_no_reminder()
    {
        Next(Daily(), Nothing with { HasTimer = true }, Today(7)).Should().BeNull();
        Next(Daily() with { ArchivedAt = Today(6) }, Nothing, Today(7)).Should().BeNull();
        Next(Daily() with { DeletedAt = Today(6) }, Nothing, Today(7)).Should().BeNull();
    }

    [Fact]
    public void End_date_stops_reminders() =>
        Next(Daily() with { EndDate = D("2026-11-04") }, Nothing, Today(21)).Should().BeNull();

    [Fact]
    public void Window_start_in_dst_gap_uses_first_valid_instant()
    {
        var goal = Daily() with { ActiveFrom = new TimeOnly(2, 30), ActiveUntil = new TimeOnly(10, 0) };
        var now = new DateTimeOffset(2026, 3, 8, 5, 30, 0, TimeSpan.Zero); // 00:30 local (EST)

        ReminderPlanner.Next(goal, Nothing, 10, now, NewYork)
            .Should().Be(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero));
    }

    // Verificação manual da Fase 4: meta salva no meio do horário notificava na hora.
    [Fact]
    public void Goal_created_today_waits_one_interval_from_creation() =>
        Next(Daily(), Nothing with { CreatedTodayAt = Today(14) }, Today(14)).Should().Be(Today(15));

    [Fact]
    public void Goal_created_before_window_starts_at_window_start() =>
        Next(Daily(), Nothing with { CreatedTodayAt = Today(7) }, Today(7, 30)).Should().Be(Today(8));

    [Fact]
    public void Later_fire_wins_over_creation() =>
        Next(Daily(), Nothing with { CreatedTodayAt = Today(9), LastFiredAt = Today(10) }, Today(10, 1)).Should().Be(Today(11));
}
