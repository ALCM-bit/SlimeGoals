using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class ReportsPageViewModelTests
{
    // Quarta, 04/11/2026 12:00 em São Paulo (UTC-3).
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly DataChangeNotifier _changes = new();

    public ReportsPageViewModelTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private GoalService GoalService => new(_goals, _checkins, _timers, _time);
    private CheckinService CheckinService => new(_goals, _checkins, _time, _changes);

    private static ReportsPageViewModel NewPage(IGoalRepository goals, ICheckinRepository checkins,
        IReminderLogRepository log, TimeProvider time, DataChangeNotifier changes) =>
        new(new ReportService(goals, checkins, log, time), time, changes);

    private async Task<ReportsPageViewModel> Loaded()
    {
        var page = NewPage(_goals, _checkins, _log, _time, _changes);
        await page.LoadCommand.ExecuteAsync(null);
        return page;
    }

    private Task<Goal> CreateDaily(string title = "Mesa") =>
        GoalService.CreateAsync(new GoalDraft(title, Recurrence.Daily, 1, "water", GoalColor.Green, 60,
            new TimeOnly(8, 0), new TimeOnly(20, 0), WeekDays.All, null, null, Measure.Completion, null));

    [Fact]
    public async Task Opens_on_current_week_with_bars_and_rows()
    {
        // Meta de 02/11, cumprida em 02/11 e 03/11; hoje (04/11) conta como pendente até ser cumprido.
        var goal = await CreateDaily();
        await _goals.UpdateAsync(goal with { CreatedAt = new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero) });
        foreach (var day in new[] { 2, 3 })
            await _checkins.AddAsync(new Checkin
            {
                Id = Guid.NewGuid(), GoalId = goal.Id, Amount = 1, UpdatedAt = Now,
                DoneAt = new DateTimeOffset(2026, 11, day, 13, 0, 0, TimeSpan.Zero),
            });

        var page = await Loaded();

        page.PeriodTitle.Should().Be("2 a 8 nov 2026");
        page.Bars.Count.Should().Be(7);
        page.Bars[2].IsCurrent.Should().BeTrue();
        page.Rows.Single().Detail.Should().Be("2 de 3 dias");
        page.Rows.Single().PercentText.Should().Be("67%");
        page.RemindersText.Should().Be("0 disparados · 0 respondidos");
        page.NextCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Current_day_bar_starts_selected_and_lists_its_goals()
    {
        var goal = await CreateDaily("Mesa");
        await _goals.UpdateAsync(goal with { CreatedAt = new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero) });
        await _checkins.AddAsync(new Checkin
        {
            Id = Guid.NewGuid(), GoalId = goal.Id, Amount = 1, UpdatedAt = Now,
            DoneAt = new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero),
        });

        var page = await Loaded();

        page.SelectedBar.Should().BeSameAs(page.Bars[2]);
        page.Bars[2].IsSelected.Should().BeTrue();
        page.SelectedBar!.DetailTitle.Should().Be("Quarta, 4 nov");
        page.SelectedBar.Details.Should().BeEmpty();
        page.SelectedBar.EmptyDetail.Should().Be("Nenhum registro neste dia.");

        page.SelectBarCommand.Execute(page.Bars[0]);

        page.Bars[2].IsSelected.Should().BeFalse();
        page.Bars[0].IsSelected.Should().BeTrue();
        page.SelectedBar!.DetailTitle.Should().Be("Segunda, 2 nov");
        page.SelectedBar.Details.Select(d => (d.Title, d.Value, d.IsMet)).Should().Equal(("Mesa", "Feito", true));
    }

    [Fact]
    public async Task Selection_survives_reload_and_resets_on_navigation()
    {
        var goal = await CreateDaily();
        var page = await Loaded();
        page.SelectBarCommand.Execute(page.Bars[0]);

        await CheckinService.AdjustAsync(goal.Id, 1); // recarga pelo aviso de mudança

        page.SelectedBar.Should().BeSameAs(page.Bars[0]);
        page.Bars[0].IsSelected.Should().BeTrue();

        await page.PreviousCommand.ExecuteAsync(null);

        page.SelectedBar.Should().BeNull(); // semana passada: nenhuma barra é a de hoje
        page.Bars.Should().OnlyContain(b => !b.IsSelected);
    }

    [Fact]
    public async Task Month_bar_lists_goal_percents_of_that_week()
    {
        var goal = await CreateDaily("Mesa");
        await _checkins.AddAsync(new Checkin
        {
            Id = Guid.NewGuid(), GoalId = goal.Id, Amount = 1, UpdatedAt = Now,
            DoneAt = new DateTimeOffset(2026, 11, 3, 13, 0, 0, TimeSpan.Zero),
        });
        await _goals.UpdateAsync(goal with { CreatedAt = new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero) });
        var page = await Loaded();

        page.IsMonth = true;
        await page.LoadCommand.ExecuteAsync(null);

        page.SelectedBar.Should().BeSameAs(page.Bars[0]);
        page.SelectedBar!.DetailTitle.Should().Be("Semana de 2 nov");
        page.SelectedBar.Details.Select(d => (d.Title, d.Value, d.IsMet)).Should().Equal(("Mesa", "33%", false)); // seg não, ter sim, qua (hoje) pendente
        page.Bars[1].Details.Should().BeEmpty();
        page.Bars[1].EmptyDetail.Should().Be("Nenhuma meta nesta semana.");
    }

    [Fact]
    public async Task Previous_then_next_navigates_weeks()
    {
        await CreateDaily();
        var page = await Loaded();

        await page.PreviousCommand.ExecuteAsync(null);
        page.PeriodTitle.Should().Be("26 out a 1 nov 2026");
        page.NextCommand.CanExecute(null).Should().BeTrue();

        await page.NextCommand.ExecuteAsync(null);
        page.PeriodTitle.Should().Be("2 a 8 nov 2026");
        page.NextCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Month_mode_shows_week_bars_streak_and_subtitle()
    {
        await CreateDaily();
        var page = await Loaded();

        page.IsMonth = true;
        await page.LoadCommand.ExecuteAsync(null);

        page.Mode.Should().Be(ReportMode.Month);
        page.IsWeek.Should().BeFalse();
        page.PeriodTitle.Should().Be("Novembro de 2026");
        page.PeriodSubtitle.Should().Be("Semanas de 2 nov a 6 dez");
        page.Bars.Count.Should().Be(5);
        page.Bars[0].Label.Should().Be("2 nov");
        page.Rows[0].Extra.Should().StartWith("Sequência: ");
        page.RemindersText.Should().BeNull();
        page.BarsTitle.Should().Be("Evolução por semana");
    }

    [Fact]
    public async Task Previous_month_from_november_is_october()
    {
        await CreateDaily();
        var page = await Loaded();
        page.IsMonth = true;

        await page.PreviousCommand.ExecuteAsync(null);

        page.PeriodTitle.Should().Be("Outubro de 2026");
    }

    [Fact]
    public async Task Archived_goal_row_is_flagged()
    {
        var goal = await CreateDaily();
        await GoalService.ArchiveAsync(goal.Id);

        var page = await Loaded();

        page.Rows.Single().IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_period_shows_message()
    {
        var page = await Loaded();

        page.IsEmpty.Should().BeTrue();
        page.EmptyMessage.Should().Be("Nenhuma meta neste período.");
    }

    [Fact]
    public async Task Reloads_on_data_change()
    {
        var goal = await CreateDaily();
        var page = await Loaded();
        page.Rows[0].Detail.Should().Be("0 de 1 dia"); // criada hoje: hoje pendente

        await CheckinService.AdjustAsync(goal.Id, 1);

        page.Rows[0].Detail.Should().Be("1 de 1 dia");
    }

    [Fact]
    public async Task Load_failure_sets_error_message()
    {
        var page = NewPage(new ThrowingGoalRepository(), _checkins, _log, _time, _changes);

        await page.LoadCommand.ExecuteAsync(null);

        page.ErrorMessage.Should().Be("Não foi possível carregar o relatório.");
    }

    private sealed class ThrowingGoalRepository : IGoalRepository
    {
        public Task AddAsync(Goal goal, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task UpdateAsync(Goal goal, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<Goal?> GetAsync(Guid id, CancellationToken ct = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default) =>
            throw new InvalidOperationException();
    }
}
