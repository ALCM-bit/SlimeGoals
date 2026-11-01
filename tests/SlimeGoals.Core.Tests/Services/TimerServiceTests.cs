using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Core.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class TimerServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();

    public TimerServiceTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private GoalService Goals => new(_goals, _checkins, _timers, _time);
    private CheckinService Checkins => new(_goals, _checkins, _time);
    private TimerService Service => new(_goals, _timers, Checkins, _time);

    private Task<Goal> Study(string title = "Estudar") => Goals.CreateAsync(GoalValidatorTests.Valid() with
        { Title = title, Measure = Measure.Duration, Unit = null, Recurrence = Recurrence.Daily, Target = 60, ActiveDays = WeekDays.All });

    [Theory]
    [InlineData(29, 0)]
    [InlineData(30, 1)]
    [InlineData(25 * 60 + 40, 26)]
    public async Task Start_then_stop_returns_rounded_minutes(int seconds, int minutes)
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        _time.Advance(TimeSpan.FromSeconds(seconds));

        (await Service.StopAsync()).Should().Be(minutes);
        (await Service.GetAsync()).Should().Be(new ActiveTimer(goal.Id, T0, T0.AddSeconds(seconds)));
    }

    [Fact]
    public async Task Stopped_timer_minutes_stay_frozen_while_time_passes()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.StopAsync();
        _time.Advance(TimeSpan.FromHours(2));

        (await Service.StopAsync()).Should().Be(10); // idempotente
        TimerService.Minutes((await Service.GetAsync())!, _time.GetUtcNow()).Should().Be(10);
    }

    [Fact]
    public async Task Start_is_refused_for_non_duration_goal()
    {
        var goal = await Goals.CreateAsync(GoalValidatorTests.Valid() with { ActiveDays = WeekDays.All });

        var act = () => Service.StartAsync(goal.Id);

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("Só metas de tempo têm cronômetro.");
    }

    [Fact]
    public async Task Second_start_reports_running_goal_title()
    {
        var a = await Study("Inglês");
        var b = await Study("Violão");
        await Service.StartAsync(a.Id);

        var act = () => Service.StartAsync(b.Id);

        await act.Should().ThrowAsync<GoalStateException>()
            .WithMessage("Já há um cronômetro em \"Inglês\". Pare-o antes de iniciar outro.");
        (await Service.GetAsync())!.GoalId.Should().Be(a.Id);
    }

    [Fact]
    public async Task Orphan_timer_is_replaced_on_start()
    {
        var goal = await Study();
        await _timers.TryStartAsync(new ActiveTimer(Guid.NewGuid(), T0.AddHours(-1)));

        await Service.StartAsync(goal.Id);

        (await Service.GetAsync()).Should().Be(new ActiveTimer(goal.Id, T0));
    }

    [Fact]
    public async Task Register_adds_checkin_and_clears()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        _time.Advance(TimeSpan.FromMinutes(25));
        await Service.StopAsync();

        (await Service.RegisterAsync(20, "foco")).Should().Be(20);

        (await Service.GetAsync()).Should().BeNull();
        _checkins.All.Should().ContainSingle(c => c.GoalId == goal.Id && c.Amount == 20 && c.Note == "foco");
    }

    [Fact]
    public async Task Register_with_zero_minutes_is_refused_and_keeps_timer()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        await Service.StopAsync();

        var act = () => Service.RegisterAsync(0);

        await act.Should().ThrowAsync<GoalValidationException>();
        (await Service.GetAsync()).Should().NotBeNull();
        _checkins.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Register_without_stopping_is_refused()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);

        var act = () => Service.RegisterAsync(5);
        var stopNothing = () => new TimerService(_goals, new InMemoryTimerRepository(), Checkins, _time).StopAsync();

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("Pare o cronômetro antes de registrar.");
        await stopNothing.Should().ThrowAsync<GoalStateException>().WithMessage("Nenhum cronômetro rodando.");
    }

    [Fact]
    public async Task Discard_clears_without_checkin()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        await Service.StopAsync();

        await Service.DiscardAsync();
        await Service.DiscardAsync();

        (await Service.GetAsync()).Should().BeNull();
        _checkins.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Timer_survives_new_service_instance()
    {
        var goal = await Study();
        await new TimerService(_goals, _timers, Checkins, _time).StartAsync(goal.Id);
        _time.Advance(TimeSpan.FromMinutes(7));

        var reopened = new TimerService(_goals, _timers, Checkins, _time);

        (await reopened.GetAsync())!.StoppedAt.Should().BeNull();
        (await reopened.StopAsync()).Should().Be(7);
    }
}
