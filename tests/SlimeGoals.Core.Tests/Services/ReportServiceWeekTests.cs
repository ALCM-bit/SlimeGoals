using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class ReportServiceWeekTests
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

    public ReportServiceWeekTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

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

    private Task Fire(Goal goal, DateTimeOffset firedAt, ReminderResponse? response) =>
        _log.AddAsync(new ReminderLogEntry(Guid.NewGuid(), goal.Id, firedAt, response, response is null ? null : firedAt));

    [Fact]
    public async Task Superseded_row_does_not_count()
    {
        var goal = await Create("Água", target: 2);
        await Record(goal, Mon, 1);
        await Record(goal, Mon, 5);
        _checkins.Superseded.Add(_checkins.All[^1].Id);

        var row = (await Service.GetWeekAsync(Today)).Goals.Single();

        (row.Done, row.MetPeriods).Should().Be((1, 0));
    }

    [Fact]
    public async Task Daily_row_counts_met_days_and_today_as_pending()
    {
        var goal = await Create("Água", target: 2);
        await Record(goal, Mon, 2);
        await Record(goal, Tue, 1);

        var row = (await Service.GetWeekAsync(Today)).Goals.Single();
        (row.MetPeriods, row.CountedPeriods, row.Percent).Should().Be((1, 3, 33)); // hoje (qua) pendente
        row.BestDay.Should().BeNull();

        await Record(goal, Today, 2);

        row = (await Service.GetWeekAsync(Today)).Goals.Single();
        (row.MetPeriods, row.CountedPeriods, row.Percent).Should().Be((2, 3, 67));
    }

    [Fact]
    public async Task Daily_row_counts_only_active_days_from_creation()
    {
        var some = await Create("Seg e qua", days: WeekDays.Monday | WeekDays.Wednesday);
        var fresh = await Create("Nova", createdOn: Tue);
        await Record(some, Mon, 1);

        var rows = (await Service.GetWeekAsync(Today)).Goals;

        var a = rows.Single(r => r.Goal.Id == some.Id);
        (a.MetPeriods, a.CountedPeriods, a.Percent).Should().Be((1, 2, 50)); // seg cumprida, qua (hoje) pendente
        var b = rows.Single(r => r.Goal.Id == fresh.Id);
        (b.MetPeriods, b.CountedPeriods, b.Percent).Should().Be((0, 2, 0)); // ter e qua
    }

    [Fact]
    public async Task Weekly_row_shows_net_sum_capped_percent_and_earliest_best_day()
    {
        var goal = await Create("Estudar", Recurrence.Weekly, target: 300, measure: Measure.Duration);
        await Record(goal, Mon, 60);
        await Record(goal, Tue, 120);
        await Record(goal, Tue, -30);
        await Record(goal, Today, 90);

        var row = (await Service.GetWeekAsync(Today)).Goals.Single();
        (row.Done, row.Percent, row.BestDay).Should().Be((240, 80, Tue));

        await Record(goal, Today, 200);

        row = (await Service.GetWeekAsync(Today)).Goals.Single();
        (row.Percent, row.BestDay).Should().Be((100, Today));
    }

    [Fact]
    public async Task Includes_goal_archived_during_week_and_excludes_earlier_archived_and_deleted()
    {
        var first = await Create("Arquivada na semana");
        await Record(first, Mon, 1);
        await _goals.UpdateAsync(first with { ArchivedAt = new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero) });
        await Record(first, Tue, 1, deletedAt: Now);

        var old = await Create("Arquivada antes");
        await _goals.UpdateAsync(old with { ArchivedAt = new DateTimeOffset(2026, 10, 30, 15, 0, 0, TimeSpan.Zero) });

        var deleted = await Create("Excluída");
        await Record(deleted, Mon, 1);
        await _goals.UpdateAsync(deleted with { DeletedAt = Now });

        var report = await Service.GetWeekAsync(Today);

        var row = report.Goals.Single();
        row.Goal.Id.Should().Be(first.Id);
        (row.MetPeriods, row.CountedPeriods, row.Percent).Should().Be((1, 2, 50));
    }

    [Fact]
    public async Task Day_bars_count_goals_with_progress_by_local_day()
    {
        var a = await Create("A", target: 5);
        var b = await Create("B");
        var undone = await Create("Desfeita");
        await Record(a, Mon, 1);
        await Record(a, Mon, 1);
        await Record(a, Mon, 1);          // três lançamentos da mesma meta contam como uma
        await Record(b, Mon, 1);
        await Record(undone, Mon, 1);
        await Record(undone, Mon, -1);    // +1 e −1 no dia: sem progresso
        await Record(a, Tue.AddDays(1), 1, new TimeOnly(2, 30)); // 23:30 locais de 03/11

        var report = await Service.GetWeekAsync(Today);

        report.Days.Select(d => d.Goals.Count).Should().Equal(2, 1, 0, 0, 0, 0, 0);
        report.Days.Select(d => d.Day).Should().Equal(Enumerable.Range(0, 7).Select(i => Mon.AddDays(i)));
    }

    [Fact]
    public async Task Day_bars_list_the_goals_that_compose_them()
    {
        var water = await Create("Água", target: 2);
        var gym = await Create("Treino", Recurrence.Weekly, target: 300, measure: Measure.Duration);
        var fixedOnly = await Create("Corrigida");
        await Record(water, Mon, 1);
        await Record(water, Mon, 1);
        await Record(gym, Mon, 60);
        await Record(gym, Mon, -15);
        await Record(fixedOnly, Mon, 1);
        await Record(fixedOnly, Mon, -1); // desfeita no mesmo dia: não compõe a barra
        await Record(water, Tue, 1);

        var report = await Service.GetWeekAsync(Today);

        var monday = report.Days[0].Goals;
        monday.Select(e => (e.Goal.Title, e.Done, e.Met)).Should().Equal(("Água", 2, (bool?)true), ("Treino", 45, (bool?)null));
        report.Days[1].Goals.Select(e => (e.Goal.Title, e.Done, e.Met)).Should().Equal(("Água", 1, (bool?)false));
        report.Days[2].Goals.Should().BeEmpty();
    }

    [Fact]
    public async Task Counts_reminders_fired_and_answered_in_week()
    {
        var goal = await Create("A");
        var at = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        await Fire(goal, at, ReminderResponse.Done);
        await Fire(goal, at.AddHours(1), ReminderResponse.Snooze);
        await Fire(goal, at.AddHours(2), ReminderResponse.Ignore);
        await Fire(goal, at.AddHours(3), null);
        await Fire(goal, new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero), ReminderResponse.Done); // 01/11 23:00 locais

        var report = await Service.GetWeekAsync(Today);

        (report.RemindersFired, report.RemindersAnswered).Should().Be((4, 2));
    }

    [Fact]
    public async Task Overall_percent_averages_rows_with_value()
    {
        var empty = await Service.GetWeekAsync(Today);
        empty.Percent.Should().BeNull();

        var daily = await Create("Diária", target: 2);
        await Record(daily, Mon, 2);
        await Record(daily, Tue, 1);
        var weekly = await Create("Semanal", Recurrence.Weekly, target: 300, measure: Measure.Duration);
        await Record(weekly, Mon, 240);
        await Create("Nova", createdOn: Today);

        var report = await Service.GetWeekAsync(Today);

        report.Goals.Select(r => r.Percent).Should().Equal(33, 80, 0);
        report.Percent.Should().Be(38);
    }

    [Fact]
    public async Task Past_week_counts_all_seven_days()
    {
        var goal = await Create("A");
        var monday = new DateOnly(2026, 10, 26);
        foreach (var i in new[] { 0, 1, 2, 4, 6 })
            await Record(goal, monday.AddDays(i), 1);

        var report = await Service.GetWeekAsync(monday);

        report.Week.Should().Be(new DateRange(monday, Mon));
        var row = report.Goals.Single();
        (row.MetPeriods, row.CountedPeriods, row.Percent).Should().Be((5, 7, 71));
    }

    [Fact]
    public async Task Week_row_limits_negative_weekly_total()
    {
        var goal = await Create("Semanal", Recurrence.Weekly, target: 3);
        await Record(goal, Mon, -2);

        var row = (await Service.GetWeekAsync(Today)).Goals.Single();

        (row.Done, row.Percent).Should().Be((0, 0));
    }

    [Fact]
    public async Task Daily_negative_day_does_not_lower_week_done()
    {
        var goal = await Create("Diária", target: 2);
        await Record(goal, Mon, -2);
        await Record(goal, Tue, 3);

        (await Service.GetWeekAsync(Today)).Goals.Single().Done.Should().Be(3);
    }

    [Fact]
    public async Task Weekly_negative_day_is_not_limited()
    {
        var goal = await Create("Semanal", Recurrence.Weekly, target: 10);
        await Record(goal, Mon, 5);
        await Record(goal, Tue, -2);

        (await Service.GetWeekAsync(Today)).Goals.Single().Done.Should().Be(3);
    }
}
