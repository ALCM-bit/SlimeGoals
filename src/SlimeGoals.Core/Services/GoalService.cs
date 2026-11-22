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
    DataChangeNotifier? changes = null, ReminderService? reminders = null, ITagRepository? tags = null)
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

        var plan = await PlanTagsAsync(goal.Id, draft.Tags, now, ct);
        goal = goal with { Tags = plan.Names ?? goal.Tags };
        await goals.AddAsync(goal, plan.Changes, ct);
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
        var updated = Apply(current, draft) with { UpdatedAt = NextUpdatedAt(current) };
        var plan = await PlanTagsAsync(id, draft.Tags, time.GetUtcNow(), ct);
        updated = updated with { Tags = plan.Names ?? updated.Tags };
        await goals.UpdateAsync(updated, plan.Changes, ct);
        changes?.NotifyChanged();
        return updated;
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var goal = await GetExistingAsync(id, ct);
        if (goal.ArchivedAt is not null)
            return; // idempotente: preserva a data original do arquivamento

        var now = time.GetUtcNow();
        await goals.UpdateAsync(goal with { ArchivedAt = now, UpdatedAt = NextUpdatedAt(goal) }, ct);
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

        await goals.UpdateAsync(goal with { ArchivedAt = null, UpdatedAt = NextUpdatedAt(goal) }, ct);
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
            await goals.UpdateAsync(goal with { ArchivedAt = now, UpdatedAt = NextUpdatedAt(goal) }, ct);
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

    /// <summary>
    /// Data de edição: a de agora, ou 1 ms depois da conhecida se o relógio estiver atrasado,
    /// para a edição sempre vencer a versão que o aparelho já viu.
    /// </summary>
    private DateTimeOffset NextUpdatedAt(Goal current)
    {
        var now = time.GetUtcNow();
        var after = current.UpdatedAt.AddMilliseconds(1);
        return now > after ? now : after;
    }

    /// <summary>
    /// Calcula as linhas de tag e de ligação do salvamento. Só as tags que entram na meta agora e não estão
    /// visíveis são criadas ou reativadas: uma tag que a meta já tinha e que ainda não chegou fica só como ligação.
    /// Com <paramref name="requested"/> nulo, as tags ficam como estão.
    /// </summary>
    private async Task<(TagChanges Changes, IReadOnlyList<string>? Names)> PlanTagsAsync(
        Guid goalId, IReadOnlyList<string>? requested, DateTimeOffset now, CancellationToken ct)
    {
        if (requested is null || tags is null)
            return (TagChanges.Empty, null);

        var wanted = requested.Select(TagName.Normalize).Distinct(StringComparer.Ordinal).ToList();
        var state = new TagService.TagState(await tags.ListTagsAsync(ct), await tags.ListLinksAsync(ct));
        var own = state.Links.Where(l => l.GoalId == goalId).ToDictionary(l => l.TagName, StringComparer.Ordinal);
        var newTags = new List<Tag>();
        var newLinks = new List<GoalTag>();
        var colors = state.Visible.Values.Select(t => t.Color).ToList();

        foreach (var name in wanted)
        {
            own.TryGetValue(name, out var link);
            state.Tags.TryGetValue(name, out var tag);
            if (link is not null && TagVisibility.IsLinkVisible(link, tag))
                continue;

            // Tag excluída que segue à vista (outra meta a marcou depois): a ligação precisa ser mais nova que a exclusão,
            // senão um relógio atrasado a deixaria escondida.
            var at = link is null ? now : TagService.Next(now, link.UpdatedAt);
            if (tag?.DeletedAt is { } deletedAt && at <= deletedAt)
                at = TagService.Next(now, deletedAt);
            newLinks.Add(link is null
                ? TagService.NewLink(goalId, name, at)
                : link with { RemovedAt = null, UpdatedAt = at });
            if (state.Visible.ContainsKey(name))
                continue;
            if (tag is not null)
                newLinks.AddRange(state.HiddenLinksOf(tag).Where(l => l.GoalId != goalId)
                    .Select(l => TagService.RemoveAt(l, TagService.Next(now, l.UpdatedAt))));
            var written = tag is null
                ? TagService.NewTag(name, TagService.PickColor(colors, colors.Count), now)
                : tag with { DeletedAt = null, UpdatedAt = TagService.Next(now, tag.UpdatedAt) };
            colors.Add(written.Color);
            newTags.Add(written);
        }

        foreach (var (name, link) in own)
        {
            if (!wanted.Contains(name) && TagVisibility.IsLinkVisible(link, state.Tags.GetValueOrDefault(name)))
            {
                var at = TagService.Next(now, link.UpdatedAt);
                newLinks.Add(link with { RemovedAt = at, UpdatedAt = at });
            }
        }

        var allTags = state.Tags.Values.Where(t => newTags.All(n => n.Id != t.Id)).Concat(newTags);
        var allLinks = state.Links.Where(l => newLinks.All(n => n.Id != l.Id)).Concat(newLinks);
        var names = TagVisibility.NamesFor(goalId, allLinks, allTags.ToDictionary(t => t.Name, StringComparer.Ordinal));
        return (new TagChanges(newTags, newLinks), names);
    }

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
