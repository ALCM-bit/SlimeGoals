using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Domain;

public class FocusPlanTests
{
    private static FocusBlock F(int m) => new(FocusBlockKind.Focus, m);
    private static FocusBlock B(int m) => new(FocusBlockKind.Break, m);

    [Fact]
    public void Default_is_25_5_repeated_4_times()
    {
        var plan = FocusPlan.Default;

        plan.Blocks.Should().Equal(F(25), B(5));
        plan.Repeat.Should().Be(4);
        plan.TotalSteps.Should().Be(8);
        plan.TotalMinutes.Should().Be(120);
    }

    [Fact]
    public void Validate_reports_each_limit()
    {
        new FocusPlan([F(0)], 1).Validate().Should().ContainSingle(e => e.Field == "Blocks" && e.Message == "O bloco deve ter de 1 min a 4 h.");
        new FocusPlan([F(241)], 1).Validate().Should().ContainSingle(e => e.Field == "Blocks" && e.Message == "O bloco deve ter de 1 min a 4 h.");
        new FocusPlan(Enumerable.Repeat(F(1), 13).ToList(), 1).Validate()
            .Should().ContainSingle(e => e.Field == "Blocks" && e.Message == "A sequência pode ter até 12 blocos.");
        new FocusPlan([F(25)], 0).Validate().Should().ContainSingle(e => e.Field == "Repeat" && e.Message == "Repita de 1 a 10 vezes.");
        new FocusPlan([F(25)], 11).Validate().Should().ContainSingle(e => e.Field == "Repeat" && e.Message == "Repita de 1 a 10 vezes.");
        new FocusPlan([B(5), B(5)], 1).Validate().Should().ContainSingle(e => e.Field == "Blocks" && e.Message == "Inclua pelo menos um bloco de foco.");
        new FocusPlan(Enumerable.Repeat(F(240), 12).ToList(), 10).Validate().Should().BeEmpty();
    }

    [Fact]
    public void Author_example_round_trips_through_json()
    {
        var plan = new FocusPlan([F(40), B(20), F(60), B(30)], 1);

        var back = FocusPlan.FromJson(plan.ToJson());

        back.Should().Be(plan);
        plan.TotalMinutes.Should().Be(150);
        FocusPlan.FromJson("{").Should().BeNull();
    }

    [Fact]
    public void Round_and_focus_number_follow_the_list()
    {
        var plan = FocusPlan.Default;

        plan.RoundOf(2).Should().Be(2);
        plan.FocusNumberInRound(2).Should().Be(1);
        plan.FocusCountInRound.Should().Be(1);
    }
}
