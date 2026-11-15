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
    public const int RecentNotesLimit = 5;

    /// <summary>Até <see cref="RecentNotesLimit"/> notas distintas da meta, da mais recente para a mais antiga.</summary>
    public Task<IReadOnlyList<string>> RecentNotesAsync(Guid goalId, CancellationToken ct = default) =>
        checkins.RecentNotesAsync(goalId, RecentNotesLimit, ct);

    /// <summary>Registra um ajuste no período atual e devolve o novo total do período.</summary>
    public async Task<int> AdjustAsync(Guid goalId, int amount, string? note = null, CancellationToken ct = default)
    {
        var (checkin, total) = await PrepareAsync(goalId, amount, note, ct);
        await checkins.AddAsync(checkin, ct);
        changes?.NotifyChanged();
        return total;
    }

    /// <summary>
    /// Valida e monta o registro de um ajuste no período atual, sem gravar, com o total que ele deixará. Quem chama grava
    /// (a troca de tarefa do cronômetro grava junto com o novo início, numa transação só).
    /// </summary>
    internal async Task<(Checkin Checkin, int Total)> PrepareAsync(Guid goalId, int amount, string? note, CancellationToken ct)
    {
        var goal = await ActiveGoalAsync(goalId, ct);
        var today = Today();

        note = Normalize(note);
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
        var stored = total - raw;
        Compensation? compensation = null;
        if (stored != amount && GoalPeriod.EvaluationPeriod(goal, today) is { } range)
            compensation = new Compensation(range.Start, await checkins.CompensationIdsAsync(goalId, range.Start, ct));
        var checkin = new Checkin
        {
            Id = Guid.CreateVersion7(now),
            GoalId = goalId,
            DoneAt = now,
            // Normalmente raw e o total limitado são iguais; só diferem após trocar a recorrência da meta,
            // e aí a tela já parte do total limitado.
            Amount = stored,
            Note = note,
            UpdatedAt = now,
            Compensation = compensation,
        };
        return (checkin, total);
    }

    /// <summary>Registros do dia local de hoje, de todas as metas, do mais recente para o mais antigo.</summary>
    public async Task<IReadOnlyList<Checkin>> TodayAsync(CancellationToken ct = default)
    {
        var (from, to) = TodayUtc();
        var list = await checkins.ListAsync(from, to, ct);
        return list.OrderByDescending(c => c.DoneAt).ThenByDescending(c => c.Id).ToList();
    }

    /// <summary>
    /// Troca os minutos e o nome de um registro de hoje de uma meta de tempo, mantendo o horário e o sinal. O registro
    /// antigo é apagado e um novo é gravado numa transação só (a sincronização não altera registros). Devolve o novo total.
    /// </summary>
    public async Task<int> ReplaceAsync(Guid goalId, Guid checkinId, int minutes, string? note, CancellationToken ct = default)
    {
        var (goal, old) = await EditableAsync(goalId, checkinId, ct);
        note = Normalize(note);
        var errors = new List<ValidationError>();
        if (minutes < 1 || minutes > MaxDurationAmount)
            errors.Add(new ValidationError("Amount", "Informe de 1 min a 24 h."));
        if (note is { Length: > MaxNoteLength })
            errors.Add(new ValidationError("Note", $"A nota deve ter até {MaxNoteLength} caracteres."));
        if (errors.Count > 0)
            throw new GoalValidationException(errors);

        var amount = Math.Sign(old.Amount) * minutes;
        if (amount == old.Amount && note == old.Note)
            return GoalTotals.Effective(goal, await TotalAsync(goal, Today(), ct));

        var total = await CheckedTotalAsync(goal, old, amount, ct);
        var now = time.GetUtcNow();
        var replacement = new Checkin
        {
            Id = Guid.CreateVersion7(now),
            GoalId = goalId,
            DoneAt = old.DoneAt,
            Amount = amount,
            Note = note,
            UpdatedAt = now,
        };
        if (!await checkins.ReplaceAsync(old.Id, replacement, ct))
            throw Changed(goalId);
        changes?.NotifyChanged();
        return total;
    }

    /// <summary>Apaga um registro de hoje de uma meta de tempo e devolve o novo total do período.</summary>
    public async Task<int> DeleteAsync(Guid goalId, Guid checkinId, CancellationToken ct = default)
    {
        var (goal, old) = await EditableAsync(goalId, checkinId, ct);
        var total = await CheckedTotalAsync(goal, old, 0, ct);
        if (!await checkins.DeleteAsync(old.Id, ct))
            throw Changed(goalId);
        changes?.NotifyChanged();
        return total;
    }

    private async Task<(Goal Goal, Checkin Checkin)> EditableAsync(Guid goalId, Guid checkinId, CancellationToken ct)
    {
        var goal = await ActiveGoalAsync(goalId, ct);
        if (goal.Measure != Measure.Duration)
            throw new GoalStateException(goalId, "Só dá para ajustar registros de metas de tempo.");
        var checkin = await checkins.GetAsync(checkinId, ct);
        if (checkin is null || checkin.GoalId != goalId)
            throw Changed(goalId);
        var (from, to) = TodayUtc();
        if (checkin.DoneAt < from || checkin.DoneAt >= to)
            throw new GoalStateException(goalId, "Só dá para ajustar registros de hoje.");
        if (checkin.Compensation is not null)
            throw new GoalStateException(goalId, "Este registro vem da junção de aparelhos e não pode ser ajustado.");
        return (goal, checkin);
    }

    /// <summary>
    /// Total limitado depois de trocar o valor do registro. Recusa só a mudança que diminui o total e o deixa negativo:
    /// depois de juntar aparelhos o total bruto pode já estar abaixo de zero, e aí aumentá-lo continua permitido.
    /// </summary>
    private async Task<int> CheckedTotalAsync(Goal goal, Checkin old, int amount, CancellationToken ct)
    {
        var raw = await TotalAsync(goal, Today(), ct) - old.Amount + amount;
        if (amount < old.Amount && raw < 0)
            throw new GoalStateException(goal.Id, "O total do período ficaria negativo.");
        return GoalTotals.Effective(goal, raw);
    }

    private static GoalStateException Changed(Guid goalId) => new(goalId, "O registro mudou. Confira em Hoje.");

    private async Task<Goal> ActiveGoalAsync(Guid goalId, CancellationToken ct)
    {
        var goal = await goals.GetAsync(goalId, ct) ?? throw new GoalNotFoundException(goalId);
        if (goal.ArchivedAt is not null)
            throw new GoalStateException(goalId, "Meta arquivada não recebe registros.");
        if (!GoalPeriod.IsShownOn(goal, Today()))
            throw new GoalStateException(goalId, "Esta meta não está ativa hoje.");
        return goal;
    }

    private static string? Normalize(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private (DateTimeOffset From, DateTimeOffset To) TodayUtc()
    {
        var today = Today();
        return (DateRange.LocalMidnightToUtc(today, time.LocalTimeZone),
            DateRange.LocalMidnightToUtc(today.AddDays(1), time.LocalTimeZone));
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
