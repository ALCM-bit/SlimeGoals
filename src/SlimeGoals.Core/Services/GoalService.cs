using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Casos de uso de metas: criar, editar, arquivar e excluir (logicamente).
/// O relógio vem de <see cref="TimeProvider"/> para os testes controlarem o tempo.
/// </summary>
public sealed class GoalService(IGoalRepository goals, TimeProvider time)
{
    public async Task<Goal> CreateAsync(GoalDraft draft, CancellationToken ct = default)
    {
        EnsureValid(draft);
        var now = time.GetUtcNow();
        var goal = Apply(new Goal
        {
            // GUID v7 começa pelo timestamp: ids ficam ordenáveis por criação.
            Id = Guid.CreateVersion7(now),
            Title = "",
            Recurrence = draft.Recurrence,
            Target = 1,
            Icon = "",
            Color = default,
            ReminderEveryMinutes = 0,
            ActiveFrom = default,
            ActiveUntil = default,
            ActiveDays = WeekDays.None,
            CreatedAt = now,
            UpdatedAt = now,
        }, draft);

        await goals.AddAsync(goal, ct);
        return goal;
    }

    public async Task<Goal> UpdateAsync(Guid id, GoalDraft draft, CancellationToken ct = default)
    {
        EnsureValid(draft);
        var current = await GetExistingAsync(id, ct);
        var updated = Apply(current, draft) with { UpdatedAt = time.GetUtcNow() };
        await goals.UpdateAsync(updated, ct);
        return updated;
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var goal = await GetExistingAsync(id, ct);
        if (goal.ArchivedAt is not null)
            return; // idempotente: preserva a data original do arquivamento

        var now = time.GetUtcNow();
        await goals.UpdateAsync(goal with { ArchivedAt = now, UpdatedAt = now }, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var goal = await GetExistingAsync(id, ct);
        var now = time.GetUtcNow();
        await goals.UpdateAsync(goal with { DeletedAt = now, UpdatedAt = now }, ct);
    }

    public Task<Goal?> GetAsync(Guid id, CancellationToken ct = default) => goals.GetAsync(id, ct);

    public Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived = false, CancellationToken ct = default) =>
        goals.ListAsync(includeArchived, ct);

    private static void EnsureValid(GoalDraft draft)
    {
        var errors = GoalValidator.Validate(draft);
        if (errors.Count > 0)
            throw new GoalValidationException(errors);
    }

    private async Task<Goal> GetExistingAsync(Guid id, CancellationToken ct) =>
        await goals.GetAsync(id, ct) ?? throw new GoalNotFoundException(id);

    /// <summary>Copia os campos editáveis do rascunho; preserva id, criação, conclusão e arquivamento.</summary>
    private static Goal Apply(Goal goal, GoalDraft draft) => goal with
    {
        Title = draft.Title.Trim(),
        Recurrence = draft.Recurrence,
        Target = draft.Target,
        Icon = draft.Icon,
        Color = draft.Color,
        ReminderEveryMinutes = draft.ReminderEveryMinutes,
        ActiveFrom = draft.ActiveFrom,
        ActiveUntil = draft.ActiveUntil,
        ActiveDays = draft.ActiveDays,
        StartDate = draft.StartDate,
        EndDate = draft.EndDate,
    };
}
