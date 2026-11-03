namespace SlimeGoals.Core.Domain;

/// <summary>Sequência atual (no fim da lista) e melhor sequência de períodos cumpridos.</summary>
public readonly record struct Streak(int Current, int Best);

public static class Streaks
{
    /// <summary>Best = maior sequência de Met; Current = sequência de Met no fim da lista.</summary>
    public static Streak From(IReadOnlyList<PeriodResult> periods)
    {
        int run = 0, best = 0;
        foreach (var p in periods)
        {
            run = p.Met ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return new Streak(run, best);
    }
}
