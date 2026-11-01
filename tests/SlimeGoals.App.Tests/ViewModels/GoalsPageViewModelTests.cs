using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class GoalsPageViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _repo = new();
    private readonly GoalService _service;
    private readonly GoalsPageViewModel _vm;

    public GoalsPageViewModelTests()
    {
        _service = new GoalService(_repo, _time);
        _vm = new GoalsPageViewModel(_service, _time);
    }

    private Task<Goal> Create(string title, Recurrence r = Recurrence.Daily, DateOnly? end = null) =>
        _service.CreateAsync(new GoalDraft(title, r, r == Recurrence.Once ? 1 : 3, "book", GoalColor.Purple, 120,
            new TimeOnly(9, 0), new TimeOnly(18, 0), WeekDays.Weekdays, null, end));

    private async Task Load() => await _vm.LoadCommand.ExecuteAsync(null);
    private IEnumerable<string> Titles => _vm.Goals.Select(g => g.Title);

    [Fact]
    public async Task Activate_archives_expired_goals_and_lists_active_ones()
    {
        await Create("Expira hoje", end: Today);
        await Create("Sem fim");
        _time.Advance(TimeSpan.FromDays(1));

        _vm.Activate();
        await _vm.LoadCommand.ExecutionTask!;

        Titles.Should().Equal("Sem fim");
        (await _repo.ListAsync(includeArchived: true)).Single(g => g.Title == "Expira hoje").ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ShowArchived_lists_only_archived_newest_first()
    {
        var first = await Create("Primeira");
        var second = await Create("Segunda");
        await Create("Ativa");
        await _service.ArchiveAsync(first.Id);
        _time.Advance(TimeSpan.FromHours(1));
        await _service.ArchiveAsync(second.Id);

        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;

        Titles.Should().Equal("Segunda", "Primeira");
        _vm.Goals.Should().OnlyContain(g => g.IsArchived);
    }

    [Fact]
    public async Task Edit_is_not_available_for_archived_items()
    {
        var goal = await Create("Arquivada");
        await _service.ArchiveAsync(goal.Id);
        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;

        _vm.EditCommand.CanExecute(_vm.Goals.Single()).Should().BeFalse();
    }

    [Fact]
    public async Task Edit_opens_editor_for_active_item()
    {
        await Create("Ativa");
        await Load();

        _vm.EditCommand.Execute(_vm.Goals.Single());

        _vm.Editor.Should().NotBeNull();
        _vm.Editor!.Title.Should().Be("Ativa");
    }

    [Fact]
    public async Task Archive_moves_item_out_of_active_list()
    {
        await Create("Vai arquivar");
        await Create("Fica");
        await Load();

        await _vm.ArchiveCommand.ExecuteAsync(_vm.Goals.First());

        Titles.Should().Equal("Fica");
    }

    [Fact]
    public async Task Unarchive_returns_item_to_active_list()
    {
        var goal = await Create("Volta");
        await _service.ArchiveAsync(goal.Id);
        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;

        await _vm.UnarchiveCommand.ExecuteAsync(_vm.Goals.Single());

        _vm.Goals.Should().BeEmpty();
        (await _service.ListAsync()).Select(g => g.Title).Should().Equal("Volta");
    }

    [Fact]
    public async Task Unarchive_of_completed_once_is_not_offered()
    {
        var goal = await Create("Tarefa", Recurrence.Once);
        await _repo.UpdateAsync(goal with { CompletedAt = T0, ArchivedAt = T0 });
        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;

        _vm.Goals.Single().CanUnarchive.Should().BeFalse();
    }

    [Fact]
    public async Task Unarchive_error_from_service_shows_ErrorMessage()
    {
        var goal = await Create("Expirada", end: Today);
        _time.Advance(TimeSpan.FromDays(1));
        await _service.ArchiveExpiredAsync();
        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;

        await _vm.UnarchiveCommand.ExecuteAsync(_vm.Goals.Single());

        _vm.ErrorMessage.Should().Be("O período desta meta terminou em 02/11/2026. Crie uma nova meta para continuar.");
        (await _repo.GetAsync(goal.Id))!.ArchivedAt.Should().NotBeNull();

        await _vm.LoadCommand.ExecuteAsync(null);
        _vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task New_opens_editor_and_save_closes_it_and_reloads()
    {
        await Load();
        _vm.NewCommand.Execute(null);
        _vm.Editor.Should().NotBeNull();
        _vm.Editor!.IsNew.Should().BeTrue();

        _vm.Editor.Title = "Nova";
        await _vm.Editor.SaveCommand.ExecuteAsync(null);
        await _vm.LoadCommand.ExecutionTask!;

        _vm.Editor.Should().BeNull();
        Titles.Should().Equal("Nova");
    }

    [Fact]
    public async Task Cancel_closes_editor_without_changes()
    {
        await Load();
        _vm.NewCommand.Execute(null);
        _vm.Editor!.CancelCommand.Execute(null);

        _vm.Editor.Should().BeNull();
        _vm.Goals.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_state_message_depends_on_filter()
    {
        await Load();
        _vm.IsEmpty.Should().BeTrue();
        _vm.EmptyMessage.Should().Be("Nenhuma meta ativa. Crie a primeira!");

        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;
        _vm.EmptyMessage.Should().Be("Nenhuma meta arquivada.");

        _vm.ShowArchived = false;
        await _vm.LoadCommand.ExecutionTask!;
        await Create("Uma");
        await Load();
        _vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Item_summary_and_status_texts()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var goal = await Create("Ler", Recurrence.Weekly);
        // 03/11 01:00 UTC = 02/11 22:00 em São Paulo: a data mostrada é a local.
        var at = new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.Zero);

        new GoalItemViewModel(goal, zone).Summary.Should().Be("3× por semana · a cada 2 h · 09:00–18:00 · Seg a sex");
        new GoalItemViewModel(goal, zone).Status.Should().BeNull();
        new GoalItemViewModel(goal with { ArchivedAt = at }, zone).Status.Should().Be("Arquivada em 02/11");
        new GoalItemViewModel(goal with { Recurrence = Recurrence.Once, Target = 1, CompletedAt = at, ArchivedAt = at }, zone)
            .Status.Should().Be("Concluída em 02/11");
    }

    [Fact]
    public async Task Archive_of_goal_deleted_elsewhere_shows_message_and_reloads()
    {
        var goal = await Create("Some");
        await Load();
        await _service.DeleteAsync(goal.Id);

        await _vm.ArchiveCommand.ExecuteAsync(_vm.Goals.Single());

        _vm.ErrorMessage.Should().Be("Esta meta não existe mais.");
        _vm.Goals.Should().BeEmpty();
    }

    // Revisão final #1: arquivada só pode ser desarquivada ou excluída (spec §15).
    [Fact]
    public async Task Archived_goal_can_be_deleted_after_confirmation()
    {
        var goal = await Create("Vencida", end: Today);
        _time.Advance(TimeSpan.FromDays(1));
        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;
        var item = _vm.Goals.Single();

        _vm.RequestDeleteCommand.Execute(item);
        item.IsConfirmingDelete.Should().BeTrue();
        _vm.CancelDeleteCommand.Execute(item);
        item.IsConfirmingDelete.Should().BeFalse();

        _vm.RequestDeleteCommand.Execute(item);
        await _vm.ConfirmDeleteCommand.ExecuteAsync(item);

        _vm.Goals.Should().BeEmpty();
        (await _service.GetAsync(goal.Id)).Should().BeNull();
    }

    // Revisão final #3
    [Fact]
    public async Task Unexpected_error_on_action_shows_message_instead_of_crashing()
    {
        await Create("Ativa");
        await Load();
        _repo.FailWritesWith = new IOException("banco travado");

        await _vm.ArchiveCommand.ExecuteAsync(_vm.Goals.Single());

        _vm.ErrorMessage.Should().Be("Não foi possível concluir a ação.");
    }
}
