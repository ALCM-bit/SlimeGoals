using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class ProgressServiceTests
{
    // Quarta, 04/11/2026 12:00 em São Paulo (UTC-3). Semana: seg 02/11 a dom 08/11.
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 4);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();

    public ProgressServiceTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private GoalService Goals => new(_goals, _checkins, _timers, _time);
    private ProgressService Service => new(_goals, _checkins, _timers, _time);

    /// <summary>
    /// Cria a meta como se existisse desde <paramref name="createdOn"/> (padrão: semana anterior, para os dias
    /// desta semana contarem todos). O relógio não volta, então a data de criação é ajustada no repositório.
    /// </summary>
    private async Task<Goal> Create(string title, Recurrence recurrence = Recurrence.Daily, int target = 1,
        WeekDays days = WeekDays.All, DateOnly? start = null, Measure measure = Measure.Quantity, DateOnly? createdOn = null)
    {
        var goal = await Goals.CreateAsync(GoalValidatorTests.Valid() with
        {
            Title = title, Recurrence = recurrence, Target = target, ActiveDays = days, StartDate = start,
            Measure = measure, Unit = measure == Measure.Quantity ? "vezes" : null,
        });
        var day = createdOn ?? Today.AddDays(-7);
        goal = goal with { CreatedAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(13, 0)), TimeSpan.Zero) + (goal.CreatedAt - Now) };
        await _goals.UpdateAsync(goal);
        _time.Advance(TimeSpan.FromSeconds(1)); // CreatedAt distintos, mesma data
        return goal;
    }

    /// <summary>Registro direto no repositório, às 10:00 locais do dia indicado.</summary>
    private Task Record(Goal goal, DateOnly day, int amount) => _checkins.AddAsync(new Checkin
    {
        Id = Guid.NewGuid(), GoalId = goal.Id, Amount = amount, UpdatedAt = Now,
        DoneAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(13, 0)), TimeSpan.Zero),
    });

    [Fact]
    public async Task Lists_goals_shown_today_pending_first_then_met()
    {
        var a = await Create("A", target: 2);
        var b = await Create("B");
        var c = await Create("C", Recurrence.Weekly, target: 3);
        await Record(b, Today, 1);

        var snapshot = await Service.GetTodayAsync();

        snapshot.Goals.Select(p => p.Goal.Title).Should().Equal("A", "C", "B");
        snapshot.Goals.Select(p => p.IsMet).Should().Equal(false, false, true);
        (snapshot.Week.InProgress, snapshot.Week.Completed).Should().Be((2, 1));
    }

    [Fact]
    public async Task Excludes_archived_and_out_of_period_and_inactive_day_daily()
    {
        await Create("Visível");
        var archived = await Create("Arquivada");
        await Goals.ArchiveAsync(archived.Id);
        await Create("Começa amanhã", start: Today.AddDays(1));
        await Create("Só fim de semana", days: WeekDays.Saturday | WeekDays.Sunday);
        await Create("Semanal só fim de semana", Recurrence.Weekly, days: WeekDays.Saturday | WeekDays.Sunday);

        (await Service.GetTodayAsync()).Goals.Select(p => p.Goal.Title).Should().Equal("Visível", "Semanal só fim de semana");
    }

    [Fact]
    public async Task Done_sums_signed_amounts_of_current_period()
    {
        var daily = await Create("Diária", target: 8);
        var weekly = await Create("Semanal", Recurrence.Weekly, target: 10);
        await Record(daily, Today, 3);
        await Record(daily, Today, -1);
        await Record(daily, Today.AddDays(-1), 5);
        await Record(weekly, Today.AddDays(-2), 4);
        await Record(weekly, Today, 2);
        await Record(weekly, Today.AddDays(-3), 9); // domingo da semana anterior

        var goals = (await Service.GetTodayAsync()).Goals;

        goals.Single(p => p.Goal.Id == daily.Id).Done.Should().Be(2);
        goals.Single(p => p.Goal.Id == weekly.Id).Done.Should().Be(6);
    }

    [Fact]
    public async Task Week_percent_averages_weekly_and_daily_goals()
    {
        var weekly = await Create("Semanal", Recurrence.Weekly, target: 6);
        var daily = await Create("Diária");
        await Record(weekly, Today.AddDays(-1), 3);
        await Record(daily, Today.AddDays(-2), 1);
        await Record(daily, Today.AddDays(-1), 1);

        // semanal 3/6 = 0,5; diária seg 1, ter 1, hoje 0 = 2/3 → média 58%
        (await Service.GetTodayAsync()).Week.Percent.Should().Be(58);
    }

    // Verificação manual da Fase 3: meta diária pendente hoje ficava fora da média e o painel mostrava 100%.
    [Fact]
    public async Task Daily_fraction_counts_each_day_with_partial_progress_capped_at_one()
    {
        var daily = await Create("Diária", target: 4);
        await Record(daily, Today.AddDays(-2), 8); // seg: passou do alvo, conta 1
        await Record(daily, Today.AddDays(-1), 2); // ter: metade
        await Record(daily, Today, 2);             // hoje: metade
        (await Service.GetTodayAsync()).Week.Percent.Should().Be(67); // (1 + 0,5 + 0,5) / 3

        await Record(daily, Today, 2);
        (await Service.GetTodayAsync()).Week.Percent.Should().Be(83); // (1 + 0,5 + 1) / 3
    }

    [Fact]
    public async Task Daily_goal_starting_today_counts_only_today()
    {
        var fresh = await Create("Nova", target: 2, start: Today);
        var weekly = await Create("Semanal", Recurrence.Weekly, target: 4);
        await Record(fresh, Today, 1);
        await Record(weekly, Today, 1);

        (await Service.GetTodayAsync()).Week.Percent.Should().Be(38); // (0,5 + 0,25) / 2 = 37,5
    }

    // Uma meta criada na quarta não conta segunda e terça como 0: ela nem existia nesses dias.
    [Fact]
    public async Task Daily_goal_created_mid_week_counts_from_creation_day()
    {
        var fresh = await Create("Água", target: 8, createdOn: Today);
        await Record(fresh, Today, 8);

        (await Service.GetTodayAsync()).Week.Percent.Should().Be(100);
    }

    [Fact]
    public async Task Creation_day_uses_local_date()
    {
        // 23:30 de terça em São Paulo é 02:30 UTC de quarta: a meta conta desde terça.
        var goal = await Create("Água", createdOn: Today);
        await _goals.UpdateAsync(goal with { CreatedAt = new DateTimeOffset(2026, 11, 4, 2, 30, 0, TimeSpan.Zero) });
        await Record(goal, Today, 1);

        (await Service.GetTodayAsync()).Week.Percent.Should().Be(50); // ter 0, qua 1
    }

    [Fact]
    public async Task No_goals_gives_zero_percent() =>
        (await Service.GetTodayAsync()).Week.Should().Be(new WeekSummary(0, 0, 0));

    // Revisão final #1: cronômetro de meta fora da lista de hoje continua visível (senão bloqueia os outros sem saída).
    [Fact]
    public async Task Timer_is_returned_with_its_goal_even_when_goal_is_not_listed()
    {
        var study = await Create("Estudar", target: 60, measure: Measure.Duration);
        await _timers.TryStartAsync(new ActiveTimer(study.Id, Now));
        var listed = await Service.GetTodayAsync();
        (listed.Timer, listed.TimerGoal?.Id).Should().Be((new ActiveTimer(study.Id, Now), study.Id));

        var weekend = await Create("Fim de semana", target: 60, measure: Measure.Duration, days: WeekDays.Saturday | WeekDays.Sunday);
        await _timers.SaveAsync(new ActiveTimer(weekend.Id, Now));
        var hidden = await Service.GetTodayAsync();
        (hidden.Timer, hidden.TimerGoal?.Title).Should().Be((new ActiveTimer(weekend.Id, Now), "Fim de semana"));

        await _timers.SaveAsync(new ActiveTimer(Guid.NewGuid(), Now));
        var orphan = await Service.GetTodayAsync();
        (orphan.Timer is not null, orphan.TimerGoal).Should().Be((true, (Goal?)null));
    }
}
