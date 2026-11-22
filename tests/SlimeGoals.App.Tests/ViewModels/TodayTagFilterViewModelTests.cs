using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Tests.ViewModels;

public class TodayTagFilterViewModelTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero));
    private readonly TodayTagFilterViewModel _filter = new();

    private TodayGoalViewModel Card(string title, params string[] tags) => new(
        new GoalProgress(new Goal
        {
            Id = Guid.NewGuid(), Title = title, Recurrence = Recurrence.Daily, Target = 1, Icon = "water",
            Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0),
            ActiveUntil = new TimeOnly(20, 0), ActiveDays = WeekDays.All, CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(), Tags = tags,
        }, 0, false), null, _time, tagColors: new Dictionary<string, GoalColor> { ["estudos"] = GoalColor.Pink });

    private TodayTagFilterViewModel Refreshed(params TodayGoalViewModel[] goals)
    {
        _filter.Refresh(goals);
        return _filter;
    }

    private void Toggle(string name) => _filter.ToggleCommand.Execute(_filter.Chips.Single(c => c.Name == name));

    [Fact]
    public void Only_tags_of_today_goals_appear_ordered_by_count_then_name()
    {
        Refreshed(Card("a", "b", "estudos"), Card("b", "b", "c"), Card("c", "a", "b"), Card("d"));

        _filter.Chips.Select(c => (c.Name, c.Count)).Should().Equal(("b", 3), ("a", 1), ("c", 1), ("estudos", 1));
        _filter.Chips.Single(c => c.Name == "estudos").Color.Should().Be(GoalColor.Pink);
        _filter.Chips.Single(c => c.Name == "b").Color.Should().Be(GoalColor.Purple);
        _filter.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Selecting_two_tags_shows_goals_with_any_of_them()
    {
        var goals = new[] { Card("a", "x"), Card("b", "y"), Card("c", "x", "y"), Card("d", "z"), Card("e") };
        Refreshed(goals);

        Toggle("x");
        goals.Select(g => g.IsShown).Should().Equal(true, false, true, false, false);
        Toggle("y");
        goals.Select(g => g.IsShown).Should().Equal(true, true, true, false, false);
        Toggle("y");
        goals.Select(g => g.IsShown).Should().Equal(true, false, true, false, false);
        _filter.HasSelection.Should().BeTrue();
    }

    [Fact]
    public void Summary_is_x_of_y_only_with_selection()
    {
        var goals = Enumerable.Range(0, 9).Select(i => Card($"m{i}", i < 5 ? ["x"] : ["y"])).ToArray();
        Refreshed(goals);
        _filter.SummaryText.Should().BeNull();

        Toggle("x");

        _filter.SummaryText.Should().Be("5 de 9 metas");
    }

    [Fact]
    public void Clear_shows_all()
    {
        var goals = new[] { Card("a", "x"), Card("b", "y") };
        Refreshed(goals);
        Toggle("x");
        goals[1].IsShown.Should().BeFalse();

        _filter.ClearCommand.Execute(null);

        goals.Should().OnlyContain(g => g.IsShown);
        (_filter.HasSelection, _filter.SummaryText, _filter.IsFilteredEmpty).Should().Be((false, null, false));
        _filter.Chips.Should().OnlyContain(c => !c.IsSelected);
    }

    [Fact]
    public void Selected_tag_that_leaves_the_row_is_dropped_and_empty_selection_clears()
    {
        var finishing = Card("a", "x");
        var other = Card("b", "y");
        Refreshed(finishing, other);
        Toggle("x");
        Toggle("y");

        // A última meta de "x" saiu da lista: "x" sai da seleção e "y" continua.
        _filter.Refresh([other]);
        _filter.Chips.Select(c => (c.Name, c.IsSelected)).Should().Equal(("y", true));
        other.IsShown.Should().BeTrue();
        _filter.HasSelection.Should().BeTrue();

        // A de "y" também: a seleção esvazia, e o filtro se limpa.
        var third = Card("c", "z");
        _filter.Refresh([third]);
        (_filter.HasSelection, _filter.SummaryText, _filter.IsFilteredEmpty).Should().Be((false, null, false));
        third.IsShown.Should().BeTrue();
        _filter.Chips.Should().OnlyContain(c => !c.IsSelected);
    }

    [Fact]
    public void Selection_survives_a_refresh_while_the_tag_stays()
    {
        Refreshed(Card("a", "x"), Card("b", "y"));
        Toggle("x");

        var goals = new[] { Card("a", "x"), Card("b", "y") };
        _filter.Refresh(goals);

        _filter.Chips.Single(c => c.Name == "x").IsSelected.Should().BeTrue();
        goals.Select(g => g.IsShown).Should().Equal(true, false);
    }

    [Fact]
    public void Row_hidden_when_no_goal_has_tags()
    {
        Refreshed(Card("a"), Card("b"));

        (_filter.IsVisible, _filter.Chips).Should().BeEquivalentTo((false, Array.Empty<TagChipViewModel>()));
    }

    [Fact]
    public void Filtered_empty_message_text_and_defensive_flag_stay_off_while_the_selection_matches()
    {
        Refreshed(Card("a", "x"));
        _filter.IsFilteredEmpty.Should().BeFalse();
        TodayTagFilterViewModel.FilteredEmptyMessage.Should().Be("Nenhuma meta de hoje com essas tags.");
        // Só some a meta mostrada quando há seleção que não casa com nada: simulado por uma lista sem a tag marcada.
        var goals = new[] { Card("a", "x"), Card("b", "y") };
        _filter.Refresh(goals);
        Toggle("x");
        _filter.IsFilteredEmpty.Should().BeFalse();
    }
}
