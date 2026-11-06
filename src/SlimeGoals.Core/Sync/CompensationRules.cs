namespace SlimeGoals.Core.Sync;

/// <summary>Uma compensação da mesma meta e período, como a regra a enxerga.</summary>
public sealed record CompensationEntry(Guid Id, DateTimeOffset DoneAt, IReadOnlyCollection<Guid> Seen);

/// <summary>
/// Decide quais compensações concorrentes deixam de contar (spec de sincronização §3). Da mais nova para a mais antiga,
/// uma entrada conta se, para cada uma que já conta, uma enxergou a outra. O resultado não depende da ordem de entrada.
/// </summary>
public static class CompensationRules
{
    public static IReadOnlySet<Guid> Superseded(IEnumerable<CompensationEntry> entries)
    {
        var ordered = entries
            .OrderByDescending(e => e.DoneAt)
            .ThenByDescending(e => e.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();
        var counted = new List<CompensationEntry>();
        var superseded = new HashSet<Guid>();
        foreach (var entry in ordered)
        {
            if (counted.All(c => c.Seen.Contains(entry.Id) || entry.Seen.Contains(c.Id)))
                counted.Add(entry);
            else
                superseded.Add(entry.Id);
        }
        return superseded;
    }
}
