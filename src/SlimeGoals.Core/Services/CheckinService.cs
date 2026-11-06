using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Registra ajustes de progresso (spec §5.2): valores com sinal; o total do período nunca fica negativo
/// e, na conclusão, nunca passa de 1. O ajuste parte do total limitado e grava a diferença até o total real,
/// então o total mostrado muda exatamente pelo valor pedido.
/// </summary>
public sealed class CheckinService(IGoalRepository goals, ICheckinRepository checkins, TimeProvider time,
    DataChangeNotifier? changes = null)
{
    public const int MaxNoteLength = 200;
    public const int MaxQuantityAmount = 9999;
    public const int MaxDurationAmount = 24 * 60;

    /// <summary>Registra um ajuste no período atual e devolve o novo total do período.</summary>
    public async Task<int> AdjustAsync(Guid goalId, int amount, string? note = null, CancellationToken ct = default)
    {
        var goal = await goals.GetAsync(goalId, ct) ?? throw new GoalNotFoundException(goalId);
        if (goal.ArchivedAt is not null)
            throw new GoalStateException(goalId, "Meta arquivada não recebe registros.");
        var today = Today();
        if (!GoalPeriod.IsShownOn(goal, today))
            throw new GoalStateException(goalId, "Esta meta não está ativa hoje.");

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var errors = new List<ValidationError>();
        if (amount == 0)
            errors.Add(new ValidationError("Amount", "Informe um valor diferente de zero."));
        else if (Math.Abs(amount) > MaxAmount(goal.Measure))
            errors.Add(new ValidationError("Amount", "Valor fora do limite."));
        if (note is { Length: > MaxNoteLength })
            errors.Add(new ValidationError("Note", $"A nota deve ter até {MaxNoteLength} caracteres."));
        if (errors.Count > 0)
            throw new GoalValidationException(errors);

        var raw = await TotalAsync(goal, today, ct);
        var total = GoalTotals.Effective(goal, raw) + amount;
        if (total < 0)
            throw new GoalStateException(goalId, "Não dá para remover mais do que o registrado no período.");
        if (goal.Measure == Measure.Completion && total > 1)
            throw new GoalStateException(goalId, "Meta já concluída neste período.");

        var now = time.GetUtcNow();
        await checkins.AddAsync(new Checkin
        {
            Id = Guid.CreateVersion7(now),
            GoalId = goalId,
            DoneAt = now,
            // Sem sincronização raw e o total limitado são iguais, e o valor gravado é `amount`.
            Amount = total - raw,
            Note = note,
            UpdatedAt = now,
        }, ct);
        changes?.NotifyChanged();
        return total;
    }

    /// <summary>Soma do período atual da meta (0 se a meta não existe ou está fora do período).</summary>
    public async Task<int> PeriodTotalAsync(Guid goalId, CancellationToken ct = default)
    {
        var goal = await goals.GetAsync(goalId, ct);
        return goal is null ? 0 : GoalTotals.Effective(goal, await TotalAsync(goal, Today(), ct));
    }

    private async Task<int> TotalAsync(Goal goal, DateOnly today, CancellationToken ct)
    {
        if (GoalPeriod.EvaluationPeriod(goal, today) is not { } period)
            return 0;
        var (from, to) = period.ToUtc(time.LocalTimeZone);
        return await checkins.SumAsync(goal.Id, from, to, ct);
    }

    private static int MaxAmount(Measure measure) => measure switch
    {
        Measure.Completion => 1,
        Measure.Quantity => MaxQuantityAmount,
        Measure.Duration => MaxDurationAmount,
        _ => throw new ArgumentOutOfRangeException(nameof(measure), measure, "Medida desconhecida."),
    };

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
