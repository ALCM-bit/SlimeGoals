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
        focus.Text.Should().Be("1 h 15");
        await vm.StartCommand.ExecuteAsync(null);
        (await _sessions.GetAsync())!.Plan.Blocks.Should().Equal(
            new FocusBlock(FocusBlockKind.Focus, 75), new FocusBlock(FocusBlockKind.Break, 5));
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

        vm.StartText.Should().Be("Começar · 2 h 30");
        vm.RemoveBlockCommand.Execute(vm.Blocks[3]);
        vm.StartText.Should().Be("Começar · 2 h");
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
