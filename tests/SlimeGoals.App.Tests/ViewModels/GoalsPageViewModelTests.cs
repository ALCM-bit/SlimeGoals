using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Hosting;
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
    private readonly InMemoryTagRepository _tagRepo = new();
    private readonly InMemoryGoalRepository _repo;
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly GoalService _service;
    private readonly TagService _tags;
    private readonly GoalsPageViewModel _vm;

    public GoalsPageViewModelTests()
    {
        _repo = new InMemoryGoalRepository(tags: _tagRepo);
        _service = new GoalService(_repo, _checkins, new InMemoryTimerRepository(), _time, tags: _tagRepo);
        _tags = new TagService(_tagRepo, _repo, _time);
        _vm = new GoalsPageViewModel(_service, _time, _tags, new PageNavigator(), _changes);
    }

    private Task<Goal> Create(string title, Recurrence r = Recurrence.Daily, DateOnly? end = null, params string[] tags) =>
        _service.CreateAsync(new GoalDraft(title, r, 3, "book", GoalColor.Purple, 120,
            new TimeOnly(9, 0), new TimeOnly(18, 0), WeekDays.Weekdays, null, end) { Tags = tags });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().Should().BeTrue();
    }

    /// <summary>Abre o editor da única meta da lista, com os chips já lidos.</summary>
    private async Task<GoalEditViewModel> EditOnlyGoal()
    {
        await Load();
        await _vm.EditCommand.ExecuteAsync(_vm.Goals.Single());
        var editor = _vm.Editor!;
        await editor.Tags.ReloadCommand.ExecuteAsync(null);
        return editor;
    }

    private async Task ReturnToPage(GoalEditViewModel editor)
    {
        _vm.Activate();
        await WaitUntilAsync(() => editor.Tags.ReloadCommand.ExecutionTask is not { IsCompleted: false });
    }

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

        await _vm.EditCommand.ExecuteAsync(_vm.Goals.Single());

        _vm.Editor.Should().NotBeNull();
        _vm.Editor!.Title.Should().Be("Ativa");
        _vm.Editor.IsMeasureLocked.Should().BeFalse();
    }

    [Fact]
    public async Task Edit_locks_measure_when_goal_has_records()
    {
        var goal = await Create("Com registro");
        await new CheckinService(_repo, _checkins, _time).AdjustAsync(goal.Id, 1);
        await Load();

        await _vm.EditCommand.ExecuteAsync(_vm.Goals.Single());

        _vm.Editor!.IsMeasureLocked.Should().BeTrue();
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
    public async Task Expired_goal_cannot_be_unarchived_and_says_why()
    {
        await Create("Expirada", end: Today);
        _time.Advance(TimeSpan.FromDays(1));
        await _service.ArchiveExpiredAsync();
        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;

        var item = _vm.Goals.Single();

        item.CanUnarchive.Should().BeFalse();
        item.Status.Should().EndWith(" · período encerrado");
    }

    [Fact]
    public async Task Archived_goal_ending_today_can_still_be_unarchived()
    {
        var goal = await Create("Termina hoje", end: Today);
        await _service.ArchiveAsync(goal.Id);
        _vm.ShowArchived = true;
        await _vm.LoadCommand.ExecutionTask!;

        var item = _vm.Goals.Single();

        item.CanUnarchive.Should().BeTrue();
        item.Status.Should().NotContain("período encerrado");
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

        new GoalItemViewModel(goal, zone, Today).Summary.Should().Be("3 vezes por semana · a cada 2 h · 09:00–18:00 · Seg a sex");
        new GoalItemViewModel(goal, zone, Today).Status.Should().BeNull();
        new GoalItemViewModel(goal with { ArchivedAt = at }, zone, Today).Status.Should().Be("Arquivada em 02/11");
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

    [Fact]
    public async Task Activate_reloads_tags_of_the_open_editor()
    {
        await _tags.CreateAsync("casa");
        _vm.NewCommand.Execute(null);
        await _vm.Editor!.Tags.ReloadCommand.ExecuteAsync(null);
        _vm.Editor.Tags.Chips.Select(c => c.Name).Should().Equal("casa");

        await _tags.CreateAsync("estudos");
        _vm.Activate();

        await WaitUntilAsync(() => _vm.Editor.Tags.Chips.Select(c => c.Name).SequenceEqual(["casa", "estudos"]));
    }

    [Fact]
    public async Task Renaming_the_selected_tag_in_settings_keeps_it_on_the_goal_after_save()
    {
        var goal = await Create("Ler", tags: "saúde");
        var editor = await EditOnlyGoal();

        await _tags.RenameAsync("saúde", "bem-estar");
        await ReturnToPage(editor);

        editor.Tags.SelectedNames.Should().Equal("bem-estar");
        await editor.SaveCommand.ExecuteAsync(null);
        (await _service.GetAsync(goal.Id))!.Tags.Should().Equal("bem-estar");
    }

    [Fact]
    public async Task Merging_the_selected_tag_in_settings_keeps_the_merged_tag_on_the_goal_after_save()
    {
        var goal = await Create("Ler", tags: "saúde");
        await _tags.CreateAsync("estudos");
        var editor = await EditOnlyGoal();

        await _tags.RenameAsync("saúde", "estudos");
        await ReturnToPage(editor);

        editor.Tags.SelectedNames.Should().Equal("estudos");
        await editor.SaveCommand.ExecuteAsync(null);
        (await _service.GetAsync(goal.Id))!.Tags.Should().Equal("estudos");
    }

    [Fact]
    public async Task Rename_keeps_what_the_person_changed_in_the_editor()
    {
        var goal = await Create("Ler", tags: ["saúde", "casa"]);
        await _tags.CreateAsync("estudos");
        var editor = await EditOnlyGoal();
        editor.Tags.ToggleCommand.Execute(editor.Tags.Chips.Single(c => c.Name == "casa"));
        editor.Tags.ToggleCommand.Execute(editor.Tags.Chips.Single(c => c.Name == "estudos"));
        editor.Tags.NewName = "nova";
        editor.Tags.CreateCommand.Execute(null);

        await _tags.RenameAsync("saúde", "bem-estar");
        await ReturnToPage(editor);

        editor.Tags.SelectedNames.Should().Equal("bem-estar", "estudos", "nova");
        await editor.SaveCommand.ExecuteAsync(null);
        (await _service.GetAsync(goal.Id))!.Tags.Should().Equal("bem-estar", "estudos", "nova");
    }

    [Fact]
    public async Task Tag_deleted_by_a_sync_while_the_editor_is_open_stays_deleted_after_save()
    {
        var goal = await Create("Ler", tags: ["tarefas", "casa"]);
        var editor = await EditOnlyGoal();

        // A exclusão chega de outro aparelho: escrita direta no banco e o aviso do notificador.
        await _tags.DeleteAsync("tarefas");
        _changes.NotifyChanged();
        await WaitUntilAsync(() => !editor.Tags.SelectedNames.Contains("tarefas"));

        editor.Title = "Ler mais";
        await editor.SaveCommand.ExecuteAsync(null);
        (await _service.GetAsync(goal.Id))!.Tags.Should().Equal("casa");
        (await _tags.ListAsync()).Select(t => t.Name).Should().Equal("casa");
    }

    [Fact]
    public async Task Closed_editor_and_hidden_page_stop_listening_to_changes()
    {
        await Create("Ler", tags: "casa");
        var editor = await EditOnlyGoal();

        _vm.Deactivate();
        await _tags.CreateAsync("longe");
        _changes.NotifyChanged();
        editor.Tags.Chips.Select(c => c.Name).Should().Equal("casa");

        await ReturnToPage(editor);
        editor.Tags.Chips.Select(c => c.Name).Should().Equal("casa", "longe");

        editor.CancelCommand.Execute(null);
        await _tags.CreateAsync("depois");
        _changes.NotifyChanged();
        editor.Tags.Chips.Select(c => c.Name).Should().Equal("casa", "longe");
    }
}
