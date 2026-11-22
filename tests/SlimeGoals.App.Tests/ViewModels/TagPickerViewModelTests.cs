using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class TagPickerViewModelTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryTagRepository _repo = new();
    private readonly TagService _service;

    public TagPickerViewModelTests() =>
        _service = new TagService(_repo, new InMemoryGoalRepository(tags: _repo), _time);

    private async Task<TagPickerViewModel> Loaded(params string[] initial)
    {
        var vm = new TagPickerViewModel(_service, initial);
        await vm.ReloadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Chips_are_visible_tags_plus_selected_not_yet_arrived_in_purple()
    {
        await _service.CreateAsync("estudos");
        await _service.ChangeColorAsync("estudos", GoalColor.Green);
        await _service.CreateAsync("casa");
        await _service.ChangeColorAsync("casa", GoalColor.Pink);

        var vm = await Loaded("estudos", "chegando");

        vm.Chips.Select(c => (c.Name, c.Color, c.IsSelected, c.Count)).Should().Equal(
            ("casa", GoalColor.Pink, false, (int?)null),
            ("estudos", GoalColor.Green, true, (int?)null),
            ("chegando", GoalColor.Purple, true, (int?)null));
        vm.SelectedNames.Should().Equal("estudos", "chegando");
    }

    [Fact]
    public async Task Toggle_selects_and_unselects()
    {
        await _service.CreateAsync("casa");
        var vm = await Loaded();

        vm.ToggleCommand.Execute(vm.Chips.Single());
        vm.SelectedNames.Should().Equal("casa");
        vm.ToggleCommand.Execute(vm.Chips.Single());
        vm.SelectedNames.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_adds_selected_chip_without_writing_to_repository()
    {
        var vm = await Loaded();
        var writes = _repo.Writes;

        vm.NewName = "  Saúde ";
        vm.CreateCommand.Execute(null);

        vm.Chips.Select(c => (c.Name, c.IsSelected, c.Color)).Should().Equal(("saúde", true, GoalColor.Purple));
        vm.SelectedNames.Should().Equal("saúde");
        (vm.NewName, vm.NewNameError).Should().Be(("", (string?)null));
        _repo.Writes.Should().Be(writes);
        _repo.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_existing_name_selects_it()
    {
        await _service.CreateAsync("casa");
        var vm = await Loaded();

        vm.NewName = " CASA";
        vm.CreateCommand.Execute(null);

        vm.Chips.Should().ContainSingle().Which.IsSelected.Should().BeTrue();
        vm.NewName.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_invalid_shows_error()
    {
        var vm = await Loaded();

        vm.NewName = "   ";
        vm.CreateCommand.Execute(null);
        vm.NewNameError.Should().Be("Dê um nome à tag.");

        vm.NewName = new string('a', 25);
        vm.CreateCommand.Execute(null);
        vm.NewNameError.Should().Be("A tag pode ter até 24 caracteres.");
        vm.Chips.Should().BeEmpty();

        vm.NewName = "ok";
        vm.NewNameError.Should().BeNull("digitar limpa o erro");
    }

    [Fact]
    public async Task Reload_keeps_selection_and_drops_deleted_selected_tag()
    {
        await _service.CreateAsync("casa");
        await _service.CreateAsync("estudos");
        var vm = await Loaded("casa", "estudos", "chegando");
        vm.NewName = "novinha";
        vm.CreateCommand.Execute(null);

        await _service.DeleteAsync("estudos");
        await _service.ChangeColorAsync("casa", GoalColor.Amber);
        await vm.ReloadCommand.ExecuteAsync(null);

        vm.SelectedNames.Should().Equal("casa", "chegando", "novinha");
        vm.Chips.First(c => c.Name == "casa").Color.Should().Be(GoalColor.Amber);
        vm.Chips.Select(c => c.Name).Should().NotContain("estudos");
    }

    [Fact]
    public async Task Reload_rereads_the_goal_tags_and_keeps_what_the_person_changed()
    {
        foreach (var name in new[] { "casa", "estudos", "saúde", "x" })
            await _service.CreateAsync(name);
        IReadOnlyList<string> stored = ["casa", "saúde", "x"];
        var vm = new TagPickerViewModel(_service, stored, _ => Task.FromResult<IReadOnlyList<string>?>(stored));
        await vm.ReloadCommand.ExecuteAsync(null);
        vm.ToggleCommand.Execute(vm.Chips.Single(c => c.Name == "casa"));    // desmarcada
        vm.ToggleCommand.Execute(vm.Chips.Single(c => c.Name == "estudos")); // marcada
        vm.NewName = "nova";
        vm.CreateCommand.Execute(null);                                       // digitada, ainda não é tag

        // Em Configurações: "saúde" vira "bem-estar" (a meta acompanha) e "estudos" é excluída.
        await _service.RenameAsync("saúde", "bem-estar");
        await _service.DeleteAsync("estudos");
        stored = ["bem-estar", "casa", "x"];
        await vm.ReloadCommand.ExecuteAsync(null);

        vm.SelectedNames.Should().Equal("bem-estar", "x", "nova");
        vm.Chips.Select(c => c.Name).Should().Equal("bem-estar", "casa", "x", "nova");
    }

    [Fact]
    public async Task Goal_gone_from_the_database_keeps_the_previous_base()
    {
        await _service.CreateAsync("casa");
        var vm = new TagPickerViewModel(_service, ["casa"], _ => Task.FromResult<IReadOnlyList<string>?>(null));

        await vm.ReloadCommand.ExecuteAsync(null);

        vm.SelectedNames.Should().Equal("casa");
    }

    [Fact]
    public async Task Toggling_a_tag_off_and_on_again_keeps_it_selected_after_reload()
    {
        await _service.CreateAsync("casa");
        var vm = new TagPickerViewModel(_service, [], _ => Task.FromResult<IReadOnlyList<string>?>([]));
        await vm.ReloadCommand.ExecuteAsync(null);

        vm.ToggleCommand.Execute(vm.Chips.Single());
        vm.ToggleCommand.Execute(vm.Chips.Single());
        vm.ToggleCommand.Execute(vm.Chips.Single());
        await vm.ReloadCommand.ExecuteAsync(null);

        vm.SelectedNames.Should().Equal("casa");
    }
}
