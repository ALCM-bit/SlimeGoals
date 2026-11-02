using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class TodayPageViewModelTests
{
    // Quarta, 04/11/2026 12:00 em São Paulo (UTC-3).
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();

    public TodayPageViewModelTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private GoalService GoalService => new(_goals, _checkins, _timers, _time);
    private CheckinService CheckinService => new(_goals, _checkins, _time);

    private TodayPageViewModel NewPage() => new(
        new ProgressService(_goals, _checkins, _timers, _time), CheckinService,
        new TimerService(_goals, _timers, CheckinService, _time), GoalService, _time);

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

    private static TodayGoalViewModel Card(TodayPageViewModel page, string title) => page.Goals.Single(g => g.Title == title);

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
        (page.InProgress, page.Completed, page.WeekPercent).Should().Be((2, 0, 0));
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
    public async Task Panel_removes_30_minutes_from_duration_goal()
    {
        var study = await Create("Estudar", Measure.Duration, 120);
        await CheckinService.AdjustAsync(study.Id, 90);
        var page = await Loaded();
        var card = Card(page, "Estudar");

        page.OpenPanelCommand.Execute(card);
        card.IsPanelOpen.Should().BeTrue();
        card.PanelRemoves = true;
        card.PanelMinutesInput = 30;
        card.PanelNote = "contei errado";
        await page.ConfirmPanelCommand.ExecuteAsync(card);

        Card(page, "Estudar").ProgressText.Should().Be("1 h de 2 h hoje");
        _checkins.All.Last().Should().Match<Checkin>(c => c.Amount == -30 && c.Note == "contei errado");
    }

    [Fact]
    public async Task Panel_with_empty_value_shows_informe_um_valor()
    {
        await Create("Água");
        var page = await Loaded();
        var card = Card(page, "Água");

        page.OpenPanelCommand.Execute(card);
        await page.ConfirmPanelCommand.ExecuteAsync(card);

        card.PanelError.Should().Be("Informe um valor.");
        card.IsPanelOpen.Should().BeTrue();
        _checkins.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Panel_note_too_long_keeps_panel_open_with_error()
    {
        await Create("Água");
        var page = await Loaded();
        var card = Card(page, "Água");

        page.OpenPanelCommand.Execute(card);
        card.PanelAmountInput = 2;
        card.PanelNote = new string('a', 201);
        await page.ConfirmPanelCommand.ExecuteAsync(card);

        card.PanelError.Should().Be("A nota deve ter até 200 caracteres.");
        card.IsPanelOpen.Should().BeTrue();
        page.Goals.Should().Contain(card);
    }

    [Fact]
    public async Task Timer_start_ticks_elapsed_then_stop_suggests_minutes_and_register_adds_them()
    {
        await Create("Estudar", Measure.Duration, 60);
        var page = await Loaded();

        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        Card(page, "Estudar").IsTimerRunning.Should().BeTrue();
        _time.Advance(TimeSpan.FromSeconds(25 * 60 + 40));
        Card(page, "Estudar").Elapsed.Should().Be("0:25:40");

        await page.StopTimerCommand.ExecuteAsync(Card(page, "Estudar"));
        var stopped = Card(page, "Estudar");
        (stopped.IsTimerRunning, stopped.IsTimerStopped, stopped.StoppedMinutesInput).Should().Be((false, true, 26m));

        await page.RegisterTimerCommand.ExecuteAsync(stopped);

        var done = Card(page, "Estudar");
        (done.IsTimerStopped, done.ProgressText).Should().Be((false, "26 min de 1 h hoje"));
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

        (Card(reopened, "Estudar").IsTimerStopped, Card(reopened, "Estudar").StoppedMinutesInput).Should().Be((true, 10m));
    }

    [Fact]
    public async Task Other_duration_goals_cannot_start_while_timer_exists()
    {
        await Create("Estudar", Measure.Duration, 60);
        await Create("Violão", Measure.Duration, 30);
        var page = await Loaded();
        Card(page, "Violão").CanStartTimer.Should().BeTrue();

        await page.StartTimerCommand.ExecuteAsync(Card(page, "Estudar"));

        Card(page, "Violão").CanStartTimer.Should().BeFalse();
        Card(page, "Estudar").CanStartTimer.Should().BeFalse();
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
        var weekend = await GoalService.CreateAsync(new GoalDraft("Fim de semana", Recurrence.Daily, 60, "book", GoalColor.Purple, 60,
            new TimeOnly(8, 0), new TimeOnly(20, 0), WeekDays.Saturday | WeekDays.Sunday, null, null, Measure.Duration, null));
        await Create("Violão", Measure.Duration, 30);
        await _timers.TryStartAsync(new ActiveTimer(weekend.Id, Now.AddMinutes(-12)));

        var page = await Loaded();

        page.Goals.Select(g => g.Title).Should().Equal("Violão");
        (page.HasOffscreenTimer, page.OffscreenTimerText).Should().Be((true, "Cronômetro rodando em \"Fim de semana\" · 0:12:00"));
        Card(page, "Violão").CanStartTimer.Should().BeFalse();

        await page.StopOffscreenTimerCommand.ExecuteAsync(null);
        page.OffscreenTimerText.Should().Be("Cronômetro parado em \"Fim de semana\" · 12 min");
        await page.DiscardOffscreenTimerCommand.ExecuteAsync(null);

        page.HasOffscreenTimer.Should().BeFalse();
        Card(page, "Violão").CanStartTimer.Should().BeTrue();
        page.ErrorMessage.Should().BeNull();
    }
}
