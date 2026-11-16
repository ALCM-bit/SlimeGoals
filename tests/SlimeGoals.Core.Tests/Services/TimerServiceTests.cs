using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
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
    private readonly InMemoryTimerRepository _timers;

    public TimerServiceTests()
    {
        _timers = new(_checkins);
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
    }

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

    // Tarefas no cronômetro (specs/Tarefas_no_cronometro_v1.md).

    [Fact]
    public async Task Set_task_trims_and_is_kept_on_the_timer()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);

        await Service.SetTaskAsync("  Inglês ");

        (await Service.GetAsync())!.Task.Should().Be("Inglês");
    }

    [Fact]
    public async Task Set_task_with_only_spaces_clears_it()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        await Service.SetTaskAsync("Inglês");

        await Service.SetTaskAsync("   ");

        (await Service.GetAsync())!.Task.Should().BeNull();
    }

    [Fact]
    public async Task Set_task_accepts_emoji_and_200_characters()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);

        await Service.SetTaskAsync(new string('a', 200));
        (await Service.GetAsync())!.Task.Should().HaveLength(200);
        await Service.SetTaskAsync("📖 Ler");
        (await Service.GetAsync())!.Task.Should().Be("📖 Ler");
    }

    [Fact]
    public async Task Set_task_longer_than_200_characters_is_refused()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);

        var act = () => Service.SetTaskAsync(new string('a', 201));

        (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should().ContainSingle()
            .Which.Message.Should().Be("A tarefa deve ter até 200 caracteres.");
        (await Service.GetAsync())!.Task.Should().BeNull();
    }

    [Fact]
    public async Task Set_task_without_timer_fails()
    {
        var act = () => Service.SetTaskAsync("Inglês");

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("Nenhum cronômetro rodando.");
        (await Service.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Switch_registers_whole_minutes_with_the_old_task_and_carries_the_seconds()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        await Service.SetTaskAsync("Inglês");
        _time.Advance(TimeSpan.FromSeconds(25 * 60 + 40));

        (await Service.SwitchTaskAsync("Capítulo 3")).Should().Be(25);

        _checkins.All.Should().ContainSingle().Which.Should().Match<Checkin>(c => c.Amount == 25 && c.Note == "Inglês");
        (await Service.GetAsync()).Should().Be(new ActiveTimer(goal.Id, T0.AddMinutes(25), null, "Capítulo 3"));
    }

    [Fact]
    public async Task Switch_then_stop_adds_up_to_the_whole_elapsed_time()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        _time.Advance(TimeSpan.FromSeconds(10 * 60 + 50));
        await Service.SwitchTaskAsync("B");
        _time.Advance(TimeSpan.FromSeconds(19 * 60 + 30)); // 30 min 20 s desde o início

        var minutes = await Service.StopAsync();
        await Service.RegisterAsync(minutes);

        _checkins.All.Sum(c => c.Amount).Should().Be(30);
    }

    [Fact]
    public async Task Switch_under_a_minute_only_renames()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        _time.Advance(TimeSpan.FromSeconds(40));

        (await Service.SwitchTaskAsync("B")).Should().Be(0);

        _checkins.All.Should().BeEmpty();
        (await Service.GetAsync()).Should().Be(new ActiveTimer(goal.Id, T0, null, "B"));
    }

    [Fact]
    public async Task Switch_with_a_stopped_timer_fails_and_registers_nothing()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Service.StopAsync();

        var act = () => Service.SwitchTaskAsync("B");

        // Só há ⇄ com o cronômetro rodando: se ele está parado, outro processo o parou.
        await act.Should().ThrowAsync<GoalStateException>().WithMessage("O cronômetro mudou. Confira em Hoje.");
        _checkins.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Switch_refused_by_checkin_validation_keeps_the_timer()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        await Service.SetTaskAsync("A");
        var before = await Service.GetAsync();
        _time.Advance(TimeSpan.FromMinutes(24 * 60 + 1));

        var act = () => Service.SwitchTaskAsync("B");

        await act.Should().ThrowAsync<GoalValidationException>();
        (await Service.GetAsync()).Should().Be(before);
        _checkins.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Switch_loses_the_race_when_the_timer_changed()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        _time.Advance(TimeSpan.FromMinutes(5));
        _timers.FailSwitches = true;

        var act = () => Service.SwitchTaskAsync("B");

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("O cronômetro mudou. Confira em Hoje.");
        _checkins.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Switch_without_timer_fails()
    {
        var act = () => Service.SwitchTaskAsync("B");

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("O cronômetro mudou. Confira em Hoje.");
    }

    [Fact]
    public async Task Stop_racing_a_switch_keeps_the_switched_timer()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        await Service.SetTaskAsync("A");
        _time.Advance(TimeSpan.FromMinutes(5));
        // O "Parar" da notificação lê o cronômetro; antes de gravar, o Hoje troca para "B" e registra os 5 min de "A".
        var racing = new RacingTimers(_timers, () => Service.SwitchTaskAsync("B"));

        var minutes = await new TimerService(_goals, racing, Checkins, _time).StopAsync();

        minutes.Should().Be(0);
        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(5);
        (await _timers.GetAsync()).Should().Be(new ActiveTimer(goal.Id, T0.AddMinutes(5), T0.AddMinutes(5), "B"));
    }

    [Fact]
    public async Task Switch_under_a_minute_racing_a_stop_fails_and_keeps_the_name()
    {
        var goal = await Study();
        await Service.StartAsync(goal.Id);
        await Service.SetTaskAsync("A");
        _time.Advance(TimeSpan.FromSeconds(40));
        var racing = new RacingTimers(_timers, () => Service.StopAsync());

        var act = () => new TimerService(_goals, racing, Checkins, _time).SwitchTaskAsync("B");

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("O cronômetro mudou. Confira em Hoje.");
        (await _timers.GetAsync()).Should().Be(new ActiveTimer(goal.Id, T0, T0.AddSeconds(40), "A"));
    }

    /// <summary>Outro processo age logo depois da primeira leitura do cronômetro (a corrida entre ler e gravar).</summary>
    private sealed class RacingTimers(ITimerRepository inner, Func<Task> race) : ITimerRepository
    {
        private Func<Task>? _race = race;

        public async Task<ActiveTimer?> GetAsync(CancellationToken ct = default)
        {
            var timer = await inner.GetAsync(ct);
            if (Interlocked.Exchange(ref _race, null) is { } r)
                await r();
            return timer;
        }

        public Task<bool> TryStartAsync(ActiveTimer timer, CancellationToken ct = default) => inner.TryStartAsync(timer, ct);
        public Task SaveAsync(ActiveTimer timer, CancellationToken ct = default) => inner.SaveAsync(timer, ct);
        public Task<bool> StopAsync(DateTimeOffset at, CancellationToken ct = default) => inner.StopAsync(at, ct);
        public Task ClearAsync(CancellationToken ct = default) => inner.ClearAsync(ct);
        public Task<bool> SetTaskAsync(string? task, CancellationToken ct = default) => inner.SetTaskAsync(task, ct);
        public Task<bool> SwitchAsync(DateTimeOffset expectedStartedAt, ActiveTimer next, Checkin? chunk, CancellationToken ct = default) =>
            inner.SwitchAsync(expectedStartedAt, next, chunk, ct);
    }

    [Fact]
    public async Task Task_suggestions_come_from_the_goal_notes()
    {
        var goal = await Study();
        await Checkins.AdjustAsync(goal.Id, 10, "Inglês");

        (await Service.TaskSuggestionsAsync(goal.Id)).Should().Equal("Inglês");
    }
}
