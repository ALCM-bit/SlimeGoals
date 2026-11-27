using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Account;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Sync;
using SlimeGoals.App.Tests.Account;
using SlimeGoals.Testing;

using static SlimeGoals.App.Tests.Support.DurationText;

namespace SlimeGoals.App.Tests.ViewModels;

public class TodayPageViewModelTests
{
    // Quarta, 04/11/2026 12:00 em São Paulo (UTC-3).
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryTagRepository _tagRepo = new();
    private readonly InMemoryGoalRepository _goals;
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers;
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly InMemorySettingsRepository _settings = new();

    public TodayPageViewModelTests()
    {
        _goals = new(tags: _tagRepo);
        _timers = new(_checkins);
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
    }

    private TimerService Timers => new(_goals, _timers, CheckinService, _time);

    private GoalService GoalService => new(_goals, _checkins, _timers, _time, tags: _tagRepo);
    private CheckinService CheckinService => new(_goals, _checkins, _time);

    private ReminderPause Pause => new(_settings, _time, _changes);

    private TodayPageViewModel NewPage(PlatformCapabilities? capabilities = null, IReminderPermissions? permissions = null) => new(
        new ProgressService(_goals, _checkins, _timers, _time), CheckinService,
        new TimerService(_goals, _timers, CheckinService, _time), GoalService, _time,
        new ReminderService(_goals, _checkins, _timers, _log, _notifications, new ReminderFormatter(), _time, Pause),
        _changes, Pause, new ReportService(_goals, _checkins, _log, _time), capabilities, permissions,
        tags: new TagService(_tagRepo, _goals, _time));

    private TodayPageViewModel NewPageWithAccount(FakeSyncAccount account, PageNavigator? navigator = null) => new(
        new ProgressService(_goals, _checkins, _timers, _time), CheckinService,
        new TimerService(_goals, _timers, CheckinService, _time), GoalService, _time,
        new ReminderService(_goals, _checkins, _timers, _log, _notifications, new ReminderFormatter(), _time, Pause),
        _changes, Pause, navigator: navigator, account: account);

    private AccountStatus SignedInSince(TimeSpan ago) =>
        new(AccountState.SignedIn, "ana@exemplo.com", new Uri("https://sync.exemplo.com/"), _time.GetUtcNow() - ago, 0, null);

    [Fact]
    public async Task Today_shows_the_sync_warning_when_stale()
    {
        var account = new FakeSyncAccount { Status = SignedInSince(TimeSpan.FromDays(4)) };
        var page = NewPageWithAccount(account);
        await page.LoadCommand.ExecuteAsync(null);

        (page.HasSyncWarning, page.SyncWarning).Should().Be((true, SyncStaleWarning.Text));
    }

    [Fact]
    public async Task Today_hides_the_warning_after_a_successful_sync()
    {
        var account = new FakeSyncAccount { Status = SignedInSince(TimeSpan.FromDays(4)) };
        var page = NewPageWithAccount(account);
        await page.LoadCommand.ExecuteAsync(null);
        page.HasSyncWarning.Should().BeTrue();

        account.Status = SignedInSince(TimeSpan.FromMinutes(1));
        account.RaiseStatusChanged();
        await WaitUntilAsync(() => !page.HasSyncWarning);

        page.SyncWarning.Should().BeNull();
    }

    [Fact]
    public async Task An_older_status_read_finishing_last_does_not_overwrite_the_newer_one()
    {
        var account = new FakeSyncAccount { Status = SignedInSince(TimeSpan.FromDays(4)) };
        var page = NewPageWithAccount(account);
        await page.LoadCommand.ExecuteAsync(null);
        page.HasSyncWarning.Should().BeTrue();

        var older = new TaskCompletionSource<AccountStatus>();
        var newer = new TaskCompletionSource<AccountStatus>();
        account.StatusGates.Enqueue(older);
        account.StatusGates.Enqueue(newer);
        account.RaiseStatusChanged();
        account.RaiseStatusChanged();

        newer.SetResult(SignedInSince(TimeSpan.FromMinutes(1)));
        await WaitUntilAsync(() => !page.HasSyncWarning);
        older.SetResult(SignedInSince(TimeSpan.FromDays(5)));
        await Task.Delay(100);

        page.HasSyncWarning.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(OperationCanceledException))]
    public async Task A_failing_status_read_leaves_no_warning_and_does_not_fault_the_load(Type failure)
    {
        var account = new FakeSyncAccount { Status = SignedInSince(TimeSpan.FromDays(4)) };
        var page = NewPageWithAccount(account);
        await page.LoadCommand.ExecuteAsync(null);
        page.HasSyncWarning.Should().BeTrue();

        account.StatusFailure = (Exception)Activator.CreateInstance(failure)!;
        var load = page.LoadCommand.ExecuteAsync(null);
        await load.Awaiting(t => t).Should().NotThrowAsync();

        (page.HasSyncWarning, page.ErrorMessage).Should().Be((false, null));
    }

    [Fact]
    public async Task The_goal_cards_do_not_wait_for_the_sync_status()
    {
        await Create("Água");
        var account = new FakeSyncAccount { Status = SignedInSince(TimeSpan.FromDays(4)) };
        account.StatusGates.Enqueue(new TaskCompletionSource<AccountStatus>());
        var page = NewPageWithAccount(account);

        var load = page.LoadCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => page.Goals.Count == 1);

        load.IsCompleted.Should().BeFalse("o aviso é só um complemento e é lido depois dos cartões");
    }

    [Fact]
    public async Task Today_has_no_sync_warning_without_an_account()
    {
        var page = NewPage();
        await page.LoadCommand.ExecuteAsync(null);

        (page.HasSyncWarning, page.SyncWarning).Should().Be((false, null));
    }

    [Fact]
    public async Task Open_sync_shows_the_sync_settings()
    {
        var navigator = new PageNavigator();
        var shown = new List<Type>();
        var sections = new List<SettingsSection>();
        navigator.Requested += shown.Add;
        navigator.SettingsRequested += (section, _) => sections.Add(section);
        var account = new FakeSyncAccount { Status = SignedInSince(TimeSpan.FromDays(4)) };
        var page = NewPageWithAccount(account, navigator);
        await page.LoadCommand.ExecuteAsync(null);

        page.OpenSyncCommand.Execute(null);

        (sections, shown).Should().BeEquivalentTo((new List<SettingsSection> { SettingsSection.Sync }, new List<Type> { typeof(SettingsPageViewModel) }));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().Should().BeTrue();
    }

    [Fact]
    public async Task Without_reminders_hides_card_time_and_pause()
    {
        await Create("Água");
        var page = NewPage(new PlatformCapabilities(false, true, true, true));
        await page.LoadCommand.ExecuteAsync(null);

        (Card(page, "Água").HasNextReminder, page.CanPause, page.ShowReminders).Should().Be((false, false, false));
    }

    private const string NotificationsWarning = "Os lembretes estão desligados: permita as notificações do SlimeGoals.";
    private const string ExactAlarmsWarning = "Os lembretes podem atrasar alguns minutos: permita alarmes exatos.";

    [Fact]
    public async Task Today_warns_when_notifications_missing()
    {
        var permissions = new FakePermissions(new ReminderPermissionStatus(false, false));
        var page = NewPage(permissions: permissions);
        await page.LoadCommand.ExecuteAsync(null);

        (page.HasPermissionWarning, page.PermissionWarning).Should().Be((true, NotificationsWarning));
        page.RequestPermissionCommand.Execute(null);
        (permissions.NotificationRequests, permissions.ExactAlarmRequests).Should().Be((1, 0));
    }

    [Fact]
    public async Task Today_warns_when_only_exact_alarms_missing()
    {
        var permissions = new FakePermissions(new ReminderPermissionStatus(true, false));
        var page = NewPage(permissions: permissions);
        await page.LoadCommand.ExecuteAsync(null);

        page.PermissionWarning.Should().Be(ExactAlarmsWarning);
        page.RequestPermissionCommand.Execute(null);
        (permissions.NotificationRequests, permissions.ExactAlarmRequests).Should().Be((0, 1));
    }

    [Fact]
    public async Task Today_has_no_warning_when_granted_or_without_port()
    {
        var granted = NewPage(permissions: new FakePermissions(new ReminderPermissionStatus(true, true)));
        await granted.LoadCommand.ExecuteAsync(null);
        var noPort = NewPage();
        await noPort.LoadCommand.ExecuteAsync(null);
        var noReminders = NewPage(new PlatformCapabilities(false, false, false, false),
            new FakePermissions(new ReminderPermissionStatus(false, false)));
        await noReminders.LoadCommand.ExecuteAsync(null);

        (granted.HasPermissionWarning, noPort.HasPermissionWarning, noReminders.HasPermissionWarning)
            .Should().Be((false, false, false));
        noPort.PermissionWarning.Should().BeNull();
    }

    [Fact]
    public async Task Today_rechecks_permissions_on_data_change()
    {
        var permissions = new FakePermissions(new ReminderPermissionStatus(false, false));
        var page = NewPage(permissions: permissions);
        await page.LoadCommand.ExecuteAsync(null);
        page.HasPermissionWarning.Should().BeTrue();

        permissions.Status = new ReminderPermissionStatus(true, true);
        _changes.NotifyChanged();
        await Task.Delay(200);

        page.HasPermissionWarning.Should().BeFalse();
    }

    [Fact]
    public async Task Today_warning_ignores_battery()
    {
        var page = NewPage(permissions: new FakePermissions(new ReminderPermissionStatus(true, true, false)));
        await page.LoadCommand.ExecuteAsync(null);

        page.HasPermissionWarning.Should().BeFalse();
    }

    private sealed class FakePermissions(ReminderPermissionStatus status) : IReminderPermissions
    {
        public ReminderPermissionStatus Status { get; set; } = status;
        public int NotificationRequests { get; private set; }
        public int ExactAlarmRequests { get; private set; }

        public ReminderPermissionStatus Get() => Status;

        public void RequestNotifications() => NotificationRequests++;

        public void RequestExactAlarms() => ExactAlarmRequests++;

        public void RequestBatteryUnrestricted()
        {
        }
    }

    private async Task<TodayPageViewModel> Loaded()
    {
        var page = NewPage();
        await page.LoadCommand.ExecuteAsync(null);
        return page;
    }

    private Task<Goal> Create(string title, Measure measure = Measure.Quantity, int target = 8, string? unit = "copos") =>
        GoalService.CreateAsync(new GoalDraft(title, Recurrence.Daily, target, "water", GoalColor.Green, 60,
            new TimeOnly(8, 0), new TimeOnly(20, 0), WeekDays.All, null, null, measure,
            measure == Measure.Quantity ? unit : null));

    private Task<Goal> CreateTagged(string title, params string[] tags) =>
        GoalService.CreateAsync(new GoalDraft(title, Recurrence.Daily, 1, "water", GoalColor.Green, 60,
            new TimeOnly(8, 0), new TimeOnly(20, 0), WeekDays.All, null, null, Measure.Completion, null) with
        { Tags = tags });

    [Fact]
    public async Task Cards_carry_tag_chips_with_the_tag_colors()
    {
        await CreateTagged("Remédio", "medicamentos", "zeta");
        await CreateTagged("Sem tag");
        var page = NewPage();
        await page.LoadCommand.ExecuteAsync(null);

        var card = Card(page, "Remédio");
        card.HasTags.Should().BeTrue();
        card.Tags.Select(t => (t.Name, t.Color)).Should().Equal(("medicamentos", GoalColor.Purple), ("zeta", GoalColor.Pink));
        Card(page, "Sem tag").HasTags.Should().BeFalse();
        page.Filter.Chips.Select(c => c.Name).Should().Equal("medicamentos", "zeta");
    }

    [Fact]
    public async Task Card_shows_first_tag_and_count_of_the_rest()
    {
        await CreateTagged("Treino", "saúde", "treino", "zumba");
        var page = await Loaded();

        var card = Card(page, "Treino");
        (card.PrimaryTag!.Name, card.PrimaryTag.DisplayName, card.MoreTagsText, card.HasMoreTags)
            .Should().Be(("saúde", "Saúde", "+2", true));
        card.TagsText.Should().Be("Saúde, Treino, Zumba"); // a dica do "+2" mostra todas
    }

    [Fact]
    public async Task Card_without_tags_has_no_primary_tag()
    {
        await CreateTagged("Sem tag");
        var page = await Loaded();

        (Card(page, "Sem tag").PrimaryTag, Card(page, "Sem tag").HasMoreTags).Should().Be((null, false));
    }

    [Fact]
    public async Task Card_with_one_tag_has_no_more_text()
    {
        await CreateTagged("Remédio", "estudos");
        var page = await Loaded();

        (Card(page, "Remédio").PrimaryTag!.DisplayName, Card(page, "Remédio").MoreTagsText).Should().Be(("Estudos", null));
    }

    [Fact]
    public async Task Filter_does_not_change_week_percent_counters_or_mascot()
    {
        await CreateTagged("A", "x");
        await CreateTagged("B", "y");
        await CreateTagged("C", "y");
        var page = NewPage();
        await page.LoadCommand.ExecuteAsync(null);
        var before = (page.WeekPercent, page.InProgress, page.Completed, page.Mascot, page.IsEmpty, page.StreakValue);

        page.Filter.ToggleCommand.Execute(page.Filter.Chips.Single(c => c.Name == "x"));

        page.Goals.Count(g => g.IsShown).Should().Be(1);
        (page.WeekPercent, page.InProgress, page.Completed, page.Mascot, page.IsEmpty, page.StreakValue).Should().Be(before);
        page.Goals.Should().HaveCount(3);
    }

    [Fact]
    public async Task Selection_survives_reload_and_drops_when_the_last_goal_of_the_tag_is_completed()
    {
        await CreateTagged("A", "x");
        var b = await CreateTagged("B", "y");
        var page = NewPage();
        await page.LoadCommand.ExecuteAsync(null);
        page.Filter.ToggleCommand.Execute(page.Filter.Chips.Single(c => c.Name == "y"));

        await page.LoadCommand.ExecuteAsync(null);
        page.Filter.HasSelection.Should().BeTrue();
        Card(page, "A").IsShown.Should().BeFalse();

        // Meta arquivada sai da lista: a tag "y" some da linha, e o filtro se limpa.
        await GoalService.ArchiveAsync(b.Id);
        await page.LoadCommand.ExecuteAsync(null);

        page.Filter.HasSelection.Should().BeFalse();
        Card(page, "A").IsShown.Should().BeTrue();
    }

    private static TodayGoalViewModel Card(TodayPageViewModel page, string title) => page.Goals.Single(g => g.Title == title);

    [Fact]
    public async Task Streak_tile_shows_top_current_streak_and_updates_when_today_is_met()
    {
        var goal = await Create("Água", Measure.Completion, 1);
        await _goals.UpdateAsync(goal with { CreatedAt = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero) });
        foreach (var day in new[] { 1, 2, 3 })
            await _checkins.AddAsync(new Checkin
            {
                Id = Guid.NewGuid(), GoalId = goal.Id, DoneAt = new DateTimeOffset(2026, 11, day, 13, 0, 0, TimeSpan.Zero),
                Amount = 1, UpdatedAt = Now,
            });

        var page = await Loaded();

        Assert.Equal((3, "dias", "Água"), (page.StreakValue, page.StreakUnit, page.StreakGoal));
        await page.CompleteCommand.ExecuteAsync(Card(page, "Água"));
        Assert.Equal(4, page.StreakValue);
    }

    /// <summary>Água com registro na terça e na quarta (hoje); Leitura só na quarta. As duas existem desde 31/10.</summary>
    private async Task SeedWeekProgress()
    {
        var created = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var water = await Create("Água");
        await _goals.UpdateAsync(water with { CreatedAt = created });
        var reading = await Create("Leitura");
        await _goals.UpdateAsync(reading with { CreatedAt = created });
        foreach (var (goal, day) in new[] { (water, 3), (water, 4), (reading, 4) })
            await _checkins.AddAsync(new Checkin
            {
                Id = Guid.NewGuid(), GoalId = goal.Id, DoneAt = new DateTimeOffset(2026, 11, day, 13, 0, 0, TimeSpan.Zero),
                Amount = 1, UpdatedAt = Now,
            });
    }

    [Fact]
    public async Task Week_bars_count_goals_with_progress_per_day()
    {
        await SeedWeekProgress();

        var page = await Loaded();

        page.HasWeekBars.Should().BeTrue();
        page.WeekBars.Select(b => b.Label).Should().Equal("Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom");
        (page.WeekBars[1].BarHeight, page.WeekBars[2].BarHeight).Should().Be((40, 80));
    }

    [Fact]
    public async Task Week_bar_of_today_is_marked()
    {
        await SeedWeekProgress();

        var page = await Loaded();

        page.WeekBars.Select(b => b.IsToday).Should().Equal(false, false, true, false, false, false, false);
    }

    [Fact]
    public async Task Week_bars_without_progress_have_minimum_height()
    {
        await SeedWeekProgress();

        var page = await Loaded();

        page.WeekBars[0].BarHeight.Should().Be(4);
    }

    [Fact]
    public async Task Streak_tile_without_streak_shows_zero_days()
    {
        var page = await Loaded();

        Assert.Equal((0, "dias", (string?)null), (page.StreakValue, page.StreakUnit, page.StreakGoal));
    }

    [Fact]
    public async Task Load_shows_goals_texts_and_week_summary()
    {
        var empty = await Loaded();
        empty.IsEmpty.Should().BeTrue();
        empty.EmptyMessage.Should().Be("Nenhuma meta para hoje. Crie uma em Metas.");

        var water = await Create("Água");
        await Create("Mesa", Measure.Completion, 1);
        await CheckinService.AdjustAsync(water.Id, 3);

        var page = await Loaded();

        page.IsEmpty.Should().BeFalse();
        var card = Card(page, "Água");
        (card.ProgressText, card.Fraction, card.IsMet, card.IsQuantity).Should().Be(("3 de 8 copos hoje", 0.375, false, true));
        Card(page, "Mesa").ProgressText.Should().Be("Pendente");
        (page.InProgress, page.Completed, page.WeekPercent).Should().Be((2, 0, 19)); // criadas hoje (quarta): só hoje conta → (3/8 + 0) / 2
    }

    [Fact]
    public async Task Increment_and_decrement_update_progress()
    {
        await Create("Água");
        var page = await Loaded();

        await page.IncrementCommand.ExecuteAsync(Card(page, "Água"));
        await page.IncrementCommand.ExecuteAsync(Card(page, "Água"));
        await page.DecrementCommand.ExecuteAsync(Card(page, "Água"));

        Card(page, "Água").ProgressText.Should().Be("1 de 8 copos hoje");
        page.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Decrement_at_zero_shows_service_message_and_keeps_total()
    {
        await Create("Água");
        var page = await Loaded();

        await page.DecrementCommand.ExecuteAsync(Card(page, "Água"));

        page.ErrorMessage.Should().Be("Não dá para remover mais do que o registrado no período.");
        Card(page, "Água").ProgressText.Should().Be("0 de 8 copos hoje");
    }

    [Fact]
    public async Task Complete_then_uncomplete_completion_goal()
    {
        await Create("Mesa", Measure.Completion, 1);
        var page = await Loaded();

        await page.CompleteCommand.ExecuteAsync(Card(page, "Mesa"));
        (Card(page, "Mesa").IsMet, Card(page, "Mesa").ProgressText, page.Completed).Should().Be((true, "Feito hoje", 1));

        await page.UncompleteCommand.ExecuteAsync(Card(page, "Mesa"));
        Card(page, "Mesa").IsMet.Should().BeFalse();
    }

    [Fact]
    public async Task Panel_adds_minutes_as_seconds()
    {
        await Create("Estudar", Measure.Duration, 7200);
        var page = await Loaded();
        var card = Card(page, "Estudar");

        page.OpenPanelCommand.Execute(card);
        card.PanelHoursInput = 0;
        card.PanelMinutesInput = 25;
        await page.ConfirmPanelCommand.ExecuteAsync(card);

        _checkins.All.Last().Amount.Should().Be(1500);
    }

    [Fact]
    public async Task Panel_removes_30_minutes_from_duration_goal()
    {
        var study = await Create("Estudar", Measure.Duration, 120 * 60);
        await CheckinService.AdjustAsync(study.Id, 90 * 60);
        var page = await Loaded();
        var card = Card(page, "Estudar");

        page.OpenPanelCommand.Execute(card);
        card.IsPanelOpen.Should().BeTrue();
        card.PanelRemoves = true;
        card.PanelMinutesInput = 30;
        card.PanelNote = "contei errado";
        await page.ConfirmPanelCommand.ExecuteAsync(card);

        Card(page, "Estudar").ProgressText.Should().Be(Dur("1 h de 2 h hoje"));
        _checkins.All.Last().Should().Match<Checkin>(c => c.Amount == -1800 && c.Note == "contei errado");
    }

    [Fact]
    public async Task Panel_does_not_open_for_quantity_goal()
    {
        await Create("Água");
        var page = await Loaded();
        var card = Card(page, "Água");

        page.OpenPanelCommand.Execute(card);

        card.IsPanelOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Panel_with_empty_value_shows_informe_um_valor()
    {
        await Create("Estudar", Measure.Duration, 60);
        var page = await Loaded();
        var card = Card(page, "Estudar");

        page.OpenPanelCommand.Execute(card);
        await page.ConfirmPanelCommand.ExecuteAsync(card);

        card.PanelError.Should().Be("Informe um valor.");
        card.IsPanelOpen.Should().BeTrue();
        _checkins.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Panel_note_too_long_keeps_panel_open_with_error()
    {
        await Create("Estudar", Measure.Duration, 60);
        var page = await Loaded();
        var card = Card(page, "Estudar");

        page.OpenPanelCommand.Execute(card);
        card.PanelMinutesInput = 20;
        card.PanelNote = new string('a', 201);
        await page.ConfirmPanelCommand.ExecuteAsync(card);

        card.PanelError.Should().Be("A nota deve ter até 200 caracteres.");
        card.IsPanelOpen.Should().BeTrue();
        page.Goals.Should().Contain(card);
    }

    [Fact]
    public async Task Timer_start_ticks_elapsed_then_stop_suggests_the_time_and_register_adds_it()
    {
        await Create("Estudar", Measure.Duration, 3600);
        var page = await Loaded();

        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        Card(page, "Estudar").IsTimerRunning.Should().BeTrue();
        _time.Advance(TimeSpan.FromSeconds(25 * 60 + 40));
        Card(page, "Estudar").Elapsed.Should().Be("0:25:40");

        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        var stopped = Card(page, "Estudar");
        (stopped.IsTimerRunning, stopped.IsTimerStopped, stopped.StoppedHoursInput, stopped.StoppedMinutesInput, stopped.StoppedSecondsInput)
            .Should().Be((false, true, 0m, 25m, 40m));

        await page.RegisterTimerCommand.ExecuteAsync(stopped);

        var done = Card(page, "Estudar");
        (done.IsTimerStopped, done.ProgressText).Should().Be((false, Dur("25 min 40 s de 1 h hoje")));
        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(25 * 60 + 40);
    }

    [Fact]
    public async Task Reopened_page_shows_running_timer_from_started_at()
    {
        await Create("Estudar", Measure.Duration, 60);
        var first = await Loaded();
        await first.StartTimerCommand.ExecuteAsync(Card(first, "Estudar"));
        _time.Advance(TimeSpan.FromMinutes(7));

        var reopened = await Loaded();

        (Card(reopened, "Estudar").IsTimerRunning, Card(reopened, "Estudar").Elapsed).Should().Be((true, "0:07:00"));
    }

    [Fact]
    public async Task Reopened_page_shows_stopped_timer_with_frozen_minutes()
    {
        await Create("Estudar", Measure.Duration, 60);
        var first = await Loaded();
        await first.StartTimerCommand.ExecuteAsync(Card(first, "Estudar"));
        _time.Advance(TimeSpan.FromMinutes(10));
        await first.StopTimerCommand.ExecuteAsync(Card(first, "Estudar"));
        _time.Advance(TimeSpan.FromHours(1));

        var reopened = await Loaded();

        var card = Card(reopened, "Estudar");
        (card.IsTimerStopped, card.StoppedHoursInput, card.StoppedMinutesInput).Should().Be((true, 0m, 10m));
    }

    [Fact]
    public async Task Timer_stopped_today_keeps_the_plain_message()
    {
        await Create("Estudar", Measure.Duration, 60);
        var page = await Loaded();
        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        _time.Advance(TimeSpan.FromMinutes(10));
        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));

        Card(page, "Estudar").StoppedText.Should().Be("Cronômetro parado. Confira o tempo antes de registrar.");
    }

    [Fact]
    public async Task Timer_stopped_on_another_day_shows_when_it_stopped()
    {
        await Create("Estudar", Measure.Duration, 60);
        var first = await Loaded();
        await first.StartTimerCommand.ExecuteAsync(Card(first, "Estudar"));
        _time.Advance(TimeSpan.FromMinutes(10));
        await first.StopTimerCommand.ExecuteAsync(Card(first, "Estudar"));
        _time.Advance(TimeSpan.FromDays(3));

        var reopened = await Loaded();

        Card(reopened, "Estudar").StoppedText.Should().Be("Cronômetro parado em 04/11, 12:10. Confira o tempo antes de registrar.");
    }

    [Fact]
    public async Task Other_duration_goals_cannot_start_while_timer_exists()
    {
        await Create("Estudar", Measure.Duration, 3600);
        await Create("Violão", Measure.Duration, 1800);
        var page = await Loaded();
        Card(page, "Violão").CanStartTimer.Should().BeTrue();

        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));

        Card(page, "Violão").CanStartTimer.Should().BeFalse();
        Card(page, "Estudar").CanStartTimer.Should().BeFalse();
    }

    [Fact]
    public async Task Today_disables_play_and_shows_the_band_during_a_session()
    {
        var sessions = new InMemoryFocusSessionRepository(_checkins, _timers);
        var focus = new FocusService(_goals, sessions, _timers, CheckinService, new AppSettings(_settings), _time, _changes);
        var navigator = new PageNavigator();
        var shown = new List<Type>();
        navigator.Requested += shown.Add;
        await Create("Estudar", Measure.Duration, 60);
        var page = new TodayPageViewModel(new ProgressService(_goals, _checkins, _timers, _time), CheckinService,
            new TimerService(_goals, _timers, CheckinService, _time, _changes, sessions), GoalService, _time,
            new ReminderService(_goals, _checkins, _timers, _log, _notifications, new ReminderFormatter(), _time, Pause),
            _changes, Pause, focus: focus, navigator: navigator);
        await page.LoadCommand.ExecuteAsync(null);
        (Card(page, "Estudar").CanStartTimer, page.FocusBandText).Should().Be((true, null));

        await focus.StartAsync(FocusPlan.Default, null);
        await page.LoadCommand.ExecutionTask!;

        (Card(page, "Estudar").CanStartTimer, page.FocusBandText, page.HasFocusBand)
            .Should().Be((false, "Sessão de Pomodoro em andamento.", true));
        page.OpenTimePageCommand.Execute(null);
        shown.Should().Equal(typeof(TimePageViewModel));

        _time.Advance(TimeSpan.FromMinutes(1));
        await focus.EndAsync(); // sem meta, terminada à espera de destino: ainda é uma sessão, mas não está andando
        await page.LoadCommand.ExecutionTask!;
        (Card(page, "Estudar").CanStartTimer, page.FocusBandText).Should()
            .Be((false, "Pomodoro concluído. Escolha o destino do tempo na aba Tempo."));

        await focus.DiscardAsync();
        await page.LoadCommand.ExecutionTask!;
        (Card(page, "Estudar").CanStartTimer, page.FocusBandText, page.HasFocusBand).Should().Be((true, null, false));
    }

    [Fact]
    public async Task Archived_goal_with_timer_disappears_without_error()
    {
        var study = await Create("Estudar", Measure.Duration, 60);
        var page = await Loaded();
        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));

        await GoalService.ArchiveAsync(study.Id);
        await page.LoadCommand.ExecuteAsync(null);
        _time.Advance(TimeSpan.FromSeconds(5)); // tick do cronômetro antigo não pode quebrar nada

        page.Goals.Should().BeEmpty();
        page.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Unexpected_error_shows_generic_message()
    {
        await Create("Água");
        var page = await Loaded();
        _checkins.FailWritesWith = new IOException("disco cheio");

        await page.IncrementCommand.ExecuteAsync(Card(page, "Água"));

        page.ErrorMessage.Should().Be("Não foi possível registrar.");
    }

    // Revisão final #1: cronômetro de meta que não aparece hoje (ex.: dia inativo) pode ser parado e descartado.
    [Fact]
    public async Task Timer_of_goal_not_shown_today_appears_in_strip_and_can_be_stopped_and_discarded()
    {
        var weekend = await GoalService.CreateAsync(new GoalDraft("Fim de semana", Recurrence.Daily, 3600, "book", GoalColor.Purple, 60,
            new TimeOnly(8, 0), new TimeOnly(20, 0), WeekDays.Saturday | WeekDays.Sunday, null, null, Measure.Duration, null));
        await Create("Violão", Measure.Duration, 1800);
        await _timers.TryStartAsync(new ActiveTimer(weekend.Id, Now.AddSeconds(-(12 * 60 + 40))));

        var page = await Loaded();

        page.Goals.Select(g => g.Title).Should().Equal("Violão");
        (page.HasOffscreenTimer, page.OffscreenTimerText).Should().Be((true, "Cronômetro rodando em \"Fim de semana\" · 0:12:40"));
        Card(page, "Violão").CanStartTimer.Should().BeFalse();

        await page.StopOffscreenTimerCommand.ExecuteAsync(null);
        page.OffscreenTimerText.Should().Be(Dur("Cronômetro parado em \"Fim de semana\" · 12 min 40 s"));
        await page.DiscardOffscreenTimerCommand.ExecuteAsync(null);

        page.HasOffscreenTimer.Should().BeFalse();
        Card(page, "Violão").CanStartTimer.Should().BeTrue();
        page.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Today_reloads_when_data_changes()
    {
        var water = await Create("Água");
        var page = await Loaded();

        await CheckinService.AdjustAsync(water.Id, 2); // escrita de fora da tela (ex.: notificação)
        _changes.NotifyChanged();
        await page.LoadCommand.ExecutionTask!;

        Card(page, "Água").ProgressText.Should().Be("2 de 8 copos hoje");
    }

    [Fact]
    public async Task Card_shows_next_reminder_of_today()
    {
        await Create("Água");
        _time.Advance(TimeSpan.FromHours(19)); // quinta, 07:00 em São Paulo: primeiro lembrete às 08:00

        var page = await Loaded();

        (Card(page, "Água").NextReminder, Card(page, "Água").HasNextReminder).Should().Be(("08:00", true));
    }

    [Fact]
    public async Task Card_without_reminder_today_shows_no_time()
    {
        await Create("Água");
        _time.Advance(TimeSpan.FromHours(9)); // quarta, 21:00: janela (08–20h) já acabou

        var page = await Loaded();

        (Card(page, "Água").NextReminder, Card(page, "Água").HasNextReminder).Should().Be((null, false));
    }

    [Fact]
    public async Task Met_goal_card_shows_no_time()
    {
        await Create("Mesa", Measure.Completion, 1);
        await Create("Água");
        _time.Advance(TimeSpan.FromHours(19)); // quinta, 07:00: as duas teriam lembrete às 08:00
        var page = await Loaded();

        await page.CompleteCommand.ExecuteAsync(Card(page, "Mesa"));
        await page.LoadCommand.ExecutionTask!;

        (Card(page, "Mesa").IsMet, Card(page, "Mesa").HasNextReminder).Should().Be((true, false));
        Card(page, "Água").NextReminder.Should().Be("08:00");
    }

    [Fact]
    public async Task Date_text_is_long_weekday_and_month()
    {
        var page = await Loaded();

        page.DateText.Should().Be("Quarta-feira, 4 de novembro");
    }

    [Fact]
    public async Task Completed_label_agrees_with_count()
    {
        await Create("Mesa", Measure.Completion, 1);
        await Create("Cama", Measure.Completion, 1);
        var page = await Loaded();
        page.CompletedLabel.Should().Be("Concluída");

        await page.CompleteCommand.ExecuteAsync(Card(page, "Mesa"));
        await page.LoadCommand.ExecutionTask!;
        page.CompletedLabel.Should().Be("Concluída");

        await page.CompleteCommand.ExecuteAsync(Card(page, "Cama"));
        await page.LoadCommand.ExecutionTask!;
        page.CompletedLabel.Should().Be("Concluídas");
    }

    [Fact]
    public async Task Next_notification_is_the_earliest_reminder_today()
    {
        await GoalService.CreateAsync(new GoalDraft("Leitura", Recurrence.Daily, 1, "book", GoalColor.Purple, 60,
            new TimeOnly(9, 0), new TimeOnly(20, 0), WeekDays.All, null, null, Measure.Completion, null));
        await Create("Água");
        _time.Advance(TimeSpan.FromHours(19)); // quinta, 07:00: Água às 08:00, Leitura às 09:00

        var page = await Loaded();

        (page.NextNotificationTitle, page.NextNotificationIn, page.HasNextNotification).Should().Be(("Água", "em 1 h", true));
    }

    [Fact]
    public async Task Next_notification_hidden_when_paused()
    {
        await Create("Água");
        _time.Advance(TimeSpan.FromHours(19));
        await new ReminderPause(_settings, _time).PauseAsync();

        var page = await Loaded();

        page.HasNextNotification.Should().BeFalse();
    }

    [Fact]
    public async Task Next_notification_moves_on_each_minute()
    {
        await GoalService.CreateAsync(new GoalDraft("Água", Recurrence.Daily, 8, "water", GoalColor.Green, 60,
            new TimeOnly(8, 0), new TimeOnly(8, 30), WeekDays.All, null, null, Measure.Quantity, "copos"));
        _time.Advance(TimeSpan.FromHours(19) + TimeSpan.FromMinutes(49)); // quinta, 07:49
        var page = await Loaded();
        page.NextNotificationIn.Should().Be("em 11 min");

        _time.Advance(TimeSpan.FromMinutes(1));
        page.NextNotificationIn.Should().Be("em 10 min");

        _time.Advance(TimeSpan.FromMinutes(11)); // 08:01: o único lembrete de hoje passou
        page.HasNextNotification.Should().BeFalse();
    }

    [Theory]
    [InlineData(30, "agora")]
    [InlineData(11 * 60, "em 11 min")]
    [InlineData(60 * 60, "em 1 h")]
    [InlineData(75 * 60, "em 1 h 15 min")]
    public void Relative_in_reads_like_the_mockup(int seconds, string expected) =>
        TodayPageViewModel.RelativeIn(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Fact]
    public async Task Today_shows_paused_state_and_hides_upcoming()
    {
        await Create("Água");
        _time.Advance(TimeSpan.FromHours(19)); // quinta, 07:00: haveria lembrete às 08:00
        await new ReminderPause(_settings, _time).PauseAsync();

        var page = await Loaded();

        (page.IsPaused, Card(page, "Água").HasNextReminder).Should().Be((true, false));
        page.PauseButtonText.Should().Be("Retomar lembretes");
        page.Goals.Should().ContainSingle(); // o resto da tela continua
    }

    [Fact]
    public async Task Toggle_pause_pauses_then_resumes()
    {
        await Create("Água");
        _time.Advance(TimeSpan.FromHours(19));
        var page = await Loaded();
        (page.IsPaused, page.PauseButtonText, Card(page, "Água").HasNextReminder).Should().Be((false, "Pausar lembretes", true));

        await page.TogglePauseCommand.ExecuteAsync(null);
        await page.LoadCommand.ExecutionTask!;
        (page.IsPaused, page.PauseButtonText, Card(page, "Água").HasNextReminder).Should().Be((true, "Retomar lembretes", false));
        (await Pause.GetAsync()).IsPaused.Should().BeTrue();

        await page.TogglePauseCommand.ExecuteAsync(null);
        await page.LoadCommand.ExecutionTask!;
        (page.IsPaused, page.PauseButtonText, Card(page, "Água").HasNextReminder).Should().Be((false, "Pausar lembretes", true));
    }

    [Fact]
    public async Task Without_pause_toggle_cannot_execute()
    {
        var page = new TodayPageViewModel(
            new ProgressService(_goals, _checkins, _timers, _time), CheckinService,
            new TimerService(_goals, _timers, CheckinService, _time), GoalService, _time,
            new ReminderService(_goals, _checkins, _timers, _log, _notifications, new ReminderFormatter(), _time), _changes);
        await page.LoadCommand.ExecuteAsync(null);

        (page.IsPaused, page.TogglePauseCommand.CanExecute(null)).Should().Be((false, false));
    }

    [Fact]
    public async Task Pause_is_available_without_upcoming_reminders()
    {
        await Create("Água");
        _time.Advance(TimeSpan.FromHours(9)); // quarta, 21:00: janela (08–20h) já acabou
        var page = await Loaded();
        (Card(page, "Água").HasNextReminder, page.CanPause, page.TogglePauseCommand.CanExecute(null)).Should().Be((false, true, true));

        await page.TogglePauseCommand.ExecuteAsync(null);
        await page.LoadCommand.ExecutionTask!;

        (page.IsPaused, page.CanPause).Should().Be((true, false));
        (await Pause.GetAsync()).IsPaused.Should().BeTrue();
    }

    [Fact]
    public async Task Mascot_follows_today_progress()
    {
        var page = await Loaded();
        page.Mascot.Pose.Should().Be(MascotPose.Sleeping);

        var water = await Create("Água");
        await Create("Mesa", Measure.Completion, 1);
        await page.LoadCommand.ExecuteAsync(null);
        page.Mascot.Pose.Should().Be(MascotPose.Idle);

        await page.CompleteCommand.ExecuteAsync(Card(page, "Mesa"));
        (page.Mascot.Pose, page.Mascot.Message).Should().Be((MascotPose.Happy, "Falta pouco!"));

        await CheckinService.AdjustAsync(water.Id, 8);
        await page.LoadCommand.ExecuteAsync(null);
        page.Mascot.Pose.Should().Be(MascotPose.Celebrating);

        await page.UncompleteCommand.ExecuteAsync(Card(page, "Mesa"));
        page.Mascot.Pose.Should().Be(MascotPose.Happy);
    }

    [Fact]
    public async Task Mascot_sleeps_while_paused()
    {
        await Create("Água");
        var page = await Loaded();
        page.Mascot.Pose.Should().Be(MascotPose.Idle);

        await page.TogglePauseCommand.ExecuteAsync(null);
        await page.LoadCommand.ExecuteAsync(null);
        page.Mascot.Pose.Should().Be(MascotPose.Sleeping);

        await page.TogglePauseCommand.ExecuteAsync(null);
        await page.LoadCommand.ExecuteAsync(null);
        page.Mascot.Pose.Should().Be(MascotPose.Idle);
    }

    [Fact]
    public async Task Mascot_shows_only_after_first_load()
    {
        await Create("Água");
        var page = NewPage();
        page.HasMascot.Should().BeFalse();

        await page.LoadCommand.ExecuteAsync(null);

        (page.HasMascot, page.Mascot.Pose).Should().Be((true, MascotPose.Idle));
    }

    [Fact]
    public async Task Mascot_stays_hidden_when_first_load_fails()
    {
        var page = NewPage();
        _goals.FailReadsWith = new IOException("x");

        await page.LoadCommand.ExecuteAsync(null);

        page.HasMascot.Should().BeFalse();
    }

    [Fact]
    public async Task Mascot_keeps_pose_when_load_fails()
    {
        var goal = await Create("Mesa", Measure.Completion, 1);
        await CheckinService.AdjustAsync(goal.Id, 1);
        var page = await Loaded();
        page.Mascot.Pose.Should().Be(MascotPose.Celebrating);

        _goals.FailReadsWith = new IOException("x");
        await page.LoadCommand.ExecuteAsync(null);

        (page.ErrorMessage, page.Mascot.Pose).Should().Be(("Não foi possível carregar o dia.", MascotPose.Celebrating));
    }

    // ---- Tarefa do cronômetro (specs/Tarefas_no_cronometro_v1.md) ----

    private async Task<TodayPageViewModel> RunningStudyAsync(string? task = null)
    {
        var goal = await Create("Estudar", Measure.Duration, 60, unit: null);
        await Timers.StartAsync(goal.Id);
        if (task is not null)
            await Timers.SetTaskAsync(task);
        return await Loaded();
    }

    [Fact]
    public async Task Running_item_shows_the_task_and_suggestions()
    {
        var goal = await Create("Estudar", Measure.Duration, 60, unit: null);
        await CheckinService.AdjustAsync(goal.Id, 10, "Inglês");
        await Timers.StartAsync(goal.Id);
        await Timers.SetTaskAsync("Capítulo 3");

        var page = await Loaded();

        var card = Card(page, "Estudar");
        (card.TimerTask, card.TaskInput).Should().Be(("Capítulo 3", "Capítulo 3"));
        card.TaskSuggestions.Should().Contain("Inglês");
    }

    [Fact]
    public async Task Timer_started_from_a_reminder_has_an_empty_task_field()
    {
        var page = await RunningStudyAsync();
        var card = Card(page, "Estudar");
        card.TaskInput.Should().BeNull();

        card.TaskInput = " Ler ";
        await page.SaveTaskCommand.ExecuteAsync(card);

        (await _timers.GetAsync())!.Task.Should().Be("Ler");
        Card(page, "Estudar").TaskInput.Should().Be("Ler");
    }

    [Fact]
    public async Task Save_task_unchanged_does_not_write()
    {
        var page = await RunningStudyAsync("Ler");
        var calls = _timers.SetTaskCalls;
        var card = Card(page, "Estudar");

        card.TaskInput = "Ler ";
        await page.SaveTaskCommand.ExecuteAsync(card);

        _timers.SetTaskCalls.Should().Be(calls);
    }

    [Fact]
    public async Task Confirm_switch_registers_and_renames()
    {
        var page = await RunningStudyAsync("Inglês");
        _time.Advance(TimeSpan.FromMinutes(25));
        var card = Card(page, "Estudar");

        page.BeginSwitchTaskCommand.Execute(card);
        (card.IsSwitchingTask, card.TaskInput).Should().Be((true, null));
        card.TaskInput = "Capítulo 3";
        await page.ConfirmSwitchTaskCommand.ExecuteAsync(card);

        _checkins.All.Should().ContainSingle().Which.Should().Match<Checkin>(c => c.Amount == 25 * 60 && c.Note == "Inglês");
        var after = Card(page, "Estudar");
        (after.TimerTask, after.TaskInput, after.IsSwitchingTask).Should().Be(("Capítulo 3", "Capítulo 3", false));
    }

    [Fact]
    public async Task Cancel_switch_restores_the_current_name()
    {
        var page = await RunningStudyAsync("Inglês");
        var card = Card(page, "Estudar");
        page.BeginSwitchTaskCommand.Execute(card);
        card.TaskInput = "Outra";

        page.CancelSwitchTaskCommand.Execute(card);

        (card.IsSwitchingTask, card.TaskInput).Should().Be((false, "Inglês"));
    }

    [Fact]
    public async Task Stopped_timer_panel_starts_with_the_task_as_note()
    {
        var page = await RunningStudyAsync("Inglês");
        _time.Advance(TimeSpan.FromMinutes(10));

        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));

        Card(page, "Estudar").PanelNote.Should().Be("Inglês");
    }

    [Fact]
    public async Task Typing_survives_a_reload()
    {
        var page = await RunningStudyAsync("Inglês");
        var card = Card(page, "Estudar");
        page.BeginSwitchTaskCommand.Execute(card);
        card.TaskInput = "Rev";

        _changes.NotifyChanged(); // como uma sincronização ou outro processo faria

        var reloaded = Card(page, "Estudar");
        reloaded.Should().NotBeSameAs(card);
        (reloaded.TaskInput, reloaded.IsSwitchingTask).Should().Be(("Rev", true));

        await Timers.StopAsync();
        await page.LoadCommand.ExecuteAsync(null);
        await Timers.DiscardAsync();
        var goal = (await GoalService.ListAsync()).Single();
        await Timers.StartAsync(goal.Id);
        await page.LoadCommand.ExecuteAsync(null);

        (Card(page, "Estudar").TaskInput, Card(page, "Estudar").IsSwitchingTask).Should().Be((null, false));
    }

    [Fact]
    public async Task Switch_race_shows_the_error()
    {
        var page = await RunningStudyAsync("Inglês");
        _time.Advance(TimeSpan.FromMinutes(5));
        var card = Card(page, "Estudar");
        page.BeginSwitchTaskCommand.Execute(card);
        card.TaskInput = "B";
        await Timers.DiscardAsync(); // o "Parar" ou o "Descartar" de outro processo

        await page.ConfirmSwitchTaskCommand.ExecuteAsync(card);

        page.ErrorMessage.Should().Be("O cronômetro mudou. Confira em Hoje.");
        _checkins.All.Should().BeEmpty();
        (await _timers.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Stop_keeps_the_task_typed_but_not_saved()
    {
        // Tocar ■ com o campo em edição: o toque não pode depender de o campo ter gravado antes.
        var page = await RunningStudyAsync("Inglês");
        Card(page, "Estudar").TaskInput = "Gramática";

        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));

        (await _timers.GetAsync())!.Task.Should().Be("Gramática");
        Card(page, "Estudar").PanelNote.Should().Be("Gramática");
    }

    [Fact]
    public async Task Begin_switch_saves_the_task_typed_first()
    {
        var page = await RunningStudyAsync("Inglês");
        Card(page, "Estudar").TaskInput = "Gramática";

        page.BeginSwitchTaskCommand.Execute(Card(page, "Estudar"));

        (await _timers.GetAsync())!.Task.Should().Be("Gramática");
        var card = Card(page, "Estudar");
        card.IsSwitchingTask.Should().BeTrue();
        card.TaskInput.Should().BeNullOrEmpty();
    }

    // ---- Campo da tarefa com estados (specs/Tarefas_no_cronometro_v1.md §7.1) ----

    [Fact]
    public async Task Saved_task_shows_as_label()
    {
        var page = await RunningStudyAsync("Inglês");

        var card = Card(page, "Estudar");
        (card.ShowsTaskLabel, card.ShowsTaskField, card.TaskLabel).Should().Be((true, false, "▸ Inglês"));
    }

    [Fact]
    public async Task Running_timer_without_task_shows_the_field_with_confirm()
    {
        var page = await RunningStudyAsync();

        var card = Card(page, "Estudar");
        (card.ShowsTaskLabel, card.ShowsTaskField, card.ShowsConfirmTask, card.IsEditingTask).Should().Be((false, true, true, false));
    }

    [Fact]
    public async Task Edit_task_opens_the_field_with_the_current_name()
    {
        var page = await RunningStudyAsync("Inglês");
        var card = Card(page, "Estudar");

        page.BeginEditTaskCommand.Execute(card);

        (card.ShowsTaskLabel, card.ShowsTaskField, card.ShowsConfirmTask, card.TaskInput).Should().Be((false, true, true, "Inglês"));
    }

    [Fact]
    public async Task Confirm_edit_saves_and_closes()
    {
        var page = await RunningStudyAsync("Inglês");
        var card = Card(page, "Estudar");
        page.BeginEditTaskCommand.Execute(card);
        card.TaskInput = "Leitura";

        await page.SaveTaskCommand.ExecuteAsync(card);

        var after = Card(page, "Estudar");
        (after.TimerTask, after.IsEditingTask, after.ShowsTaskLabel).Should().Be(("Leitura", false, true));
    }

    [Fact]
    public async Task Confirm_unchanged_edit_closes_without_writing()
    {
        var page = await RunningStudyAsync("Inglês");
        var calls = _timers.SetTaskCalls;
        var card = Card(page, "Estudar");
        page.BeginEditTaskCommand.Execute(card);

        await page.SaveTaskCommand.ExecuteAsync(card);

        (card.IsEditingTask, card.ShowsTaskLabel).Should().Be((false, true));
        _timers.SetTaskCalls.Should().Be(calls);
    }

    [Fact]
    public async Task Cancel_edit_restores_the_name()
    {
        var page = await RunningStudyAsync("Inglês");
        var card = Card(page, "Estudar");
        page.BeginEditTaskCommand.Execute(card);
        card.TaskInput = "Outra";

        page.CancelEditTaskCommand.Execute(card);

        (card.IsEditingTask, card.TaskInput, card.ShowsTaskLabel).Should().Be((false, "Inglês", true));
    }

    [Fact]
    public async Task Reload_keeps_the_task_being_edited()
    {
        var page = await RunningStudyAsync("Inglês");
        var card = Card(page, "Estudar");
        page.BeginEditTaskCommand.Execute(card);
        card.TaskInput = "Lei";

        _changes.NotifyChanged();

        var reloaded = Card(page, "Estudar");
        reloaded.Should().NotBeSameAs(card);
        (reloaded.IsEditingTask, reloaded.TaskInput).Should().Be((true, "Lei"));
    }

    [Fact]
    public async Task Reload_keeps_an_unchanged_edit_open()
    {
        var page = await RunningStudyAsync("Inglês");
        page.BeginEditTaskCommand.Execute(Card(page, "Estudar"));

        _changes.NotifyChanged();

        (Card(page, "Estudar").IsEditingTask, Card(page, "Estudar").TaskInput).Should().Be((true, "Inglês"));
    }

    // ---- Trechos de hoje no cartão (specs/Tarefas_no_cronometro_v1.md §7.2) ----

    private async Task<Goal> StudyWithRecordsAsync()
    {
        var goal = await Create("Estudar", Measure.Duration, 3600, unit: null);
        await CheckinService.AdjustAsync(goal.Id, 25 * 60, "Inglês");
        _time.Advance(TimeSpan.FromMinutes(1));
        await CheckinService.AdjustAsync(goal.Id, 12 * 60);
        return goal;
    }

    private static IEnumerable<(string Name, string Text)> Rows(TodayGoalViewModel card) =>
        card.Chunks.Select(c => (c.Name, c.Text));

    [Fact]
    public async Task Row_panels_open_with_the_ellipsis_and_stay_while_chunks_are_open()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();
        var card = Card(page, "Estudar");
        card.ShowsRowPanels.Should().BeFalse(); // na linha do desktop, a faixa dos trechos não fica à vista

        page.OpenPanelCommand.Execute(card);
        (card.ShowsRowPanels, card.HasChunks).Should().Be((true, true));

        page.ToggleChunksCommand.Execute(card);
        page.CancelPanelCommand.Execute(card);
        card.ShowsRowPanels.Should().BeTrue();

        page.ToggleChunksCommand.Execute(card);
        card.ShowsRowPanels.Should().BeFalse();
    }

    [Fact]
    public async Task Duration_card_lists_todays_records_newest_first()
    {
        await StudyWithRecordsAsync();

        var card = Card(await Loaded(), "Estudar");

        Rows(card).Should().Equal(("Sem nome", Dur("12 min")), ("Inglês", Dur("25 min")));
        (card.HasChunks, card.ChunksHeader, card.IsChunksOpen).Should().Be((true, Dur("Hoje · 37 min"), false));
        card.Chunks.Should().OnlyContain(c => c.CanEdit && !c.IsRunning);
    }

    [Fact]
    public async Task Card_shows_seconds_in_progress_header_and_chunks()
    {
        var goal = await Create("Estudar", Measure.Duration, 9000, unit: null);
        await CheckinService.AdjustAsync(goal.Id, 4200, "Projeto");
        _time.Advance(TimeSpan.FromMinutes(1));
        await CheckinService.AdjustAsync(goal.Id, 160, "Revisar capítulo 3");

        var card = Card(await Loaded(), "Estudar");

        card.ProgressText.Should().Be(Dur("1 h 12 min 40 s de 2 h 30 min hoje"));
        card.ChunksHeader.Should().Be(Dur("Hoje · 1 h 12 min 40 s"));
        Rows(card).Should().Equal(("Revisar capítulo 3", Dur("2 min 40 s")), ("Projeto", Dur("1 h 10 min")));
    }

    [Fact]
    public async Task Card_without_records_today_has_no_list()
    {
        await Create("Estudar", Measure.Duration, 60, unit: null);
        var water = await Create("Água");
        await CheckinService.AdjustAsync(water.Id, 2);

        var page = await Loaded();

        (Card(page, "Estudar").HasChunks, Card(page, "Água").HasChunks).Should().Be((false, false));
    }

    [Fact]
    public async Task Running_chunk_comes_first_and_is_not_editable()
    {
        var goal = await StudyWithRecordsAsync();
        await Timers.StartAsync(goal.Id);
        await Timers.SetTaskAsync("Leitura");
        _time.Advance(TimeSpan.FromMinutes(3));

        var card = Card(await Loaded(), "Estudar");

        var running = card.Chunks[0];
        (running.IsRunning, running.CanEdit, running.Name, running.Text, running.CheckinId).Should().Be((true, false, "Leitura", "0:03:00", null));
        card.ChunksHeader.Should().Be(Dur("Hoje · 37 min"));
        _time.Advance(TimeSpan.FromSeconds(5));
        card.UpdateElapsed();
        running.Text.Should().Be("0:03:05");
    }

    [Fact]
    public async Task Compensation_record_is_not_editable()
    {
        var goal = await Create("Estudar", Measure.Duration, 60, unit: null);
        await _checkins.AddAsync(new Checkin
        {
            Id = Guid.CreateVersion7(), GoalId = goal.Id, DoneAt = Now, Amount = 5, UpdatedAt = Now,
            Compensation = new Compensation(new DateOnly(2026, 11, 4), []),
        });

        Card(await Loaded(), "Estudar").Chunks.Should().ContainSingle().Which.CanEdit.Should().BeFalse();
    }

    [Fact]
    public async Task Negative_record_shows_with_minus()
    {
        var goal = await StudyWithRecordsAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        await CheckinService.AdjustAsync(goal.Id, -5 * 60);

        var card = Card(await Loaded(), "Estudar");

        (card.Chunks[0].Text, card.ChunksHeader).Should().Be((Dur("-5 min"), Dur("Hoje · 32 min")));
    }

    [Fact]
    public async Task Toggle_survives_reload()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();

        page.ToggleChunksCommand.Execute(Card(page, "Estudar"));
        _changes.NotifyChanged();

        Card(page, "Estudar").IsChunksOpen.Should().BeTrue();
        page.ToggleChunksCommand.Execute(Card(page, "Estudar"));
        _changes.NotifyChanged();
        Card(page, "Estudar").IsChunksOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Edit_chunk_prefills_and_closes_other_editors()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();
        var (unnamed, english) = (Card(page, "Estudar").Chunks[0], Card(page, "Estudar").Chunks[1]);

        page.EditChunkCommand.Execute(unnamed);
        page.EditChunkCommand.Execute(english);

        unnamed.IsEditing.Should().BeFalse();
        (english.IsEditing, english.HoursInput, english.MinutesInput, english.NoteInput).Should().Be((true, 0m, 25m, "Inglês"));
        page.CancelChunkEditCommand.Execute(english);
        english.IsEditing.Should().BeFalse();
    }

    [Fact]
    public async Task Save_chunk_replaces_the_record()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();
        var english = Card(page, "Estudar").Chunks[1];
        page.EditChunkCommand.Execute(english);
        english.MinutesInput = 20;
        english.NoteInput = "Inglês 2";

        await page.SaveChunkCommand.ExecuteAsync(english);

        var card = Card(page, "Estudar");
        (card.ChunksHeader, card.Chunks.Any(c => c.IsEditing)).Should().Be((Dur("Hoje · 32 min"), false));
        Rows(card).Should().Contain(("Inglês 2", Dur("20 min")));
    }

    [Fact]
    public async Task Delete_chunk_removes_it()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();
        var unnamed = Card(page, "Estudar").Chunks[0];
        page.EditChunkCommand.Execute(unnamed);

        await page.DeleteChunkCommand.ExecuteAsync(unnamed);

        Rows(Card(page, "Estudar")).Should().Equal(("Inglês", Dur("25 min")));
    }

    [Fact]
    public async Task Chunk_error_stays_in_the_editor()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();
        var unnamed = Card(page, "Estudar").Chunks[0];
        page.EditChunkCommand.Execute(unnamed);
        await _checkins.DeleteAsync(unnamed.CheckinId!.Value); // outro aparelho apagou

        unnamed.MinutesInput = 10;
        await page.SaveChunkCommand.ExecuteAsync(unnamed);

        (unnamed.IsEditing, unnamed.Error, page.ErrorMessage).Should().Be((true, "O registro mudou. Confira em Hoje.", null));
    }

    [Fact]
    public async Task Reload_keeps_the_open_chunk_editor()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();
        var english = Card(page, "Estudar").Chunks[1];
        page.EditChunkCommand.Execute(english);
        (english.HoursInput, english.MinutesInput) = (1, 18);

        _changes.NotifyChanged();

        var reloaded = Card(page, "Estudar").Chunks[1];
        reloaded.Should().NotBeSameAs(english);
        (reloaded.IsEditing, reloaded.HoursInput, reloaded.MinutesInput, reloaded.NoteInput).Should().Be((true, 1m, 18m, "Inglês"));
    }

    [Fact]
    public async Task Empty_time_fields_show_inform_value()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();
        var english = Card(page, "Estudar").Chunks[1];
        page.EditChunkCommand.Execute(english);
        (english.HoursInput, english.MinutesInput, english.SecondsInput) = (null, null, null);

        await page.SaveChunkCommand.ExecuteAsync(english);

        english.Error.Should().Be("Informe um valor.");
        _checkins.All.Should().HaveCount(2);
    }

    [Fact]
    public async Task Chunk_editor_works_in_hours_minutes_and_seconds()
    {
        var goal = await Create("Estudar", Measure.Duration, 300 * 60, unit: null);
        await CheckinService.AdjustAsync(goal.Id, 150 * 60, "Projeto");
        var page = await Loaded();
        var chunk = Card(page, "Estudar").Chunks.Single();
        chunk.Text.Should().Be(Dur("2 h 30 min"));

        page.EditChunkCommand.Execute(chunk);
        (chunk.HoursInput, chunk.MinutesInput).Should().Be((2m, 30m));
        (chunk.HoursInput, chunk.MinutesInput) = (3, null);
        await page.SaveChunkCommand.ExecuteAsync(chunk);

        Rows(Card(page, "Estudar")).Should().Equal(("Projeto", Dur("3 h")));
    }

    [Fact]
    public async Task Stopped_timer_is_reviewed_in_hours_and_minutes()
    {
        await Create("Estudar", Measure.Duration, 300 * 60, unit: null);
        var page = await Loaded();
        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        _time.Advance(TimeSpan.FromMinutes(125));
        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));

        var stopped = Card(page, "Estudar");
        (stopped.StoppedHoursInput, stopped.StoppedMinutesInput).Should().Be((2m, 5m));
        (stopped.StoppedHoursInput, stopped.StoppedMinutesInput) = (1, null);
        await page.RegisterTimerCommand.ExecuteAsync(stopped);

        Card(page, "Estudar").ProgressText.Should().Be(Dur("1 h de 5 h hoje"));
        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(3600);
    }

    [Fact]
    public async Task Chunk_editor_saved_unchanged_keeps_exact_seconds()
    {
        var goal = await Create("Estudar", Measure.Duration, 3600, unit: null);
        await CheckinService.AdjustAsync(goal.Id, 160, "Revisar capítulo 3");
        var page = await Loaded();
        var chunk = Card(page, "Estudar").Chunks.Single();
        var id = chunk.CheckinId;

        page.EditChunkCommand.Execute(chunk);
        (chunk.HoursInput, chunk.MinutesInput, chunk.SecondsInput).Should().Be((0m, 2m, 40m));
        await page.SaveChunkCommand.ExecuteAsync(chunk);

        chunk.Error.Should().BeNull();
        _checkins.All.Should().ContainSingle().Which.Should().Match<Checkin>(c => c.Id == id && c.Amount == 160);
    }

    [Fact]
    public async Task Negative_chunk_editor_keeps_the_sign_and_the_seconds()
    {
        var goal = await StudyWithRecordsAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        await CheckinService.AdjustAsync(goal.Id, -160);
        var page = await Loaded();
        var negative = Card(page, "Estudar").Chunks[0];
        var id = negative.CheckinId;

        page.EditChunkCommand.Execute(negative);
        (negative.HoursInput, negative.MinutesInput, negative.SecondsInput).Should().Be((0m, 2m, 40m));
        await page.SaveChunkCommand.ExecuteAsync(negative);

        negative.Error.Should().BeNull();
        _checkins.All.Should().ContainSingle(c => c.Amount < 0).Which.Should().Match<Checkin>(c => c.Id == id && c.Amount == -160);

        var reloaded = Card(page, "Estudar").Chunks[0];
        page.EditChunkCommand.Execute(reloaded);
        (reloaded.HoursInput, reloaded.MinutesInput, reloaded.SecondsInput) = (0, 3, 5);
        await page.SaveChunkCommand.ExecuteAsync(reloaded);

        reloaded.Error.Should().BeNull();
        _checkins.All.Should().ContainSingle(c => c.Amount < 0).Which.Amount.Should().Be(-185);
        Rows(Card(page, "Estudar")).First().Should().Be(("Sem nome", Dur("-3 min 5 s")));
    }

    [Fact]
    public async Task Chunk_editor_saves_seconds()
    {
        var goal = await Create("Estudar", Measure.Duration, 3600, unit: null);
        await CheckinService.AdjustAsync(goal.Id, 160, "Revisar capítulo 3");
        var page = await Loaded();
        var chunk = Card(page, "Estudar").Chunks.Single();

        page.EditChunkCommand.Execute(chunk);
        (chunk.MinutesInput, chunk.SecondsInput) = (3, 5);
        await page.SaveChunkCommand.ExecuteAsync(chunk);

        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(185);
        Rows(Card(page, "Estudar")).Should().Equal(("Revisar capítulo 3", Dur("3 min 5 s")));
    }

    [Fact]
    public async Task Chunk_editor_accepts_only_seconds()
    {
        var goal = await Create("Estudar", Measure.Duration, 3600, unit: null);
        await CheckinService.AdjustAsync(goal.Id, 160);
        var page = await Loaded();
        var chunk = Card(page, "Estudar").Chunks.Single();

        page.EditChunkCommand.Execute(chunk);
        (chunk.HoursInput, chunk.MinutesInput, chunk.SecondsInput) = (null, null, 45);
        await page.SaveChunkCommand.ExecuteAsync(chunk);

        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(45);
    }

    [Fact]
    public async Task Reload_keeps_the_typed_seconds_of_the_chunk_editor()
    {
        await StudyWithRecordsAsync();
        var page = await Loaded();
        var english = Card(page, "Estudar").Chunks[1];
        page.EditChunkCommand.Execute(english);
        english.SecondsInput = 7;

        _changes.NotifyChanged();

        Card(page, "Estudar").Chunks[1].SecondsInput.Should().Be(7m);
    }

    [Fact]
    public async Task Stopped_timer_review_shows_and_registers_seconds()
    {
        await Create("Estudar", Measure.Duration, 3600, unit: null);
        var page = await Loaded();
        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        _time.Advance(TimeSpan.FromSeconds(160) + TimeSpan.FromMilliseconds(700));
        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));

        var stopped = Card(page, "Estudar");
        (stopped.StoppedHoursInput, stopped.StoppedMinutesInput, stopped.StoppedSecondsInput).Should().Be((0m, 2m, 40m));
        await page.RegisterTimerCommand.ExecuteAsync(stopped);

        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(160);
        Card(page, "Estudar").ProgressText.Should().Be(Dur("2 min 40 s de 1 h hoje"));
    }

    [Fact]
    public async Task Stopped_timer_accepts_only_seconds()
    {
        await Create("Estudar", Measure.Duration, 3600, unit: null);
        var page = await Loaded();
        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        _time.Advance(TimeSpan.FromMinutes(10));
        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        var stopped = Card(page, "Estudar");
        (stopped.StoppedHoursInput, stopped.StoppedMinutesInput, stopped.StoppedSecondsInput) = (null, null, 30);

        await page.RegisterTimerCommand.ExecuteAsync(stopped);

        _checkins.All.Should().ContainSingle().Which.Amount.Should().Be(30);
    }

    [Fact]
    public async Task Stopped_timer_without_time_asks_for_a_value()
    {
        await Create("Estudar", Measure.Duration, 60, unit: null);
        var page = await Loaded();
        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        _time.Advance(TimeSpan.FromMinutes(10));
        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        var stopped = Card(page, "Estudar");
        (stopped.StoppedHoursInput, stopped.StoppedMinutesInput, stopped.StoppedSecondsInput) = (null, null, null);

        await page.RegisterTimerCommand.ExecuteAsync(stopped);

        (stopped.PanelError, Card(page, "Estudar").IsTimerStopped).Should().Be(("Informe um valor.", true));
    }
}
