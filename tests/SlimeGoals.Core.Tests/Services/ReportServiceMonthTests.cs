using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class ReportServiceMonthTests
{
    // Quarta, 04/11/2026 12:00 em São Paulo (UTC-3). Semana: seg 02/11 a dom 08/11.
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static readonly DateOnly Mon = new(2026, 11, 2);
    private static readonly DateOnly Tue = new(2026, 11, 3);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly InMemoryReminderLogRepository _log = new();

    public ReportServiceMonthTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private GoalService Goals => new(_goals, _checkins, _timers, _time);
    private ReportService Service => new(_goals, _checkins, _log, _time);

    /// <summary>Cria a meta como se existisse desde <paramref name="createdOn"/> (padrão: 01/10).</summary>
    private async Task<Goal> Create(string title, Recurrence recurrence = Recurrence.Daily, int target = 1,
        WeekDays days = WeekDays.All, Measure measure = Measure.Quantity, DateOnly? createdOn = null)
    {
        var goal = await Goals.CreateAsync(GoalValidatorTests.Valid() with
        {
            Title = title, Recurrence = recurrence, Target = target, ActiveDays = days,
            Measure = measure, Unit = measure == Measure.Quantity ? "vezes" : null,
        });
        var day = createdOn ?? new DateOnly(2026, 10, 1);
        goal = goal with { CreatedAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(13, 0)), TimeSpan.Zero) + (goal.CreatedAt - Now) };
        await _goals.UpdateAsync(goal);
        _time.Advance(TimeSpan.FromSeconds(1));
        return goal;
    }

    private Task Record(Goal goal, DateOnly day, int amount, TimeOnly? at = null, DateTimeOffset? deletedAt = null) =>
        _checkins.AddAsync(new Checkin
        {
            Id = Guid.NewGuid(), GoalId = goal.Id, Amount = amount, UpdatedAt = Now, DeletedAt = deletedAt,
            DoneAt = new DateTimeOffset(day.ToDateTime(at ?? new TimeOnly(13, 0)), TimeSpan.Zero),
        });

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private async Task RecordRange(Goal goal, DateOnly from, DateOnly to, bool weekdaysOnly = false, params DateOnly[] skip)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (skip.Contains(d) || weekdaysOnly && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await Record(goal, d, 1);
        }
    }

    [Fact]
    public void MonthRange_uses_weeks_whose_monday_is_in_month()
    {
        ReportService.MonthRange(2026, 11).Should().Be(new DateRange(D(11, 2), D(12, 7)));
        ReportService.MonthRange(2026, 10).Should().Be(new DateRange(D(10, 5), D(11, 2)));
        ReportService.MonthRange(2027, 2).Should().Be(new DateRange(new(2027, 2, 1), new(2027, 3, 1)));
    }

    [Fact]
    public async Task Daily_month_counts_days_until_today()
    {
        var goal = await Create("Água");
        await Record(goal, Mon, 1);
        await Record(goal, Tue, 1);

        var report = await Service.GetMonthAsync(2026, 11);

        var row = report.Goals.Single();
        (row.MetPeriods, row.CountedPeriods, row.Percent).Should().Be((2, 2, 100));
        row.WeekPercents.Should().Equal(100, null, null, null, null);
        report.Weeks.Should().HaveCount(5);
        report.Weeks[0].Percent.Should().Be(100);
        report.Weeks[1].Percent.Should().BeNull();
        report.Percent.Should().Be(100);
    }

    [Fact]
    public async Task Weekly_month_counts_weeks_by_monday()
    {
        var goal = await Create("Treino", Recurrence.Weekly, target: 3, createdOn: D(10, 1));
        await Record(goal, D(10, 1), 3);
        await Record(goal, D(10, 6), 3);
        await Record(goal, D(10, 13), 1);
        await Record(goal, D(10, 20), 5);

        var report = await Service.GetMonthAsync(2026, 10);

        var row = report.Goals.Single();
        (row.MetPeriods, row.CountedPeriods, row.Percent).Should().Be((2, 4, 50));
        row.WeekPercents.Should().Equal(100, 33, 100, 0);
        report.Weeks.Select(w => w.Percent).Should().Equal(100, 33, 100, 0);
        report.Weeks[0].Monday.Should().Be(D(10, 5));
    }

    [Fact]
    public async Task Daily_streaks_skip_inactive_days()
    {
        var weekdays = WeekDays.Monday | WeekDays.Tuesday | WeekDays.Wednesday | WeekDays.Thursday | WeekDays.Friday;
        var goal = await Create("Foco", days: weekdays, createdOn: D(10, 1));
        await RecordRange(goal, D(10, 5), D(10, 20), weekdaysOnly: true);
        await RecordRange(goal, D(10, 22), D(10, 30), weekdaysOnly: true);
        await Record(goal, Mon, 1);
        await Record(goal, Tue, 1);

        (await Service.GetMonthAsync(2026, 11)).Goals.Single().Streak.Should().Be(new Streak(9, 12));
        (await Service.GetMonthAsync(2026, 10)).Goals.Single().Streak.Should().Be(new Streak(7, 12));
    }

    [Fact]
    public async Task Weekly_streaks_in_weeks_ignore_open_current_week()
    {
        var goal = await Create("Leitura", Recurrence.Weekly, createdOn: D(10, 1));
        await Record(goal, D(10, 6), 1);
        await Record(goal, D(10, 13), 1);
        await Record(goal, D(10, 27), 1);

        (await Service.GetMonthAsync(2026, 11)).Goals.Single().Streak.Should().Be(new Streak(1, 2));
    }

    [Fact]
    public async Task Archived_goal_counts_only_while_active()
    {
        var goal = await Create("Antiga", createdOn: D(10, 1));
        await RecordRange(goal, D(10, 5), D(10, 15));
        await _goals.UpdateAsync(goal with { ArchivedAt = new DateTimeOffset(2026, 10, 15, 18, 0, 0, TimeSpan.Zero) });

        var october = (await Service.GetMonthAsync(2026, 10)).Goals.Single();
        (october.MetPeriods, october.CountedPeriods, october.Percent).Should().Be((11, 11, 100));
        october.Streak.Should().Be(new Streak(11, 11));

        (await Service.GetMonthAsync(2026, 11)).Goals.Should().BeEmpty();
    }

    [Fact]
    public async Task Top_streak_compares_weeks_as_seven_days()
    {
        (await Service.GetTopStreakAsync()).Should().BeNull();

        var daily = await Create("Diária", createdOn: D(10, 1));
        await RecordRange(daily, D(10, 26), D(11, 3)); // 9 dias
        var weekly = await Create("Semanal", Recurrence.Weekly, createdOn: D(10, 1));
        await Record(weekly, D(10, 21), 1);
        await Record(weekly, D(10, 28), 1); // 2 semanas (19/10 e 26/10)
        var archived = await Create("Arquivada", createdOn: D(10, 1));
        await RecordRange(archived, D(10, 1), D(11, 3));
        await _goals.UpdateAsync(archived with { ArchivedAt = Now });

        var top = await Service.GetTopStreakAsync();

        top.Should().NotBeNull();
        top!.Goal.Id.Should().Be(weekly.Id);
        top.Length.Should().Be(2);
    }
}
