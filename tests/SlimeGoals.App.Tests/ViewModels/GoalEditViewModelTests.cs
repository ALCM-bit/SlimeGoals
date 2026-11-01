using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class GoalEditViewModelTests
{
    // 02/11/2026, fuso UTC (padrão do FakeTimeProvider).
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryGoalRepository _repo = new();
    private readonly List<bool> _closed = [];
    private GoalService Service => new(_repo, _time);

    private GoalEditViewModel New() => new(Service, null, _closed.Add);
    private GoalEditViewModel Edit(Goal goal) => new(Service, goal, _closed.Add);

    private Task<Goal> Existing(Func<GoalDraft, GoalDraft>? change = null) => Service.CreateAsync((change ?? (d => d))(
        new GoalDraft("Estudar inglês", Recurrence.Weekly, 5, "book", GoalColor.Pink, 45,
            new TimeOnly(9, 0), new TimeOnly(18, 0), WeekDays.Weekdays, new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 20))));

    [Fact]
    public void New_goal_starts_with_defaults()
    {
        var vm = New();

        vm.IsNew.Should().BeTrue();
        vm.PageTitle.Should().Be("Nova meta");
        vm.Title.Should().BeEmpty();
        vm.Recurrence.Should().Be(Recurrence.Daily);
        vm.Target.Should().Be(1);
        vm.SelectedIcon.Key.Should().Be("target");
        vm.SelectedColor.Color.Should().Be(GoalColor.Purple);
        vm.SelectedInterval.Minutes.Should().Be(60);
        vm.ActiveFrom.Should().Be(new TimeSpan(8, 0, 0));
        vm.ActiveUntil.Should().Be(new TimeSpan(20, 0, 0));
        vm.Days.Should().OnlyContain(d => d.IsSelected);
        vm.StartDate.Should().BeNull();
        vm.EndDate.Should().BeNull();
        vm.DeleteCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Editing_loads_all_fields_from_goal()
    {
        var goal = await Existing();
        var vm = Edit(goal);

        vm.IsNew.Should().BeFalse();
        vm.PageTitle.Should().Be("Editar meta");
        vm.Title.Should().Be("Estudar inglês");
        vm.Recurrence.Should().Be(Recurrence.Weekly);
        vm.Target.Should().Be(5);
        vm.SelectedIcon.Key.Should().Be("book");
        vm.SelectedColor.Color.Should().Be(GoalColor.Pink);
        vm.ActiveFrom.Should().Be(new TimeSpan(9, 0, 0));
        vm.ActiveUntil.Should().Be(new TimeSpan(18, 0, 0));
        vm.Days.Where(d => d.IsSelected).Select(d => d.Day).Should().Equal(
            WeekDays.Monday, WeekDays.Tuesday, WeekDays.Wednesday, WeekDays.Thursday, WeekDays.Friday);
        vm.StartDate.Should().Be(new DateTime(2026, 11, 5));
        vm.EndDate.Should().Be(new DateTime(2026, 12, 20));
        // 45 min não está na lista padrão: entra na ordem, entre 30 e 60, e fica selecionado.
        vm.Intervals.Select(i => i.Minutes).Should().ContainInOrder(30, 45, 60);
        vm.SelectedInterval.Minutes.Should().Be(45);
    }

    [Fact]
    public async Task Unknown_icon_key_selects_target()
    {
        var goal = await Existing();
        Edit(goal with { Icon = "removido" }).SelectedIcon.Key.Should().Be("target");
    }

    [Fact]
    public void Switching_to_once_forces_target_1_and_hides_it_then_restores_previous_target()
    {
        var vm = New();
        vm.Target = 5;

        vm.Recurrence = Recurrence.Once;
        vm.Target.Should().Be(1);
        vm.ShowsTarget.Should().BeFalse();

        vm.Recurrence = Recurrence.Weekly;
        vm.Target.Should().Be(5);
        vm.ShowsTarget.Should().BeTrue();
    }

    [Fact]
    public void Target_suffix_follows_recurrence()
    {
        var vm = New();
        vm.TargetSuffix.Should().Be("por dia");
        vm.IsWeekly = true;
        vm.Recurrence.Should().Be(Recurrence.Weekly);
        vm.TargetSuffix.Should().Be("por semana");
    }

    [Fact]
    public async Task Errors_appear_only_after_first_save_attempt_then_update_live()
    {
        var vm = New();
        vm.TitleError.Should().BeNull();

        await vm.SaveCommand.ExecuteAsync(null);
        vm.TitleError.Should().Be("Informe um título.");
        _closed.Should().BeEmpty();
        _repo.Count.Should().Be(0);

        vm.Title = "Beber água";
        vm.TitleError.Should().BeNull();
    }

    [Fact]
    public async Task Cleared_time_reports_informe_o_horario()
    {
        var vm = New();
        vm.Title = "Beber água";
        vm.ActiveFrom = null;

        await vm.SaveCommand.ExecuteAsync(null);

        vm.ActiveFromError.Should().Be("Informe o horário.");
        vm.ActiveUntilError.Should().BeNull();
        _repo.Count.Should().Be(0);
    }

    [Fact]
    public async Task Save_new_creates_goal_and_closes_with_true()
    {
        var vm = New();
        vm.Title = "  Beber água ";
        vm.SelectedIcon = vm.Icons.Single(i => i.Key == "water");
        vm.SelectedColor = vm.Colors.Single(c => c.Color == GoalColor.Green);
        vm.Target = 8;

        await vm.SaveCommand.ExecuteAsync(null);

        _closed.Should().Equal(true);
        var saved = (await _repo.ListAsync(includeArchived: false)).Single();
        saved.Title.Should().Be("Beber água");
        saved.Icon.Should().Be("water");
        saved.Color.Should().Be(GoalColor.Green);
        saved.Target.Should().Be(8);
        saved.ActiveFrom.Should().Be(new TimeOnly(8, 0));
        saved.ActiveDays.Should().Be(WeekDays.All);
    }

    [Fact]
    public async Task Save_existing_updates_goal_and_closes_with_true()
    {
        var goal = await Existing();
        var vm = Edit(goal);
        vm.Title = "Inglês";
        vm.EndDate = null;

        await vm.SaveCommand.ExecuteAsync(null);

        _closed.Should().Equal(true);
        var saved = await _repo.GetAsync(goal.Id);
        saved!.Title.Should().Be("Inglês");
        saved.EndDate.Should().BeNull();
        saved.ReminderEveryMinutes.Should().Be(45);
    }

    [Fact]
    public async Task Past_end_date_from_service_shows_on_end_date_field()
    {
        var vm = New();
        vm.Title = "Beber água";
        vm.EndDate = new DateTime(2026, 11, 1);

        await vm.SaveCommand.ExecuteAsync(null);

        vm.EndDateError.Should().Be("A data de fim não pode estar no passado.");
        _closed.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_requires_confirmation_then_deletes_and_closes()
    {
        var goal = await Existing();
        var vm = Edit(goal);

        vm.DeleteCommand.Execute(null);
        vm.IsConfirmingDelete.Should().BeTrue();
        (await _repo.GetAsync(goal.Id)).Should().NotBeNull();

        vm.CancelDeleteCommand.Execute(null);
        vm.IsConfirmingDelete.Should().BeFalse();

        vm.DeleteCommand.Execute(null);
        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        (await _repo.GetAsync(goal.Id)).Should().BeNull();
        _closed.Should().Equal(true);
    }

    [Fact]
    public void Cancel_closes_with_false_and_persists_nothing()
    {
        var vm = New();
        vm.Title = "Algo";
        vm.CancelCommand.Execute(null);

        _closed.Should().Equal(false);
        _repo.Count.Should().Be(0);
    }

    [Fact]
    public async Task Selected_days_map_to_WeekDays_and_none_selected_reports_error()
    {
        var vm = New();
        vm.Title = "Correr";
        foreach (var d in vm.Days) d.IsSelected = false;

        await vm.SaveCommand.ExecuteAsync(null);
        vm.ActiveDaysError.Should().Be("Escolha ao menos um dia.");

        vm.Days.Single(d => d.Day == WeekDays.Saturday).IsSelected = true;
        vm.ActiveDaysError.Should().BeNull();
        vm.Days.Single(d => d.Day == WeekDays.Sunday).IsSelected = true;

        await vm.SaveCommand.ExecuteAsync(null);
        (await _repo.ListAsync(includeArchived: false)).Single().ActiveDays
            .Should().Be(WeekDays.Saturday | WeekDays.Sunday);
    }

    // Revisão final #2: NumericUpDown devolve null quando o campo é apagado.
    [Fact]
    public async Task Cleared_target_reports_error_and_saves_nothing()
    {
        var vm = New();
        vm.Title = "Ler";
        vm.Target = 5;

        vm.TargetInput = null;
        await vm.SaveCommand.ExecuteAsync(null);

        vm.TargetError.Should().Be("Informe o alvo.");
        _repo.Count.Should().Be(0);
        _closed.Should().BeEmpty();

        vm.TargetInput = 3;
        vm.TargetError.Should().BeNull();
        vm.Target.Should().Be(3);
    }

    [Fact]
    public void Target_input_follows_target_changes()
    {
        var vm = New();
        vm.Target = 4;
        vm.TargetInput.Should().Be(4);
    }

    // Revisão final #3: erro inesperado do banco não pode derrubar o app.
    [Fact]
    public async Task Unexpected_error_on_save_shows_general_error()
    {
        var vm = New();
        vm.Title = "Ler";
        _repo.FailWritesWith = new IOException("disco cheio");

        await vm.SaveCommand.ExecuteAsync(null);

        vm.GeneralError.Should().Be("Não foi possível salvar a meta.");
        _closed.Should().BeEmpty();
    }

    [Fact]
    public async Task Unexpected_error_on_delete_shows_general_error()
    {
        var vm = Edit(await Existing());
        _repo.FailWritesWith = new IOException("disco cheio");

        vm.DeleteCommand.Execute(null);
        await vm.ConfirmDeleteCommand.ExecuteAsync(null);

        vm.GeneralError.Should().Be("Não foi possível excluir a meta.");
        _closed.Should().BeEmpty();
    }
}
