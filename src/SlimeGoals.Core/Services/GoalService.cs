using System.Globalization;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Casos de uso de metas: criar, editar, arquivar e excluir (de vez, com tudo ligado à meta).
/// O relógio vem de <see cref="TimeProvider"/> para os testes controlarem o tempo.
/// Registros entram para travar a medida; o cronômetro, para ser limpo quando a meta sai de cena.
/// Os lembretes entram na exclusão, para as notificações pendentes saírem junto; sem eles, só o banco é limpo.
/// </summary>
public sealed class GoalService(IGoalRepository goals, ICheckinRepository checkins, ITimerRepository timers, TimeProvider time,
    DataChangeNotifier? changes = null, ReminderService? reminders = null)
{
    public async Task<Goal> CreateAsync(GoalDraft draft, CancellationToken ct = default)
    {
        draft = Normalize(draft);
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
        changes?.NotifyChanged();
        return goal;
    }

    public async Task<Goal> UpdateAsync(Guid id, GoalDraft draft, CancellationToken ct = default)
    {
        var current = await GetExistingAsync(id, ct);
        if (current.ArchivedAt is not null)
            throw new GoalStateException(id, "Meta arquivada não pode ser editada. Desarquive-a primeiro.");
        draft = Normalize(draft);
        EnsureValid(draft);
        if (draft.Measure != current.Measure && await timers.GetAsync(ct) is { } timer && timer.GoalId == id)
            throw new GoalValidationException(
                [new ValidationError(nameof(GoalDraft.Measure), "Pare o cronômetro antes de mudar a medida.")]);
        if (draft.Measure != current.Measure && await checkins.HasAnyAsync(id, ct))
            throw new GoalValidationException(
                [new ValidationError(nameof(GoalDraft.Measure), "A medida não muda depois do primeiro registro.")]);
        var updated = Apply(current, draft) with { UpdatedAt = time.GetUtcNow() };
        await goals.UpdateAsync(updated, ct);
        changes?.NotifyChanged();
        return updated;
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var goal = await GetExistingAsync(id, ct);
        if (goal.ArchivedAt is not null)
            return; // idempotente: preserva a data original do arquivamento

        var now = time.GetUtcNow();
        await goals.UpdateAsync(goal with { ArchivedAt = now, UpdatedAt = now }, ct);
        await ClearTimerOfAsync(id, ct);
        changes?.NotifyChanged();
    }

    /// <summary>
    /// Volta a meta para a lista ativa. Idempotente. Recusa meta cujo período já terminou
    /// (seria arquivada de novo).
    /// </summary>
    public async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
    {
        var goal = await GetExistingAsync(id, ct);
        if (goal.ArchivedAt is null)
            return;
        if (goal.EndDate is { } end && end < Today())
            throw new GoalStateException(id,
                $"O período desta meta terminou em {end.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}. Crie uma nova meta para continuar.");

        await goals.UpdateAsync(goal with { ArchivedAt = null, UpdatedAt = time.GetUtcNow() }, ct);
        changes?.NotifyChanged();
    }

    /// <summary>Arquiva as metas ativas cujo fim já passou (spec RF10). Devolve quantas arquivou.</summary>
    public async Task<int> ArchiveExpiredAsync(CancellationToken ct = default)
    {
        var today = Today();
        var now = time.GetUtcNow();
        var expired = (await goals.ListAsync(includeArchived: false, ct))
            .Where(g => g.EndDate is { } end && end < today)
            .ToList();

        foreach (var goal in expired)
        {
            await goals.UpdateAsync(goal with { ArchivedAt = now, UpdatedAt = now }, ct);
            await ClearTimerOfAsync(goal.Id, ct);
        }
        if (expired.Count > 0)
            changes?.NotifyChanged();
        return expired.Count;
    }

    /// <summary>Apaga a meta com seus registros, lembretes e cronômetro. Não há como desfazer.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await GetExistingAsync(id, ct);
        await (reminders?.DeleteGoalAsync(id, ct) ?? goals.DeleteAsync(id, ct));
        changes?.NotifyChanged();
    }

    /// <summary>A meta já tem registros (a medida fica travada).</summary>
    public Task<bool> HasRecordsAsync(Guid id, CancellationToken ct = default) => checkins.HasAnyAsync(id, ct);

    private async Task ClearTimerOfAsync(Guid goalId, CancellationToken ct)
    {
        if (await timers.GetAsync(ct) is { } timer && timer.GoalId == goalId)
            await timers.ClearAsync(ct);
    }

    public Task<Goal?> GetAsync(Guid id, CancellationToken ct = default) => goals.GetAsync(id, ct);

    public Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived = false, CancellationToken ct = default) =>
        goals.ListAsync(includeArchived, ct);

    /// <summary>
    /// O banco guarda horários como HH:mm: descarta segundos. Unidade só existe na quantidade;
    /// conclusão tem sempre alvo 1 (spec §5.2).
    /// </summary>
    private static GoalDraft Normalize(GoalDraft draft) => draft with
    {
        ActiveFrom = new TimeOnly(draft.ActiveFrom.Hour, draft.ActiveFrom.Minute),
        ActiveUntil = new TimeOnly(draft.ActiveUntil.Hour, draft.ActiveUntil.Minute),
        Target = draft.Measure == Measure.Completion ? 1 : draft.Target,
        Unit = draft.Measure == Measure.Quantity ? draft.Unit?.Trim() : null,
    };

    /// <summary>Data de hoje no fuso local do relógio injetado.</summary>
    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    private void EnsureValid(GoalDraft draft)
    {
        var errors = GoalValidator.Validate(draft).ToList();
        if (draft.EndDate is { } end && end < Today())
            errors.Add(new ValidationError(nameof(GoalDraft.EndDate), "A data de fim não pode estar no passado."));
        if (errors.Count > 0)
            throw new GoalValidationException(errors);
    }

    private async Task<Goal> GetExistingAsync(Guid id, CancellationToken ct) =>
        await goals.GetAsync(id, ct) ?? throw new GoalNotFoundException(id);

    /// <summary>Copia os campos editáveis do rascunho; preserva id, criação e arquivamento.</summary>
    private static Goal Apply(Goal goal, GoalDraft draft) => goal with
    {
        Title = draft.Title.Trim(),
        Recurrence = draft.Recurrence,
        Target = draft.Target,
        Measure = draft.Measure,
        Unit = draft.Unit,
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
