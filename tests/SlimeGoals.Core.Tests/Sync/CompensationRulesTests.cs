using SlimeGoals.Core.Sync;

namespace SlimeGoals.Core.Tests.Sync;

public class CompensationRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static CompensationEntry Entry(Guid id, int minutes, params Guid[] seen) =>
        new(id, T0.AddMinutes(minutes), seen);

    [Fact]
    public void Concurrent_pair_keeps_only_the_newest() =>
        CompensationRules.Superseded([Entry(A, 0), Entry(B, 1)]).Should().BeEquivalentTo([A]);

    [Fact]
    public void Three_concurrent_keep_only_the_newest() =>
        CompensationRules.Superseded([Entry(A, 0), Entry(B, 1), Entry(C, 2)]).Should().BeEquivalentTo([A, B]);

    [Fact]
    public void Seen_compensation_still_counts() =>
        CompensationRules.Superseded([Entry(A, 0), Entry(B, 1, A)]).Should().BeEmpty();

    [Fact]
    public void Partial_view_keeps_what_the_newest_saw() =>
        CompensationRules.Superseded([Entry(A, 0), Entry(B, 1), Entry(C, 2, A)]).Should().BeEquivalentTo([B]);

    [Fact]
    public void Later_one_that_saw_a_newer_clock_is_not_concurrent() =>
        CompensationRules.Superseded([Entry(A, 0, B), Entry(B, 5)]).Should().BeEmpty();

    [Fact]
    public void Same_instant_keeps_larger_id() =>
        CompensationRules.Superseded([Entry(A, 0), Entry(B, 0)]).Should().BeEquivalentTo([A]);

    [Fact]
    public void Result_does_not_depend_on_input_order()
    {
        var entries = new[] { Entry(A, 0), Entry(B, 1), Entry(C, 2, A) };
        foreach (var permutation in Permutations(entries))
            CompensationRules.Superseded(permutation).Should().BeEquivalentTo([B]);
    }

    private static IEnumerable<CompensationEntry[]> Permutations(CompensationEntry[] items)
    {
        foreach (var i in Enumerable.Range(0, 3))
            foreach (var j in Enumerable.Range(0, 3).Where(j => j != i))
                yield return [items[i], items[j], items[3 - i - j]];
    }
}
