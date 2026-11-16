using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>
/// Adaptador SQLite da porta <see cref="IFocusSessionRepository"/>: tabela focus_session, linha única (id = 1), e os
/// trechos em focus_chunk. Nada daqui sincroniza; os registros entram por <see cref="CheckinRepository.InsertAsync"/>.
/// </summary>
public sealed class FocusSessionRepository(SqliteConnectionFactory db) : IFocusSessionRepository
{
    private const string Columns = """
        plan, goal_id, started_at, step, block_started_at, cut_at, carry_ms, paused_at, task, finished_at, alerted_step, revision
        """;

    private const string DeleteAll = "DELETE FROM focus_chunk; DELETE FROM focus_session;";

    public async Task<FocusSession?> GetAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        try
        {
            var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
                SELECT plan AS Plan, goal_id AS GoalId, started_at AS StartedAt, step AS Step,
                       block_started_at AS BlockStartedAt, cut_at AS CutAt, carry_ms AS CarryMs, paused_at AS PausedAt,
                       task AS Task, finished_at AS FinishedAt, alerted_step AS AlertedStep, revision AS Revision
                FROM focus_session WHERE id = 1
                """, cancellationToken: ct));
            return row?.ToDomain();
        }
        catch (Exception e) when (RowReader.IsInvalidRow(e))
        {
            // Sem o plano ou os instantes não há relógio a retomar, e a linha ocuparia a vaga do único relógio para sempre.
            Trace.TraceError($"Sessão de Pomodoro inválida descartada: {TraceText.Types(e)}");
            await c.ExecuteAsync(new CommandDefinition(DeleteAll, cancellationToken: ct));
            return null;
        }
    }

    public async Task<IReadOnlyList<FocusChunk>> ChunksAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        return await RowReader.ReadValidAsync<ChunkRow, FocusChunk>(c, new CommandDefinition("""
            SELECT id AS Id, minutes AS Minutes, task AS Task, ended_at AS EndedAt, checkin_id AS CheckinId
            FROM focus_chunk ORDER BY ended_at, id
            """, cancellationToken: ct), "focus_chunk", r => r.ToDomain());
    }

    public async Task<bool> TryStartAsync(FocusSession session, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        // Um comando só confere as duas tabelas e grava: dois processos começando juntos não abrem dois relógios.
        var inserted = await c.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO focus_session (id, {Columns})
            SELECT 1, @Plan, @GoalId, @StartedAt, @Step, @BlockStartedAt, @CutAt, @CarryMs, @PausedAt, @Task, @FinishedAt,
                   @AlertedStep, @Revision
            WHERE NOT EXISTS (SELECT 1 FROM active_timer) AND NOT EXISTS (SELECT 1 FROM focus_session)
            """, Params(session, session.Revision), tx, cancellationToken: ct));
        if (inserted != 1)
            return false;
        // Trechos que sobraram de uma sessão anterior não entram na nova.
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM focus_chunk", transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> UpdateAsync(int expectedRevision, FocusSession? next, FocusChunk? chunk, Checkin? checkin,
        CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        try
        {
            if (next is null)
            {
                var removed = await c.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM focus_session WHERE id = 1 AND revision = @Expected", new { Expected = expectedRevision },
                    tx, cancellationToken: ct));
                if (removed != 1)
                    return false; // o descarte da transação desfaz
                await c.ExecuteAsync(new CommandDefinition("DELETE FROM focus_chunk", transaction: tx, cancellationToken: ct));
            }
            else
            {
                // No SET, step e alerted_step são os da linha gravada: no mesmo passo, a marca do aviso dada por outro
                // processo depois da leitura de quem grava fica, e o alarme não toca de novo.
                var updated = await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE focus_session SET
                      plan = @Plan, goal_id = @GoalId, started_at = @StartedAt, step = @Step,
                      block_started_at = @BlockStartedAt, cut_at = @CutAt, carry_ms = @CarryMs, paused_at = @PausedAt,
                      task = @Task, finished_at = @FinishedAt,
                      alerted_step = CASE WHEN step = @Step THEN alerted_step ELSE @AlertedStep END,
                      revision = @Revision
                    WHERE id = 1 AND revision = @Expected
                    """, Params(next, expectedRevision + 1, expectedRevision), tx, cancellationToken: ct));
                if (updated != 1)
                    return false;
                if (chunk is not null)
                    await c.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO focus_chunk (id, minutes, task, ended_at, checkin_id)
                        VALUES (@Id, @Minutes, @Task, @EndedAt, @CheckinId)
                        """, ChunkParams(chunk), tx, cancellationToken: ct));
            }

            if (checkin is not null)
                await CheckinRepository.InsertAsync(c, tx, checkin, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (SqliteException e) when (checkin is not null && e.SqliteExtendedErrorCode == SqlText.ForeignKeyViolation)
        {
            throw new GoalNotFoundException(checkin.GoalId); // a meta foi excluída depois de quem corta lê-la
        }
    }

    public async Task<bool> AssignChunkAsync(Guid chunkId, Checkin checkin, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        try
        {
            // Só o trecho ainda sem registro: dois processos somando juntos não registram o mesmo tempo duas vezes.
            var linked = await c.ExecuteAsync(new CommandDefinition(
                "UPDATE focus_chunk SET checkin_id = @CheckinId WHERE id = @Id AND checkin_id IS NULL",
                new { Id = SqlText.Id(chunkId), CheckinId = SqlText.Id(checkin.Id) }, tx, cancellationToken: ct));
            if (linked != 1)
                return false;
            await CheckinRepository.InsertAsync(c, tx, checkin, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (SqliteException e) when (e.SqliteExtendedErrorCode == SqlText.ForeignKeyViolation)
        {
            throw new GoalNotFoundException(checkin.GoalId);
        }
    }

    public async Task<bool> MarkAlertedAsync(int step, CancellationToken ct = default)
    {
        await using var c = db.Open();
        // Sem mexer na revisão: a marca não conflita com quem está gravando a sessão.
        return await c.ExecuteAsync(new CommandDefinition(
            "UPDATE focus_session SET alerted_step = @Step WHERE id = 1 AND step = @Step",
            new { Step = step }, cancellationToken: ct)) == 1;
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(DeleteAll, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    private static object Params(FocusSession s, int revision, int expected = 0) => new
    {
        Plan = s.Plan.ToJson(),
        GoalId = s.GoalId is { } g ? SqlText.Id(g) : null,
        StartedAt = SqlText.Instant(s.StartedAt),
        s.Step,
        BlockStartedAt = SqlText.Instant(s.BlockStartedAt),
        CutAt = SqlText.Instant(s.CutAt),
        // Milissegundos inteiros: o que fica abaixo disso não muda um minuto de foco.
        CarryMs = s.Carry.Ticks / TimeSpan.TicksPerMillisecond,
        PausedAt = s.PausedAt is { } p ? SqlText.Instant(p) : null,
        s.Task,
        FinishedAt = s.FinishedAt is { } f ? SqlText.Instant(f) : null,
        s.AlertedStep,
        Revision = revision,
        Expected = expected,
    };

    private static object ChunkParams(FocusChunk chunk) => new
    {
        Id = SqlText.Id(chunk.Id),
        chunk.Minutes,
        chunk.Task,
        EndedAt = SqlText.Instant(chunk.EndedAt),
        CheckinId = chunk.CheckinId is { } id ? SqlText.Id(id) : null,
    };

    private sealed class Row
    {
        public required string Plan { get; init; }
        public string? GoalId { get; init; }
        public required string StartedAt { get; init; }
        public long Step { get; init; }
        public required string BlockStartedAt { get; init; }
        public required string CutAt { get; init; }
        public long CarryMs { get; init; }
        public string? PausedAt { get; init; }
        public string? Task { get; init; }
        public string? FinishedAt { get; init; }
        public long? AlertedStep { get; init; }
        public long Revision { get; init; }

        public FocusSession ToDomain()
        {
            var plan = FocusPlan.FromJson(Plan) ?? throw new FormatException("Plano ilegível.");
            if (plan.Validate().Count > 0)
                throw new FormatException("Plano fora dos limites.");
            var step = checked((int)Step);
            if (step < 0 || step >= plan.TotalSteps)
                throw new FormatException("Passo fora do plano.");
            return new FocusSession(plan, GoalId is null ? null : Guid.ParseExact(GoalId, "D"),
                SqlText.ParseInstant(StartedAt), step, SqlText.ParseInstant(BlockStartedAt), SqlText.ParseInstant(CutAt),
                TimeSpan.FromMilliseconds(CarryMs), Instant(PausedAt), Task, Instant(FinishedAt),
                AlertedStep is { } a ? checked((int)a) : null, checked((int)Revision));
        }

        private static DateTimeOffset? Instant(string? value) => value is null ? null : SqlText.ParseInstant(value);
    }

    private sealed class ChunkRow
    {
        public required string Id { get; init; }
        public long Minutes { get; init; }
        public string? Task { get; init; }
        public required string EndedAt { get; init; }
        public string? CheckinId { get; init; }

        public FocusChunk ToDomain() => new(Guid.ParseExact(Id, "D"), checked((int)Minutes), Task,
            SqlText.ParseInstant(EndedAt), CheckinId is null ? null : Guid.ParseExact(CheckinId, "D"));
    }
}
