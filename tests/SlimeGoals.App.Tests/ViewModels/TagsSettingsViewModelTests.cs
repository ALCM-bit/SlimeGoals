using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class TagsSettingsViewModelTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryTagRepository _tagRepo = new();
    private readonly InMemoryGoalRepository _goalRepo;
    private readonly TagService _tags;
    private readonly GoalService _goals;

    public TagsSettingsViewModelTests()
    {
        _goalRepo = new InMemoryGoalRepository(tags: _tagRepo);
        _tags = new TagService(_tagRepo, _goalRepo, _time);
        _goals = new GoalService(_goalRepo, new InMemoryCheckinRepository(), new InMemoryTimerRepository(), _time, tags: _tagRepo);
    }

    private async Task<Goal> GoalWith(params string[] tags) =>
        await _goals.CreateAsync(new GoalDraft("Meta", Recurrence.Daily, 3, "book", GoalColor.Purple, 120,
            new TimeOnly(9, 0), new TimeOnly(18, 0), WeekDays.Weekdays, null, null) with
        { Tags = tags });

    private async Task<TagsSettingsViewModel> Loaded()
    {
        var vm = new TagsSettingsViewModel(_tags);
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Usage_texts_match_spec()
    {
        await _tags.CreateAsync("a-nenhuma");
        await GoalWith("b-uma");
        await GoalWith("c-duas");
        await GoalWith("c-duas");
        await GoalWith("d-arq", "e-arqs");
        await GoalWith("d-arq", "e-arqs");
        var archivedOne = await GoalWith("d-arq", "e-arqs");
        var archivedTwo = await GoalWith("e-arqs");
        await _goals.ArchiveAsync(archivedOne.Id);
        await _goals.ArchiveAsync(archivedTwo.Id);

        var vm = await Loaded();

        vm.Rows.Select(r => (r.Name, r.UsageText)).Should().Equal(
            ("a-nenhuma", "nenhuma meta"),
            ("b-uma", "usada em 1 meta"),
            ("c-duas", "usada em 2 metas"),
            ("d-arq", "usada em 3 metas (1 arquivada)"),
            ("e-arqs", "usada em 4 metas (2 arquivadas)"));
    }

    [Fact]
    public async Task Row_shows_the_name_capitalized_and_keeps_the_stored_one()
    {
        await _tags.CreateAsync("saúde");
        var vm = await Loaded();

        (vm.Rows.Single().Name, vm.Rows.Single().DisplayName).Should().Be(("saúde", "Saúde"));
    }

    [Fact]
    public async Task Delete_question_counts_goals()
    {
        await _tags.CreateAsync("vazia");
        await GoalWith("uma");
        await GoalWith("duas");
        var archived = await GoalWith("duas");
        await _goals.ArchiveAsync(archived.Id);
        var vm = await Loaded();

        var rows = vm.Rows.ToDictionary(r => r.Name);
        foreach (var row in rows.Values)
            row.BeginDeleteCommand.Execute(null);

        rows["vazia"].DeleteQuestion.Should().Be("Excluir a tag \"Vazia\"?");
        rows["uma"].DeleteQuestion.Should().Be("Excluir a tag \"Uma\"? Ela sai de 1 meta.");
        rows["duas"].DeleteQuestion.Should().Be("Excluir a tag \"Duas\"? Ela sai de 2 metas.");
        rows["uma"].IsConfirmingDelete.Should().BeTrue();

        rows["uma"].CancelDeleteCommand.Execute(null);
        rows["uma"].IsConfirmingDelete.Should().BeFalse();
        vm.Rows.Should().HaveCount(3);

        await rows["duas"].ConfirmDeleteCommand.ExecuteAsync(null);
        vm.Rows.Select(r => r.Name).Should().Equal("uma", "vazia");
    }

    [Fact]
    public async Task Rename_into_existing_asks_merge_then_merges()
    {
        var goal = await GoalWith("velha");
        await _tags.CreateAsync("nova");
        var vm = await Loaded();
        var row = vm.Rows.Single(r => r.Name == "velha");

        row.BeginEditCommand.Execute(null);
        row.EditText = "  NOVA ";
        await row.ConfirmEditCommand.ExecuteAsync(null);

        (row.IsConfirmingMerge, row.IsEditing).Should().Be((true, false));
        row.MergeQuestion.Should().Be("Já existe a tag \"Nova\". Juntar as duas?");
        (await _goalRepo.GetAsync(goal.Id))!.Tags.Should().Equal("velha");

        await row.ConfirmMergeCommand.ExecuteAsync(null);

        vm.Rows.Select(r => (r.Name, r.UsageText)).Should().Equal(("nova", "usada em 1 meta"));
        (await _goalRepo.GetAsync(goal.Id))!.Tags.Should().Equal("nova");
    }

    [Fact]
    public async Task Rename_to_a_free_name_renames_without_asking()
    {
        await GoalWith("velha");
        var vm = await Loaded();
        var row = vm.Rows.Single();

        row.BeginEditCommand.Execute(null);
        row.EditText.Should().Be("Velha"); // como aparece; o nome gravado continua em minúsculas
        row.EditText = "Nova";
        await row.ConfirmEditCommand.ExecuteAsync(null);

        vm.Rows.Select(r => (r.Name, r.UsageText)).Should().Equal(("nova", "usada em 1 meta"));
    }

    [Fact]
    public async Task Cancelling_the_merge_returns_to_editing()
    {
        await _tags.CreateAsync("a");
        await _tags.CreateAsync("b");
        var vm = await Loaded();
        var row = vm.Rows.First();

        row.BeginEditCommand.Execute(null);
        row.EditText = "b";
        await row.ConfirmEditCommand.ExecuteAsync(null);
        row.CancelMergeCommand.Execute(null);

        (row.IsConfirmingMerge, row.IsEditing, row.EditText).Should().Be((false, true, "b"));
        row.CancelEditCommand.Execute(null);
        row.IsEditing.Should().BeFalse();
        vm.Rows.Select(r => r.Name).Should().Equal("a", "b");
    }

    [Fact]
    public async Task Rename_invalid_shows_error_and_keeps_editing()
    {
        await _tags.CreateAsync("casa");
        var vm = await Loaded();
        var row = vm.Rows.Single();

        row.BeginEditCommand.Execute(null);
        row.EditText = "   ";
        await row.ConfirmEditCommand.ExecuteAsync(null);
        (row.IsEditing, row.EditError).Should().Be((true, "Dê um nome à tag."));

        row.EditText = new string('x', 25);
        row.EditError.Should().BeNull();
        await row.ConfirmEditCommand.ExecuteAsync(null);
        (row.IsEditing, row.EditError).Should().Be((true, "A tag pode ter até 24 caracteres."));
        row.Name.Should().Be("casa");
    }

    [Fact]
    public async Task Color_pick_saves_immediately()
    {
        await _tags.CreateAsync("casa");
        var vm = await Loaded();
        var row = vm.Rows.Single();

        row.TogglePickerCommand.Execute(null);
        row.IsPickingColor.Should().BeTrue();
        await row.PickColorCommand.ExecuteAsync(GoalColor.Green);

        (await _tags.ListAsync()).Single().Color.Should().Be(GoalColor.Green);
        (vm.Rows.Single().Color, vm.Rows.Single().IsPickingColor).Should().Be((GoalColor.Green, false));
    }

    [Fact]
    public async Task Empty_list_message()
    {
        var vm = await Loaded();

        (vm.IsEmpty, vm.EmptyMessage).Should().Be((true, "Nenhuma tag. Crie uma aqui ou na página de uma meta."));

        vm.BeginCreateCommand.Execute(null);
        vm.NewName = "casa";
        await vm.ConfirmCreateCommand.ExecuteAsync(null);

        (vm.IsEmpty, vm.IsCreating).Should().Be((false, false));
        vm.Rows.Select(r => r.Name).Should().Equal("casa");
    }

    [Fact]
    public async Task Create_validates_and_can_be_cancelled()
    {
        var vm = await Loaded();

        vm.BeginCreateCommand.Execute(null);
        vm.NewName = " ";
        await vm.ConfirmCreateCommand.ExecuteAsync(null);
        (vm.IsCreating, vm.NewNameError).Should().Be((true, "Dê um nome à tag."));

        vm.NewName = "x";
        vm.NewNameError.Should().BeNull();
        vm.CancelCreateCommand.Execute(null);
        vm.IsCreating.Should().BeFalse();
        vm.NewName.Should().BeEmpty();
        vm.NewNameError.Should().BeNull();
        vm.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task While_watching_a_change_from_outside_reloads_the_list()
    {
        var changes = new DataChangeNotifier();
        var vm = new TagsSettingsViewModel(_tags, changes);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.StartWatching();

        await _tags.CreateAsync("chegou"); // a tag chega por uma sincronização
        changes.NotifyChanged();
        vm.Rows.Select(r => r.Name).Should().Equal("chegou");

        vm.StopWatching();
        await _tags.CreateAsync("depois");
        changes.NotifyChanged();
        vm.Rows.Select(r => r.Name).Should().Equal("chegou");
    }

    [Fact]
    public async Task Reload_keeps_the_open_state_of_a_row_that_is_still_there()
    {
        await _tags.CreateAsync("casa");
        var vm = await Loaded();
        var row = vm.Rows.Single();
        row.BeginEditCommand.Execute(null);
        row.EditText = "cas";

        await _tags.CreateAsync("outra");
        await vm.LoadCommand.ExecuteAsync(null);

        vm.Rows.Single(r => r.Name == "casa").Should().Match<TagRowViewModel>(r => r.IsEditing && r.EditText == "cas" && !r.IsIdle);
        vm.Rows.Single(r => r.Name == "outra").IsIdle.Should().BeTrue();
    }
}

