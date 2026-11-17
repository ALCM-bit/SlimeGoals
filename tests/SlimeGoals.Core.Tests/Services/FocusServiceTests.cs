using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Core.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class FocusServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers;
    private readonly InMemoryFocusSessionRepository _sessions;
    private readonly InMemorySettingsRepository _settings = new();
    private readonly DataChangeNotifier _changes = new();
    private int _notified;

    public FocusServiceTests()
    {
        _timers = new(_checkins);
        _sessions = new(_checkins, _timers);
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        _changes.Changed += (_, _) => _notified++;
    }

    private GoalService Goals => new(_goals, _checkins, _timers, _time);
    private CheckinService Checkins => new(_goals, _checkins, _time);
    private FocusService Service => new(_goals, _sessions, _timers, Checkins, new AppSettings(_settings), _time, _changes);

    private static FocusBlock Focus(int minutes) => new(FocusBlockKind.Focus, minutes);
    private static FocusBlock Break(int minutes) => new(FocusBlockKind.Break, minutes);

    private Task<Goal> Study(string title = "Estudar") => Goals.CreateAsync(GoalValidatorTests.Valid() with
        { Title = title, Measure = Measure.Duration, Unit = null, Recurrence = Recurrence.Daily, Target = 60, ActiveDays = WeekDays.All });

    private async Task<FocusSession> SessionAsync() => (await _sessions.GetAsync())!;

    [Fact]
    public async Task Start_saves_the_plan_as_next_suggestion_but_not_the_goal()
    {
        var goal = await Study();
        var plan = new FocusPlan([Focus(50), Break(10)], 2);

        await Service.StartAsync(plan, goal.Id);

        (await Service.SuggestedPlanAsync()).Should().Be(plan);
        (await _settings.GetAsync("focus.plan")).Should().NotContain(goal.Id.ToString());
        var session = await SessionAsync();
        session.Should().Be(new FocusSession(plan, goal.Id, T0, 0, T0, T0, TimeSpan.Zero, null, null, null, null, 0));
        _notified.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Start_validates_the_plan_and_the_goal()
    {
        var count = await Goals.CreateAsync(GoalValidatorTests.Valid() with { ActiveDays = WeekDays.All });

        var badPlan = () => Service.StartAsync(new FocusPlan([Break(5)], 1), null);
        var notTime = () => Service.StartAsync(FocusPlan.Default, count.Id);

        (await badPlan.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should()
            .ContainSingle().Which.Message.Should().Be("Inclua pelo menos um bloco de foco.");
        await notTime.Should().ThrowAsync<GoalStateException>().WithMessage("Só metas de tempo têm cronômetro.");
        (await _sessions.GetAsync()).Should().BeNull();
        (await _settings.GetAsync("focus.plan")).Should().BeNull();
    }

    [Fact]
    public async Task Start_with_a_live_timer_is_refused_with_its_title()
    {
        var english = await Study("Inglês");
        await new TimerService(_goals, _timers, Checkins, _time).StartAsync(english.Id);

        var act = () => Service.StartAsync(FocusPlan.Default, null);

        await act.Should().ThrowAsync<GoalStateException>()
            .WithMessage("Já há um cronômetro em \"Inglês\". Registre ou descarte no Hoje antes de começar o Pomodoro.");
        (await _sessions.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Timer_conflict_names_a_live_or_stopped_timer_and_ignores_orphans()
    {
        const string message = "Já há um cronômetro em \"Inglês\". Registre ou descarte no Hoje antes de começar o Pomodoro.";
        (await Service.TimerConflictAsync()).Should().BeNull();

        var english = await Study("Inglês");
        var timers = new TimerService(_goals, _timers, Checkins, _time);
        await timers.StartAsync(english.Id);
        (await Service.TimerConflictAsync()).Should().Be(message);
        await timers.StopAsync();
        (await Service.TimerConflictAsync()).Should().Be(message);

        await Goals.ArchiveAsync(english.Id);
        await _timers.TryStartAsync(new ActiveTimer(english.Id, T0));
        (await Service.TimerConflictAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Task_suggestions_come_from_the_session_goal_or_from_every_goal()
    {
        var study = await Study();
        var english = await Study("Inglês");
        await Checkins.AdjustAsync(study.Id, 10, "Capítulo 3");
        _time.Advance(TimeSpan.FromMinutes(1));
        await Checkins.AdjustAsync(english.Id, 10, "Listening");
        (await Service.TaskSuggestionsAsync()).Should().BeEmpty();

        await Service.StartAsync(FocusPlan.Default, study.Id);
        (await Service.TaskSuggestionsAsync()).Should().Equal("Capítulo 3");

        await _sessions.ClearAsync();
        await Service.StartAsync(FocusPlan.Default, null);
        (await Service.TaskSuggestionsAsync()).Should().Equal("Listening", "Capítulo 3");
    }

    [Fact]
    public async Task Orphan_timer_gives_way_to_the_session()
    {
        await _timers.TryStartAsync(new ActiveTimer(Guid.NewGuid(), T0.AddHours(-1)));

        await Service.StartAsync(FocusPlan.Default, null);

        (await _timers.GetAsync()).Should().BeNull();
        (await _sessions.GetAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Second_start_is_refused()
    {
        await Service.StartAsync(FocusPlan.Default, null);

        var act = () => Service.StartAsync(FocusPlan.Default, null);

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("Já há uma sessão de Pomodoro aberta.");
    }

    [Fact]
    public async Task Operations_without_a_session_are_refused()
    {
        var end = () => Service.EndAsync();
        var pause = () => Service.PauseAsync();

        await end.Should().ThrowAsync<GoalStateException>().WithMessage("Nenhuma sessão de Pomodoro aberta.");
        await pause.Should().ThrowAsync<GoalStateException>().WithMessage("Nenhuma sessão de Pomodoro aberta.");
    }

    [Fact]
    public async Task Advancing_a_focus_records_whole_minutes_and_carries_seconds()
    {
        var goal = await Study();
        await Service.StartAsync(FocusPlan.Default, goal.Id);
        _time.Advance(TimeSpan.FromSeconds(25 * 60 + 40));
        var now = _time.GetUtcNow();

        (await Service.AdvanceAsync()).Should().BeNull();

        var checkin = _checkins.All.Should().ContainSingle().Which;
        checkin.GoalId.Should().Be(goal.Id);
        checkin.Amount.Should().Be(25);
        var session = await SessionAsync();
        session.Step.Should().Be(1);
        session.Carry.Should().Be(TimeSpan.FromSeconds(40));
        session.BlockStartedAt.Should().Be(now);
        session.CutAt.Should().Be(now);
        session.Revision.Should().Be(1);
        var chunk = (await Service.ChunksAsync()).Should().ContainSingle().Which;
        (chunk.Minutes, chunk.Task, chunk.EndedAt, chunk.CheckinId).Should().Be((25, (string?)null, now, (Guid?)checkin.Id));
    }

    [Fact]
    public async Task Carry_crosses_the_break_into_the_next_focus()
    {
        var goal = await Study();
        await Service.StartAsync(FocusPlan.Default, goal.Id);
        _time.Advance(TimeSpan.FromSeconds(25 * 60 + 40));
        await Service.AdvanceAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await Service.AdvanceAsync(); // fim da pausa: não corta
        _time.Advance(TimeSpan.FromSeconds(4 * 60 + 30));

        await Service.SwitchTaskAsync("Revisar");

        _checkins.All.Select(c => c.Amount).Should().Equal(25, 5);
        (await SessionAsync()).Carry.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(25 * 60 + 29, 25)]
    [InlineData(25 * 60 + 30, 26)]
    public async Task Last_cut_rounds_to_the_nearest_minute(int seconds, int minutes)
    {
        var goal = await Study();
        await Service.StartAsync(new FocusPlan([Focus(25)], 1), goal.Id);
        _time.Advance(TimeSpan.FromSeconds(seconds));

        var summary = await Service.AdvanceAsync();

        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(minutes);
        summary.Should().BeEquivalentTo(new FocusSummary(minutes, [new FocusSummaryLine(null, minutes)], Assigned: true));
        (await _sessions.GetAsync()).Should().BeNull();
        (await _sessions.ChunksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Last_focus_rounds_even_when_the_session_ends_on_a_break()
    {
        var goal = await Study();
        await Service.StartAsync(new FocusPlan([Focus(10), Break(5)], 2), goal.Id);
        _time.Advance(TimeSpan.FromSeconds(10 * 60 + 20));
        await Service.AdvanceAsync(); // foco do meio: trunca e guarda 20 s
        _time.Advance(TimeSpan.FromMinutes(5));
        await Service.AdvanceAsync();
        _time.Advance(TimeSpan.FromSeconds(10 * 60 + 20));
        await Service.AdvanceAsync(); // último foco: 10 min 40 s arredondam para 11

        _checkins.All.Select(c => c.Amount).Should().Equal(10, 11);
        (await SessionAsync()).Carry.Should().Be(TimeSpan.Zero);
        _time.Advance(TimeSpan.FromMinutes(5));

        var summary = await Service.AdvanceAsync();

        _checkins.All.Select(c => c.Amount).Should().Equal(10, 11);
        summary.Should().BeEquivalentTo(new FocusSummary(21, [new FocusSummaryLine(null, 21)], Assigned: true));
        (await _sessions.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Switch_cuts_the_chunk_with_the_old_task_and_keeps_counting()
    {
        var goal = await Study();
        await Service.StartAsync(FocusPlan.Default, goal.Id);
        await Service.SetTaskAsync("  Ler  ");
        _time.Advance(TimeSpan.FromSeconds(10 * 60 + 20));
        var switchedAt = _time.GetUtcNow();

        await Service.SwitchTaskAsync("Escrever");

        var first = _checkins.All.Should().ContainSingle().Which;
        (first.Amount, first.Note).Should().Be((10, "Ler"));
        var session = await SessionAsync();
        (session.Step, session.Task, session.CutAt, session.BlockStartedAt, session.Carry)
            .Should().Be((0, "Escrever", switchedAt, T0, TimeSpan.FromSeconds(20)));

        _time.Advance(TimeSpan.FromSeconds(15 * 60 + 20)); // 20 s depois do fim do bloco
        await Service.AdvanceAsync();

        _checkins.All.Select(c => (c.Amount, c.Note)).Should().Equal((10, "Ler"), (15, "Escrever"));
    }

    [Fact]
    public async Task Switch_while_paused_cuts_at_the_pause()
    {
        var goal = await Study();
        await Service.StartAsync(FocusPlan.Default, goal.Id);
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.PauseAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        await Service.SwitchTaskAsync("B");
        _time.Advance(TimeSpan.FromMinutes(5));
        await Service.ResumeAsync();
        _time.Advance(TimeSpan.FromMinutes(15));

        await Service.AdvanceAsync();

        _checkins.All.Select(c => (c.Amount, c.Note)).Should().Equal((10, (string?)null), (15, "B"));
    }

    [Fact]
    public async Task Ending_without_goal_and_without_time_closes_the_session()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        _time.Advance(TimeSpan.FromSeconds(20));

        var summary = await Service.EndAsync();

        summary.Should().BeEquivalentTo(new FocusSummary(0, [], Assigned: true));
        (await _sessions.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Paused_time_never_counts()
    {
        var goal = await Study();
        await Service.StartAsync(FocusPlan.Default, goal.Id);
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.PauseAsync();
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.ResumeAsync();
        var session = await SessionAsync();
        (session.PausedAt, session.BlockStartedAt, session.CutAt).Should().Be((null, T0.AddMinutes(10), T0.AddMinutes(10)));
        _time.Advance(TimeSpan.FromMinutes(15));

        await Service.AdvanceAsync();

        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(25);
    }

    [Fact]
    public async Task Pause_is_refused_while_waiting_for_the_tap()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        _time.Advance(TimeSpan.FromMinutes(26));

        var act = () => Service.PauseAsync();

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("A sessão mudou. Confira na aba Tempo.");
        (await SessionAsync()).PausedAt.Should().BeNull();
    }

    [Fact]
    public async Task Forgotten_focus_needs_review_and_records_the_reviewed_time()
    {
        var goal = await Study();
        await Service.StartAsync(FocusPlan.Default, goal.Id);
        _time.Advance(TimeSpan.FromMinutes(25 + 40));

        var unreviewed = () => Service.AdvanceAsync();
        // Um tipo próprio: a tela reconhece a recusa sem comparar o texto, e quem só conhece GoalStateException mostra a mensagem.
        (await unreviewed.Should().ThrowAsync<FocusReviewRequiredException>())
            .Which.Should().BeAssignableTo<GoalStateException>()
            .Which.Message.Should().Be("Confira o tempo do último trecho antes de seguir.");
        _checkins.All.Should().BeEmpty();

        await Service.AdvanceAsync(30);

        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(30);
        var session = await SessionAsync();
        (session.Step, session.Carry, session.BlockStartedAt).Should().Be((1, TimeSpan.Zero, _time.GetUtcNow()));
    }

    [Fact]
    public async Task Reviewed_time_over_24h_is_refused_without_recording()
    {
        var goal = await Study();
        await Service.StartAsync(FocusPlan.Default, goal.Id);
        _time.Advance(TimeSpan.FromHours(30));
        var before = await SessionAsync();

        var act = () => Service.AdvanceAsync(CheckinService.MaxDurationAmount + 1);

        (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should()
            .ContainSingle().Which.Message.Should().Be("Valor fora do limite.");
        _checkins.All.Should().BeEmpty();
        (await SessionAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Goal_archived_mid_session_holds_the_next_chunks()
    {
        var goal = await Study();
        await Service.StartAsync(new FocusPlan([Focus(10), Break(5)], 2), goal.Id);
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.AdvanceAsync();
        await Goals.ArchiveAsync(goal.Id);
        _time.Advance(TimeSpan.FromMinutes(5));
        await Service.AdvanceAsync();
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.AdvanceAsync();
        _time.Advance(TimeSpan.FromMinutes(5));

        var summary = await Service.AdvanceAsync();

        _checkins.All.Should().ContainSingle().Which.GoalId.Should().Be(goal.Id);
        (await Service.ChunksAsync()).Select(c => (c.Minutes, c.CheckinId is null)).Should().Equal((10, false), (10, true));
        var session = await SessionAsync();
        (session.FinishedAt, session.GoalId).Should().Be((_time.GetUtcNow(), null));
        summary.Should().BeEquivalentTo(new FocusSummary(10, [new FocusSummaryLine(null, 10)], Assigned: false));
    }

    [Fact]
    public async Task Unassigned_session_waits_for_a_destination_then_assigns_each_chunk_with_its_note()
    {
        await Service.StartAsync(new FocusPlan([Focus(10)], 2), null);
        await Service.SetTaskAsync("Ler");
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.AdvanceAsync();
        var goal = await Study();
        var early = () => Service.AssignAsync(goal.Id);
        await early.Should().ThrowAsync<GoalStateException>().WithMessage("A sessão mudou. Confira na aba Tempo.");
        await Service.SetTaskAsync("Escrever");
        _time.Advance(TimeSpan.FromMinutes(10));

        var summary = await Service.AdvanceAsync();

        summary.Should().BeEquivalentTo(new FocusSummary(20,
            [new FocusSummaryLine("Ler", 10), new FocusSummaryLine("Escrever", 10)], Assigned: false));
        _checkins.All.Should().BeEmpty();
        (await SessionAsync()).FinishedAt.Should().Be(_time.GetUtcNow());

        _time.Advance(TimeSpan.FromHours(1));
        var total = await Service.AssignAsync(goal.Id);

        total.Should().Be(20);
        _checkins.All.Select(c => (c.GoalId, c.Amount, c.Note, c.DoneAt)).Should().Equal(
            (goal.Id, 10, "Ler", _time.GetUtcNow()), (goal.Id, 10, "Escrever", _time.GetUtcNow()));
        (await _sessions.GetAsync()).Should().BeNull();
        (await _sessions.ChunksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Discard_removes_the_held_chunks_and_the_session()
    {
        await Service.StartAsync(new FocusPlan([Focus(10)], 1), null);
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.AdvanceAsync();
        _notified = 0;

        await Service.DiscardAsync();

        (await _sessions.GetAsync()).Should().BeNull();
        (await _sessions.ChunksAsync()).Should().BeEmpty();
        _checkins.All.Should().BeEmpty();
        _notified.Should().Be(1);
    }

    [Fact]
    public async Task Stale_revision_reports_the_session_changed_and_writes_nothing()
    {
        var goal = await Study();
        await Service.StartAsync(FocusPlan.Default, goal.Id);
        _time.Advance(TimeSpan.FromMinutes(25));
        _sessions.BeforeUpdate = _sessions.ChangeElsewhere;
        _notified = 0;

        var act = () => Service.AdvanceAsync();

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("A sessão mudou. Confira na aba Tempo.");
        _checkins.All.Should().BeEmpty();
        (await _sessions.ChunksAsync()).Should().BeEmpty();
        (await SessionAsync()).Step.Should().Be(0);
        _notified.Should().Be(0);
    }

    [Fact]
    public async Task Summary_groups_minutes_by_task()
    {
        await Service.StartAsync(new FocusPlan([Focus(60)], 1), null);
        await Service.SetTaskAsync("A");
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.SwitchTaskAsync("B");
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.SwitchTaskAsync("A");
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.SwitchTaskAsync(" ");
        _time.Advance(TimeSpan.FromMinutes(5));

        var summary = await Service.EndAsync();

        summary.Should().BeEquivalentTo(new FocusSummary(35,
            [new FocusSummaryLine("A", 20), new FocusSummaryLine("B", 10), new FocusSummaryLine(null, 5)], Assigned: false),
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Task_over_the_note_limit_is_refused()
    {
        await Service.StartAsync(FocusPlan.Default, null);

        var act = () => Service.SetTaskAsync(new string('a', CheckinService.MaxNoteLength + 1));

        (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should()
            .ContainSingle().Which.Message.Should().Be("A nota deve ter até 200 caracteres.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("não é json")]
    [InlineData("""{"blocks":[],"repeat":4}""")]
    [InlineData("""{"blocks":[{"kind":"focus","minutes":25}],"repeat":0}""")]
    public async Task Suggested_plan_falls_back_to_default_on_bad_json(string? raw)
    {
        if (raw is not null)
            await _settings.SetAsync("focus.plan", raw);

        (await Service.SuggestedPlanAsync()).Should().Be(FocusPlan.Default);
    }
}
