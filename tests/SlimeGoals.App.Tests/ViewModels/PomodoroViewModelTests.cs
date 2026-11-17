using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class PomodoroViewModelTests
{
    // Quarta, 04/11/2026 12:00 em São Paulo (UTC-3).
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers;
    private readonly InMemoryFocusSessionRepository _sessions;
    private readonly InMemorySettingsRepository _settings = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly PageNavigator _navigator = new();

    public PomodoroViewModelTests()
    {
        _timers = new(_checkins);
        _sessions = new(_checkins, _timers);
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
    }

    private GoalService GoalService => new(_goals, _checkins, _timers, _time);
    private CheckinService Checkins => new(_goals, _checkins, _time);
    private TimerService Timers => new(_goals, _timers, Checkins, _time, _changes, _sessions);
    private FocusService Focus => new(_goals, _sessions, _timers, Checkins, new AppSettings(_settings), _time, _changes);

    private async Task<PomodoroViewModel> Loaded()
    {
        var vm = new PomodoroViewModel(Focus, GoalService, _time, _changes, _navigator);
        await vm.LoadAsync();
        return vm;
    }

    private Task<Goal> Create(string title, Measure measure = Measure.Duration, WeekDays days = WeekDays.All) =>
        GoalService.CreateAsync(new GoalDraft(title, Recurrence.Daily, measure == Measure.Completion ? 1 : 60, "book",
            GoalColor.Purple, 60, new TimeOnly(8, 0), new TimeOnly(20, 0), days, null, null, measure,
            measure == Measure.Quantity ? "vezes" : null));

    private static IEnumerable<(FocusBlockKind Kind, int? Minutes)> Plan(PomodoroViewModel vm) =>
        vm.Blocks.Select(b => (b.Kind, b.Minutes()));

    [Fact]
    public async Task First_open_suggests_25_5_four_times_without_goal()
    {
        var vm = await Loaded();

        Plan(vm).Should().Equal((FocusBlockKind.Focus, 25), (FocusBlockKind.Break, 5));
        vm.Blocks.Select(b => (b.Text, b.IsFocus)).Should().Equal(("25 min", true), ("5 min", false));
        vm.RepeatInput.Should().Be(4);
        vm.SelectedGoal.Should().Be(new FocusGoalOption(null, "Sem meta"));
        (vm.IsPreparing, vm.IsRunning, vm.IsFinished).Should().Be((true, false, false));
        (vm.CanStart, vm.StartBlockedReason, vm.Error).Should().Be((true, null, null));
    }

    [Fact]
    public async Task Last_started_plan_comes_back_and_goal_does_not()
    {
        var study = await Create("Estudar");
        var first = await Loaded();
        first.SelectedGoal = first.GoalOptions.Single(o => o.Id == study.Id);
        first.Blocks.RemoveAt(1);
        first.Blocks[0].HoursInput = 0;
        first.Blocks[0].MinutesInput = 50;
        first.AddBreakCommand.Execute(null);
        first.RepeatInput = 2;

        await first.StartCommand.ExecuteAsync(null);
        (first.IsPreparing, first.IsRunning, first.Error).Should().Be((false, true, null));
        await Focus.EndAsync();

        var reopened = await Loaded();
        Plan(reopened).Should().Equal((FocusBlockKind.Focus, 50), (FocusBlockKind.Break, 5));
        reopened.RepeatInput.Should().Be(2);
        reopened.SelectedGoal.Id.Should().BeNull();
    }

    [Fact]
    public async Task Block_editor_takes_hours_and_minutes()
    {
        var vm = await Loaded();
        var focus = vm.Blocks[0];
        vm.EditBlockCommand.Execute(vm.Blocks[1]);

        vm.EditBlockCommand.Execute(focus);
        focus.HoursInput = 1;
        focus.MinutesInput = 15;
        vm.CloseBlockEditorCommand.Execute(focus);

        vm.Blocks.Select(b => b.IsEditing).Should().Equal(false, false);
        focus.Text.Should().Be("1 h 15 min");
        await vm.StartCommand.ExecuteAsync(null);
        (await _sessions.GetAsync())!.Plan.Blocks.Should().Equal(
            new FocusBlock(FocusBlockKind.Focus, 75), new FocusBlock(FocusBlockKind.Break, 5));
    }

    [Fact]
    public async Task Block_being_edited_survives_a_background_reload()
    {
        var vm = await Loaded();
        var focus = vm.Blocks[0];
        vm.EditBlockCommand.Execute(focus);
        (focus.HoursInput, focus.MinutesInput) = (1, 10);
        vm.RepeatInput = 2;

        _changes.NotifyChanged(); // a sincronização, ou outra página, grava algo
        await vm.LoadCommand.ExecutionTask!;

        vm.Blocks[0].Should().BeSameAs(focus);
        (focus.IsEditing, focus.HoursInput, focus.MinutesInput, vm.RepeatInput).Should().Be((true, 1m, 10m, 2m));
        vm.Blocks.Select(b => b.IsEditing).Should().Equal(true, false);
    }

    [Fact]
    public async Task Editing_one_block_closes_the_other_editor()
    {
        var vm = await Loaded();

        vm.EditBlockCommand.Execute(vm.Blocks[0]);
        vm.EditBlockCommand.Execute(vm.Blocks[1]);

        vm.Blocks.Select(b => b.IsEditing).Should().Equal(false, true);
    }

    [Fact]
    public async Task Start_text_shows_the_whole_session_length()
    {
        var vm = await Loaded();
        vm.StartText.Should().Be("Começar · 2 h");

        // O exemplo do autor: 40 de foco, 20 de pausa, 60 de foco e 30 de pausa, uma vez.
        vm.Blocks[0].MinutesInput = 40;
        vm.Blocks[1].MinutesInput = 20;
        vm.AddFocusCommand.Execute(null);
        vm.AddBreakCommand.Execute(null);
        vm.Blocks[2].HoursInput = 1;
        vm.Blocks[2].MinutesInput = 0;
        vm.Blocks[3].MinutesInput = 30;
        vm.RepeatInput = 1;

        vm.StartText.Should().Be("Começar · 2 h 30 min");
        vm.RemoveBlockCommand.Execute(vm.Blocks[3]);
        vm.StartText.Should().Be("Começar · 2 h");
    }

    [Fact]
    public async Task Start_button_shows_minutes_label()
    {
        var vm = await Loaded();
        (vm.Blocks[0].HoursInput, vm.Blocks[0].MinutesInput) = (2, 0);
        (vm.Blocks[1].HoursInput, vm.Blocks[1].MinutesInput) = (0, 30);
        vm.RepeatInput = 1;

        vm.StartText.Should().Be("Começar · 2 h 30 min");
        vm.Blocks.Select(b => b.Text).Should().Equal("2 h", "30 min");
    }

    [Theory]
    [InlineData(1, "vez")]
    [InlineData(2, "vezes")]
    [InlineData(null, "vezes")]
    public async Task Repeat_unit_agrees_with_the_number(int? repeat, string unit)
    {
        var vm = await Loaded();
        vm.RepeatInput = repeat;
        vm.RepeatUnitText.Should().Be(unit); // "Repetir [1] vez", "Repetir [2] vezes"
    }

    [Fact]
    public async Task Invalid_plan_shows_the_validation_message_and_starts_nothing()
    {
        var vm = await Loaded();
        vm.RemoveBlockCommand.Execute(vm.Blocks[0]);

        await vm.StartCommand.ExecuteAsync(null);
        vm.Error.Should().Be("Inclua pelo menos um bloco de foco.");

        vm.AddFocusCommand.Execute(null);
        vm.RepeatInput = 11;
        await vm.StartCommand.ExecuteAsync(null);
        vm.Error.Should().Be("Repita de 1 a 10 vezes.");

        vm.RepeatInput = 4;
        vm.Blocks[1].HoursInput = null;
        vm.Blocks[1].MinutesInput = null;
        vm.Blocks[1].Minutes().Should().BeNull();
        await vm.StartCommand.ExecuteAsync(null);
        vm.Error.Should().Be("O bloco deve ter de 1 min a 4 h.");

        (await _sessions.GetAsync()).Should().BeNull();
        vm.IsPreparing.Should().BeTrue();
    }

    [Fact]
    public async Task Start_is_blocked_with_a_timer_and_offers_today()
    {
        var english = await Create("Inglês");
        await Timers.StartAsync(english.Id);
        var shown = new List<Type>();
        _navigator.Requested += shown.Add;

        var vm = await Loaded();
        const string reason = "Já há um cronômetro em \"Inglês\". Registre ou descarte no Hoje antes de começar o Pomodoro.";
        (vm.CanStart, vm.StartBlockedReason).Should().Be((false, reason));
        vm.StartCommand.CanExecute(null).Should().BeFalse();

        await Timers.StopAsync(); // parado à espera de registro ainda bloqueia
        (vm.CanStart, vm.StartBlockedReason).Should().Be((false, reason));

        vm.GoToTodayCommand.Execute(null);
        shown.Should().Equal(typeof(TodayPageViewModel));

        await Timers.DiscardAsync(); // a recarga chega pelo DataChangeNotifier
        await vm.LoadCommand.ExecutionTask!;
        (vm.CanStart, vm.StartBlockedReason).Should().Be((true, null));
        vm.StartCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Goal_options_are_time_goals_active_today()
    {
        await Create("Estudar");
        await Create("Água", Measure.Quantity);
        await Create("Meditar", Measure.Completion);
        await Create("Fim de semana", days: WeekDays.Saturday | WeekDays.Sunday);
        var archived = await Create("Violão");
        await GoalService.ArchiveAsync(archived.Id);
        await Create("Inglês");

        var vm = await Loaded();

        vm.GoalOptions.Select(o => o.Title).Should().Equal("Sem meta", "Estudar", "Inglês");
        vm.GoalOptions[0].Id.Should().BeNull();
    }

    [Fact]
    public async Task Session_state_follows_the_stored_session()
    {
        await Focus.StartAsync(new FocusPlan([new FocusBlock(FocusBlockKind.Focus, 25)], 1), null);
        var vm = await Loaded();
        (vm.IsPreparing, vm.IsRunning, vm.IsFinished, vm.CanStart).Should().Be((false, true, false, false));

        _time.Advance(TimeSpan.FromMinutes(25));
        await Focus.AdvanceAsync(); // sem meta, a sessão fica terminada à espera de destino
        await vm.LoadCommand.ExecutionTask!;

        (vm.IsPreparing, vm.IsRunning, vm.IsFinished, vm.CanStart).Should().Be((false, false, true, false));
    }

    [Fact]
    public async Task Time_page_has_the_pomodoro_tab_and_loads_on_activate()
    {
        var pomodoro = new PomodoroViewModel(Focus, GoalService, _time, _changes, _navigator);
        var page = new TimePageViewModel(pomodoro);

        (page.Title, page.IconKey, page.ShortTitle).Should().Be(("Tempo", "NavTime", "Tempo"));
        page.Tabs.Should().Equal("Pomodoro");
        page.Pomodoro.Should().BeSameAs(pomodoro);
        pomodoro.Blocks.Should().BeEmpty();

        page.Activate();

        pomodoro.Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void Opening_the_time_page_silences_the_alert()
    {
        var alert = new FakeAlert();
        var alerts = new FocusAlerts(Focus, _sessions, new NoAlarm(), alert, _time, _changes);
        var page = new TimePageViewModel(new PomodoroViewModel(Focus, GoalService, _time, _changes), alerts);

        page.Activate();

        alert.Silenced.Should().Be(1);
    }

    [Fact]
    public void Coming_back_to_the_app_on_the_time_page_silences_the_alert()
    {
        var alert = new FakeAlert();
        var alerts = new FocusAlerts(Focus, _sessions, new NoAlarm(), alert, _time, _changes);
        var page = new TimePageViewModel(new PomodoroViewModel(Focus, GoalService, _time, _changes), alerts);

        page.Resume();

        alert.Silenced.Should().Be(1);
    }

    // ---- Sessão rodando, conferência e tela de fim (spec Pomodoro §4.2–§4.4) ----

    private static FocusBlock F(int minutes) => new(FocusBlockKind.Focus, minutes);
    private static FocusBlock B(int minutes) => new(FocusBlockKind.Break, minutes);

    private async Task<PomodoroViewModel> Running(FocusPlan plan, Guid? goalId = null)
    {
        await Focus.StartAsync(plan, goalId);
        return await Loaded();
    }

    private async Task Reloaded(PomodoroViewModel vm)
    {
        _changes.NotifyChanged();
        await vm.LoadCommand.ExecutionTask!;
    }

    [Fact]
    public async Task Running_focus_counts_down_in_minutes_and_seconds()
    {
        await Focus.StartAsync(new FocusPlan([F(40), B(10)], 1), null);
        _time.Advance(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(48));
        var vm = await Loaded();

        (vm.IsRunning, vm.IsPreparing, vm.IsFinished).Should().Be((true, false, false));
        (vm.MinutesText, vm.SecondsText).Should().Be(("27", "12"));
        (vm.PhaseText, vm.IsPaused, vm.IsWaiting, vm.OvertimeText, vm.GoalText).Should()
            .Be(("Foco · 1 de 1", false, false, (string?)null, (string?)null));

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        _time.Advance(TimeSpan.FromSeconds(1)); // o relógio de 1 s

        (vm.MinutesText, vm.SecondsText).Should().Be(("27", "11"));
        changed.Should().Contain(nameof(PomodoroViewModel.SecondsText));
    }

    [Fact]
    public async Task Long_block_shows_total_minutes_on_top()
    {
        var vm = await Running(new FocusPlan([F(75), B(5)], 1));

        (vm.MinutesText, vm.SecondsText).Should().Be(("75", "00"));
    }

    [Fact]
    public async Task Dots_show_the_current_round_and_round_text_appears_with_repeat()
    {
        var study = await Create("Estudar");
        await Focus.StartAsync(new FocusPlan([F(25), B(5), F(25), B(15)], 2), study.Id);
        foreach (var minutes in new[] { 25, 5, 25, 15, 25 })
        {
            _time.Advance(TimeSpan.FromMinutes(minutes));
            await Focus.AdvanceAsync();
        }
        var vm = await Loaded(); // passo 5: a primeira pausa da segunda rodada

        vm.Dots.Select(d => (d.IsFocus, d.IsDone, d.IsCurrent)).Should().Equal(
            (true, true, false), (false, false, true), (true, false, false), (false, false, false));
        (vm.PhaseText, vm.RoundText, vm.GoalText).Should().Be(("Pausa", "Rodada 2 de 2", "Estudar"));

        _time.Advance(TimeSpan.FromMinutes(5));
        await vm.AdvanceCommand.ExecuteAsync(null);
        vm.PhaseText.Should().Be("Foco · 2 de 2");
        vm.Dots.Select(d => (d.IsDone, d.IsCurrent)).Should().Equal((true, false), (true, false), (false, true), (false, false));

        await Focus.EndAsync();
        var single = await Running(new FocusPlan([F(25), B(5)], 1));
        single.RoundText.Should().BeNull();
        single.Dots.Should().HaveCount(2);
    }

    [Fact]
    public async Task Break_and_next_block_flags_follow_the_block()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        (vm.IsBreak, vm.HasNextBlock).Should().Be((false, true));

        _time.Advance(TimeSpan.FromMinutes(25));
        await vm.AdvanceCommand.ExecuteAsync(null);

        // Na pausa, que é o último bloco: a tela pinta de verde e não oferece um "Encerrar" repetido.
        (vm.IsBreak, vm.HasNextBlock).Should().Be((true, false));

        _time.Advance(TimeSpan.FromMinutes(5));
        await vm.AdvanceCommand.ExecuteAsync(null);
        (vm.IsBreak, vm.HasNextBlock).Should().Be((false, false));
    }

    [Fact]
    public async Task Waiting_shows_overtime_and_the_next_action()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        (vm.NextActionText, vm.BeginSwitchTaskCommand.CanExecute(null)).Should().Be(("Começar pausa", true));

        _time.Advance(TimeSpan.FromMinutes(27)); // o relógio vê o fim do bloco e conta o extra

        (vm.IsWaiting, vm.PhaseText, vm.OvertimeText, vm.NextActionText).Should()
            .Be((true, "Foco concluído", "+2 min", "Começar pausa"));
        (vm.MinutesText, vm.SecondsText).Should().Be(("00", "00"));
        // O ⇄ não tem conferência: esperando o toque, ele some; o ⏸ também.
        (vm.CanSwitchTask, vm.BeginSwitchTaskCommand.CanExecute(null), vm.PauseResumeCommand.CanExecute(null)).Should()
            .Be((false, false, false));

        await vm.AdvanceCommand.ExecuteAsync(null);

        (await _sessions.ChunksAsync()).Single().Seconds.Should().Be(27 * 60); // o extra de um foco conta como foco
        (vm.IsWaiting, vm.PhaseText, vm.MinutesText, vm.OvertimeText).Should().Be((false, "Pausa", "05", (string?)null));
        vm.ShowsTaskField.Should().BeFalse(); // a subatividade só aparece no foco

        _time.Advance(TimeSpan.FromMinutes(6));
        (vm.PhaseText, vm.OvertimeText, vm.NextActionText).Should().Be(("Pausa concluída", "+1 min", "Encerrar"));

        await vm.AdvanceCommand.ExecuteAsync(null);
        (vm.IsFinished, vm.IsAwaitingDestination, vm.SummaryText).Should().Be((true, true, "27 min"));
    }

    [Fact]
    public async Task Overtime_shows_from_the_first_second()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));

        _time.Advance(TimeSpan.FromMinutes(25) + TimeSpan.FromMilliseconds(500));
        (vm.IsWaiting, vm.PhaseText, vm.OvertimeText).Should().Be((true, "Foco concluído", (string?)null)); // sem "+0 s"

        _time.Advance(TimeSpan.FromMilliseconds(500));
        vm.OvertimeText.Should().Be("+1 s");

        _time.Advance(TimeSpan.FromSeconds(64));
        vm.OvertimeText.Should().Be("+1 min 5 s");
    }

    [Fact]
    public async Task Forgotten_focus_opens_the_review_prefilled_with_the_open_time()
    {
        var study = await Create("Estudar");
        var vm = await Running(new FocusPlan([F(25), B(5)], 1), study.Id);
        _time.Advance(TimeSpan.FromMinutes(95) + TimeSpan.FromSeconds(40)); // 1 h 10 min de extra

        await vm.AdvanceCommand.ExecuteAsync(null);

        (vm.IsReviewing, vm.ReviewHoursInput, vm.ReviewMinutesInput, vm.ReviewSecondsInput).Should().Be((true, 1m, 35m, 40m));
        _checkins.All.Should().BeEmpty();
        (await _sessions.GetAsync())!.Step.Should().Be(0);

        vm.CancelReviewCommand.Execute(null);
        vm.IsReviewing.Should().BeFalse();
        vm.AskEndCommand.Execute(null);
        await vm.ConfirmEndCommand.ExecuteAsync(null); // o Encerrar passa pela mesma conferência
        (vm.IsReviewing, vm.IsConfirmingEnd).Should().Be((true, false));
        vm.CancelReviewCommand.Execute(null);
        await vm.SkipCommand.ExecuteAsync(null); // e o ⏭ também
        vm.IsReviewing.Should().BeTrue();

        (vm.ReviewHoursInput, vm.ReviewMinutesInput) = (24, 1);
        await vm.ConfirmReviewCommand.ExecuteAsync(null);
        (vm.IsReviewing, vm.Error).Should().Be((true, "Valor fora do limite."));
        _checkins.All.Should().BeEmpty();

        (vm.ReviewHoursInput, vm.ReviewMinutesInput, vm.ReviewSecondsInput) = (0, 30, 0);
        await vm.ConfirmReviewCommand.ExecuteAsync(null);

        (vm.IsReviewing, vm.Error, vm.PhaseText).Should().Be((false, null, "Pausa"));
        _checkins.All.Single().Should().Match<Checkin>(c => c.GoalId == study.Id && c.Amount == 30 * 60);
        (vm.MinutesText, vm.SecondsText).Should().Be(("05", "00")); // a pausa começa na confirmação
    }

    [Fact]
    public async Task Review_fields_include_seconds()
    {
        var study = await Create("Estudar");
        var vm = await Running(new FocusPlan([F(25), B(5)], 1), study.Id);
        _time.Advance(TimeSpan.FromMinutes(62) + TimeSpan.FromSeconds(17) + TimeSpan.FromMilliseconds(900));

        await vm.AdvanceCommand.ExecuteAsync(null);
        (vm.IsReviewing, vm.ReviewHoursInput, vm.ReviewMinutesInput, vm.ReviewSecondsInput).Should().Be((true, 1m, 2m, 17m));

        vm.ReviewSecondsInput = 5;
        await vm.ConfirmReviewCommand.ExecuteAsync(null);

        (vm.IsReviewing, vm.Error).Should().Be((false, null));
        _checkins.All.Single().Should().Match<Checkin>(c => c.GoalId == study.Id && c.Amount == 3725);
    }

    [Fact]
    public async Task Review_accepts_only_seconds_and_asks_for_the_time_when_empty()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        _time.Advance(TimeSpan.FromMinutes(70));
        await vm.AdvanceCommand.ExecuteAsync(null);

        (vm.ReviewHoursInput, vm.ReviewMinutesInput, vm.ReviewSecondsInput) = (null, null, null);
        await vm.ConfirmReviewCommand.ExecuteAsync(null);
        (vm.IsReviewing, vm.Error).Should().Be((true, "Informe o tempo."));

        vm.ReviewSecondsInput = 50;
        await vm.ConfirmReviewCommand.ExecuteAsync(null);

        (vm.IsReviewing, vm.Error).Should().Be((false, null));
        (await _sessions.ChunksAsync()).Single().Seconds.Should().Be(50);
    }

    [Fact]
    public async Task Skip_on_the_last_block_asks_before_ending()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        _time.Advance(TimeSpan.FromMinutes(10));
        await vm.SkipCommand.ExecuteAsync(null); // no foco, o ⏭ segue para a pausa sem perguntar
        (vm.PhaseText, vm.IsConfirmingEnd).Should().Be(("Pausa", false));

        _time.Advance(TimeSpan.FromMinutes(2));
        await vm.SkipCommand.ExecuteAsync(null); // no último bloco, um toque solto não encerra a sessão

        (vm.IsConfirmingEnd, vm.IsRunning).Should().Be((true, true));
        (await _sessions.GetAsync())!.FinishedAt.Should().BeNull();
        vm.CancelEndCommand.Execute(null);
        (vm.IsConfirmingEnd, vm.IsRunning).Should().Be((false, true));

        await vm.SkipCommand.ExecuteAsync(null);
        await vm.ConfirmEndCommand.ExecuteAsync(null);
        (vm.IsFinished, vm.SummaryText).Should().Be((true, "10 min"));
    }

    [Fact]
    public async Task Review_opens_when_the_service_asks_for_it_after_the_screen_read()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        _time.Advance(TimeSpan.FromMinutes(10));
        await vm.PauseResumeCommand.ExecuteAsync(null);
        // Outro processo retoma sem a tela saber: para ela, a sessão segue pausada e sem extra.
        await new FocusService(_goals, _sessions, _timers, Checkins, new AppSettings(_settings), _time).ResumeAsync();
        _time.Advance(TimeSpan.FromMinutes(60)); // 45 min de extra na sessão guardada

        await vm.SkipCommand.ExecuteAsync(null);

        (vm.IsReviewing, vm.ReviewHoursInput, vm.ReviewMinutesInput, vm.Error).Should().Be((true, 1m, 10m, (string?)null));
        _checkins.All.Should().BeEmpty();
        (await _sessions.GetAsync())!.Step.Should().Be(0);
    }

    [Fact]
    public async Task Typed_review_survives_a_background_reload()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        _time.Advance(TimeSpan.FromMinutes(70)); // 45 min de extra
        await vm.AdvanceCommand.ExecuteAsync(null);
        vm.IsReviewing.Should().BeTrue();
        (vm.ReviewHoursInput, vm.ReviewMinutesInput, vm.ReviewSecondsInput) = (0, 40, 0);

        await Reloaded(vm);

        (vm.IsReviewing, vm.ReviewHoursInput, vm.ReviewMinutesInput, vm.ReviewSecondsInput).Should().Be((true, 0m, 40m, 0m));
        await vm.ConfirmReviewCommand.ExecuteAsync(null);
        (await _sessions.ChunksAsync()).Single().Seconds.Should().Be(40 * 60);
    }

    [Fact]
    public async Task Pause_freezes_the_countdown()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        _time.Advance(TimeSpan.FromMinutes(10));

        await vm.PauseResumeCommand.ExecuteAsync(null);
        (vm.IsPaused, vm.PhaseText, vm.MinutesText, vm.SecondsText).Should().Be((true, "Pausado", "15", "00"));

        var ticks = 0;
        vm.PropertyChanged += (_, e) => ticks += e.PropertyName == nameof(PomodoroViewModel.SecondsText) ? 1 : 0;
        _time.Advance(TimeSpan.FromMinutes(20));
        (vm.IsPaused, vm.IsWaiting, vm.MinutesText).Should().Be((true, false, "15"));
        ticks.Should().Be(0); // pausado, o relógio de 1 s não anda

        await vm.PauseResumeCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(1));
        (vm.IsPaused, vm.PhaseText, vm.MinutesText, vm.SecondsText).Should().Be((false, "Foco · 1 de 1", "14", "00"));
    }

    [Fact]
    public async Task Switch_task_records_the_chunk_like_the_timer_card()
    {
        var study = await Create("Estudar");
        await Checkins.AdjustAsync(study.Id, 10, "Capítulo 2");
        var vm = await Running(new FocusPlan([F(50), B(10)], 1), study.Id);
        (vm.TaskSuggestions, vm.ShowsTaskField, vm.ShowsTaskLabel).Should().BeEquivalentTo((new[] { "Capítulo 2" }, true, false));

        vm.TaskInput = "  Capítulo 3 ";
        await vm.SaveTaskCommand.ExecuteAsync(null);
        (await _sessions.GetAsync())!.Task.Should().Be("Capítulo 3");
        (vm.ShowsTaskLabel, vm.TaskLabel, vm.IsEditingTask).Should().Be((true, "▸ Capítulo 3", false));

        vm.BeginEditTaskCommand.Execute(null);
        (vm.IsEditingTask, vm.TaskInput, vm.ShowsTaskField).Should().Be((true, "Capítulo 3", true));
        vm.CancelEditTaskCommand.Execute(null);
        vm.ShowsTaskLabel.Should().BeTrue();

        _time.Advance(TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(30));
        await vm.BeginSwitchTaskCommand.ExecuteAsync(null);
        (vm.IsSwitchingTask, vm.ShowsTaskField, vm.ShowsConfirmTask).Should().Be((true, true, false));
        vm.TaskInput.Should().BeNull();
        vm.CancelSwitchTaskCommand.Execute(null);
        (vm.IsSwitchingTask, vm.TaskInput).Should().Be((false, "Capítulo 3"));

        await vm.BeginSwitchTaskCommand.ExecuteAsync(null);
        vm.TaskInput = "Revisão";
        await vm.SwitchTaskCommand.ExecuteAsync(null);

        _checkins.All.Where(c => c.Note == "Capítulo 3").Should().ContainSingle()
            .Which.Should().Match<Checkin>(c => c.GoalId == study.Id && c.Amount == 12 * 60 + 30);
        (vm.IsSwitchingTask, vm.TaskLabel, vm.Error).Should().Be((false, "▸ Revisão", null));
        (await _sessions.GetAsync())!.Carry.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Task_field_left_open_closes_when_the_focus_ends()
    {
        var vm = await Running(new FocusPlan([F(25), B(5), F(25), B(5)], 1));
        vm.TaskInput = "Capítulo 3";
        await vm.SaveTaskCommand.ExecuteAsync(null);

        // ⇄ aberto quando o foco chega ao fim: a troca não vale mais, e o nome gravado volta (sem cartão vazio).
        await vm.BeginSwitchTaskCommand.ExecuteAsync(null);
        vm.TaskInput = "Revis";
        vm.IsTaskFieldOpen.Should().BeTrue();
        _time.Advance(TimeSpan.FromMinutes(25));
        (vm.IsWaiting, vm.IsSwitchingTask, vm.IsTaskFieldOpen).Should().Be((true, false, false));
        (vm.ShowsTaskLabel, vm.TaskInput).Should().Be((true, "Capítulo 3"));

        // ✎ aberto ao começar a pausa: na pausa o campo não aparece, e os controles (⏸, ⏭, ✕) voltam.
        vm.BeginEditTaskCommand.Execute(null);
        vm.IsTaskFieldOpen.Should().BeTrue();
        await vm.AdvanceCommand.ExecuteAsync(null);
        (vm.IsBreak, vm.IsEditingTask, vm.IsSwitchingTask, vm.IsTaskFieldOpen).Should().Be((true, false, false, false));

        // ⏭ com o ⇄ aberto no foco seguinte: a pausa também começa sem o modo de troca.
        await vm.SkipCommand.ExecuteAsync(null);
        await vm.BeginSwitchTaskCommand.ExecuteAsync(null);
        await vm.SkipCommand.ExecuteAsync(null);
        (vm.IsBreak, vm.IsSwitchingTask, vm.IsTaskFieldOpen).Should().Be((true, false, false));
    }

    [Fact]
    public async Task Typing_survives_a_background_reload()
    {
        var vm = await Loaded();
        vm.EditBlockCommand.Execute(vm.Blocks[0]);
        vm.Blocks[0].MinutesInput = 40;
        await Reloaded(vm);
        (vm.Blocks[0].IsEditing, vm.Blocks[0].Minutes()).Should().Be((true, 40));

        await vm.StartCommand.ExecuteAsync(null);
        vm.TaskInput = "Capít";
        await Reloaded(vm);
        vm.TaskInput.Should().Be("Capít");

        await Focus.SetTaskAsync("Outro"); // outro processo dá um nome: o rascunho fica, em edição
        await vm.LoadCommand.ExecutionTask!;
        (vm.TaskInput, vm.IsEditingTask, vm.TaskLabel).Should().Be(("Capít", true, "▸ Outro"));

        _time.Advance(TimeSpan.FromMinutes(75));
        vm.AskEndCommand.Execute(null);
        await vm.ConfirmEndCommand.ExecuteAsync(null);
        (vm.IsReviewing, vm.ReviewHoursInput, vm.ReviewMinutesInput).Should().Be((true, 1m, 15m));
        vm.ReviewMinutesInput = 10;
        await Reloaded(vm);
        (vm.IsReviewing, vm.ReviewHoursInput, vm.ReviewMinutesInput).Should().Be((true, 1m, 10m));
    }

    [Fact]
    public async Task Unassigned_end_offers_assign_create_and_discard()
    {
        var study = await Create("Estudar");
        await Create("Inglês");
        await Focus.StartAsync(new FocusPlan([F(25), B(5)], 1), null);
        await Focus.SetTaskAsync("Capítulo 3");
        _time.Advance(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(40));
        await Focus.SwitchTaskAsync("Revisão");
        _time.Advance(TimeSpan.FromMinutes(18));
        var vm = await Loaded();

        vm.AskEndCommand.Execute(null);
        vm.IsConfirmingEnd.Should().BeTrue();
        await vm.ConfirmEndCommand.ExecuteAsync(null);

        (vm.IsFinished, vm.IsRunning, vm.IsPreparing, vm.IsAwaitingDestination).Should().Be((true, false, false, true));
        (vm.SummaryText, vm.GoalText).Should().Be(("38 min 40 s", null));
        vm.SummaryLines.Should().Equal("Capítulo 3 · 20 min 40 s", "Revisão · 18 min");
        vm.AssignOptions.Select(o => o.Title).Should().Equal("Estudar", "Inglês");
        vm.AssignCommand.CanExecute(null).Should().BeFalse();

        var reopened = await Loaded(); // o app reaberto mostra o mesmo fim, dos trechos guardados
        (reopened.IsAwaitingDestination, reopened.SummaryText).Should().Be((true, "38 min 40 s"));
        reopened.SummaryLines.Should().Equal("Capítulo 3 · 20 min 40 s", "Revisão · 18 min");

        vm.AskDiscardCommand.Execute(null);
        vm.IsConfirmingDiscard.Should().BeTrue();
        vm.CancelDiscardCommand.Execute(null);
        vm.IsConfirmingDiscard.Should().BeFalse();

        vm.AssignGoal = vm.AssignOptions.Single(o => o.Id == study.Id);
        await vm.AssignCommand.ExecuteAsync(null);

        _checkins.All.Select(c => (c.GoalId, c.Amount, c.Note)).Should()
            .BeEquivalentTo([(study.Id, 20 * 60 + 40, "Capítulo 3"), (study.Id, 18 * 60, "Revisão")]);
        (await _sessions.GetAsync()).Should().BeNull();
        (vm.IsFinished, vm.IsAwaitingDestination, vm.GoalText, vm.SummaryText).Should().Be((true, false, "Estudar", "38 min 40 s"));
        vm.NewSessionCommand.Execute(null);
        vm.IsPreparing.Should().BeTrue();

        // Descartar: pede confirmação e apaga os trechos.
        await Focus.StartAsync(new FocusPlan([F(25)], 1), null);
        _time.Advance(TimeSpan.FromMinutes(25));
        await Focus.AdvanceAsync();
        await vm.LoadCommand.ExecutionTask!;
        vm.IsAwaitingDestination.Should().BeTrue();
        vm.AskDiscardCommand.Execute(null);
        await vm.ConfirmDiscardCommand.ExecuteAsync(null);

        (await _sessions.GetAsync()).Should().BeNull();
        _checkins.All.Should().HaveCount(2);
        (vm.IsPreparing, vm.IsFinished, vm.IsConfirmingDiscard).Should().Be((true, false, false));
    }

    [Fact]
    public async Task Create_goal_assigns_the_time_on_save_and_returns_on_cancel()
    {
        await Focus.StartAsync(new FocusPlan([F(25)], 1), null);
        _time.Advance(TimeSpan.FromMinutes(25));
        await Focus.AdvanceAsync();
        var vm = await Loaded();

        vm.CreateGoalCommand.Execute(null);
        vm.Editor.Should().NotBeNull();
        (vm.Editor!.IsNew, vm.Editor.IsDuration, vm.Editor.IsMeasureLocked).Should().Be((true, true, true));
        vm.Editor.BackText.Should().Be("← Tempo"); // o voltar do editor leva de volta à aba Tempo, não a Metas
        vm.Editor.CancelCommand.Execute(null);
        vm.Editor.Should().BeNull();
        (vm.IsAwaitingDestination, vm.SummaryText).Should().Be((true, "25 min"));

        vm.CreateGoalCommand.Execute(null);
        var editor = vm.Editor!;
        editor.Title = "Ler";
        editor.DurationHoursInput = 1;
        await editor.SaveCommand.ExecuteAsync(null);
        await vm.AssignCommand.ExecutionTask!;

        var created = (await _goals.ListAsync(includeArchived: false)).Single();
        (created.Title, created.Measure).Should().Be(("Ler", Measure.Duration));
        _checkins.All.Single().Should().Match<Checkin>(c => c.GoalId == created.Id && c.Amount == 25 * 60);
        vm.Editor.Should().BeNull();
        (vm.IsAwaitingDestination, vm.GoalText, vm.Error).Should().Be((false, "Ler", null));
    }

    [Fact]
    public async Task Back_closes_what_is_open_over_the_session()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        var page = new TimePageViewModel(vm);
        page.TryGoBack().Should().BeFalse();

        vm.AskEndCommand.Execute(null);
        page.TryGoBack().Should().BeTrue();
        vm.IsConfirmingEnd.Should().BeFalse();

        _time.Advance(TimeSpan.FromMinutes(25));
        await vm.AdvanceCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(5));
        await vm.AdvanceCommand.ExecuteAsync(null);
        vm.CreateGoalCommand.Execute(null);
        page.TryGoBack().Should().BeTrue();
        (vm.Editor, vm.IsAwaitingDestination).Should().Be((null, true));
    }

    [Fact]
    public async Task One_tap_at_a_time_while_the_session_is_saving()
    {
        var held = new HeldSessions(_sessions);
        var focus = new FocusService(_goals, held, _timers, Checkins, new AppSettings(_settings), _time, _changes);
        await focus.StartAsync(new FocusPlan([F(25), B(5), F(25)], 1), null);
        var vm = new PomodoroViewModel(focus, GoalService, _time, _changes, _navigator);
        await vm.LoadAsync();
        _time.Advance(TimeSpan.FromMinutes(25));

        held.Gate = new TaskCompletionSource();
        var advancing = vm.AdvanceCommand.ExecuteAsync(null); // "Começar pausa", ainda gravando
        advancing.IsCompleted.Should().BeFalse();

        new[]
        {
            vm.AdvanceCommand.CanExecute(null), vm.SkipCommand.CanExecute(null), vm.ConfirmEndCommand.CanExecute(null),
            vm.ConfirmReviewCommand.CanExecute(null), vm.PauseResumeCommand.CanExecute(null),
            vm.SwitchTaskCommand.CanExecute(null),
        }.Should().AllBeEquivalentTo(false);
        var skipping = vm.SkipCommand.ExecuteAsync(null); // o ⏭ tocado logo depois não corta de novo
        held.Gate.SetResult();
        held.Gate = null;
        await advancing;
        await skipping;

        (await _sessions.GetAsync())!.Step.Should().Be(1);
        (vm.PhaseText, vm.Error).Should().Be(("Pausa", null));
        (vm.SkipCommand.CanExecute(null), vm.PauseResumeCommand.CanExecute(null)).Should().Be((true, true));
    }

    [Fact]
    public async Task Errors_survive_a_background_reload_while_the_session_runs()
    {
        var vm = await Running(new FocusPlan([F(25), B(5)], 1));
        _time.Advance(TimeSpan.FromMinutes(60));
        await vm.AdvanceCommand.ExecuteAsync(null);
        (vm.ReviewHoursInput, vm.ReviewMinutesInput) = (24, 1);
        await vm.ConfirmReviewCommand.ExecuteAsync(null);
        vm.Error.Should().Be("Valor fora do limite.");

        await Reloaded(vm); // sincronização ou a recarga que o próprio toque postou
        (vm.IsReviewing, vm.Error).Should().Be((true, "Valor fora do limite."));

        vm.CancelReviewCommand.Execute(null);
        vm.TaskInput = new string('a', 201);
        await vm.SaveTaskCommand.ExecuteAsync(null);
        await Reloaded(vm);
        vm.Error.Should().Be("A nota deve ter até 200 caracteres.");
    }

    [Fact]
    public async Task Linked_end_keeps_the_summary_until_new_session()
    {
        var study = await Create("Estudar");
        var vm = await Running(new FocusPlan([F(25), B(5)], 1), study.Id);
        vm.TaskInput = "Capítulo 3"; // digitado e não confirmado: o fim do foco usa o texto, como o ■ do cronômetro
        _time.Advance(TimeSpan.FromMinutes(25));
        await vm.AdvanceCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromMinutes(5));
        vm.NextActionText.Should().Be("Encerrar");

        await vm.AdvanceCommand.ExecuteAsync(null);

        (await _sessions.GetAsync()).Should().BeNull();
        (vm.IsFinished, vm.IsPreparing, vm.IsAwaitingDestination).Should().Be((true, false, false));
        (vm.SummaryText, vm.GoalText).Should().Be(("25 min", "Estudar"));
        vm.SummaryLines.Should().Equal("Capítulo 3 · 25 min");
        _checkins.All.Single().Should().Match<Checkin>(c => c.GoalId == study.Id && c.Amount == 25 * 60 && c.Note == "Capítulo 3");

        await Reloaded(vm);
        (vm.IsFinished, vm.IsPreparing, vm.SummaryText).Should().Be((true, false, "25 min"));

        vm.NewSessionCommand.Execute(null);
        await vm.LoadCommand.ExecutionTask!;
        (vm.IsPreparing, vm.IsFinished, vm.SummaryText).Should().Be((true, false, null));
        Plan(vm).Should().Equal((FocusBlockKind.Focus, 25), (FocusBlockKind.Break, 5));
        vm.RepeatInput.Should().Be(1);
    }

    /// <summary>Segura as gravações da sessão enquanto <see cref="Gate"/> estiver aberto: o toque fica "gravando".</summary>
    private sealed class HeldSessions(IFocusSessionRepository inner) : IFocusSessionRepository
    {
        public TaskCompletionSource? Gate { get; set; }

        public Task<FocusSession?> GetAsync(CancellationToken ct = default) => inner.GetAsync(ct);
        public Task<IReadOnlyList<FocusChunk>> ChunksAsync(CancellationToken ct = default) => inner.ChunksAsync(ct);
        public Task<bool> TryStartAsync(FocusSession session, CancellationToken ct = default) => inner.TryStartAsync(session, ct);

        public async Task<bool> UpdateAsync(int expectedRevision, FocusSession? next, FocusChunk? chunk, Checkin? checkin,
            CancellationToken ct = default)
        {
            if (Gate is { } gate)
                await gate.Task;
            return await inner.UpdateAsync(expectedRevision, next, chunk, checkin, ct);
        }

        public Task<bool> AssignChunkAsync(Guid chunkId, Checkin checkin, CancellationToken ct = default) =>
            inner.AssignChunkAsync(chunkId, checkin, ct);
        public Task<bool> MarkAlertedAsync(int step, CancellationToken ct = default) => inner.MarkAlertedAsync(step, ct);
        public Task ClearAsync(CancellationToken ct = default) => inner.ClearAsync(ct);
    }

    private sealed class NoAlarm : IFocusAlarm
    {
        public void Set(DateTimeOffset at) { }
        public void Cancel() { }
    }

    private sealed class FakeAlert : IFocusAlert
    {
        public int Silenced { get; private set; }
        public void Show(FocusAlertKind kind) { }
        public void Silence() => Silenced++;
        public void Withdraw() { }
    }
}
