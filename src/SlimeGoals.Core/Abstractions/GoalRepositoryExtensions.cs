using System.Diagnostics;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

internal static class GoalRepositoryExtensions
{
    /// <summary>
    /// Como <see cref="IGoalRepository.GetAsync"/>, mas a meta com linha inválida no banco vale como inexistente,
    /// igual às listas, que a pulam. Para quem chega à meta por uma referência (lembrete pendente, cronômetro)
    /// e não pode parar por causa dela.
    /// </summary>
    public static async Task<Goal?> GetIfReadableAsync(this IGoalRepository goals, Guid id, CancellationToken ct)
    {
        try
        {
            return await goals.GetAsync(id, ct);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            Trace.TraceError($"Meta {id} ilegível; tratada como inexistente: {TraceText.Types(e)}");
            return null;
        }
    }
}
