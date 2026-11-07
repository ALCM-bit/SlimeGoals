using System.Data;
using System.Diagnostics;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>
/// Aplica o que chega do servidor dentro da transação de quem chama (spec de sincronização §4). Dado aplicado não
/// volta para a fila; a única exceção é o <c>delete</c> de um upsert que chegou para uma meta com marca de exclusão.
/// </summary>
internal sealed class SyncApply
{
    /// <summary>SQLITE_CONSTRAINT_CHECK: a linha fere uma regra da tabela. A instrução falha e a transação continua.</summary>
    private const int CheckViolation = 275;

    private readonly SqliteConnection _c;
    private readonly IDbTransaction _tx;
    private readonly CancellationToken _ct;
    private readonly HashSet<CompensationGroup> _touched = [];

    private SyncApply(SqliteConnection c, IDbTransaction tx, CancellationToken ct)
    {
        _c = c;
        _tx = tx;
        _ct = ct;
    }

    private sealed record KnownRecord(string RecordId, string Kind);

    private sealed record CompensationGroup(string GoalId, string Period);

    private sealed record PendingRow(string RecordId, string Payload);

    private sealed class CompensationRow
    {
        public required string Id { get; init; }
        public required string DoneAt { get; init; }
        public string? Seen { get; init; }

        public CompensationEntry ToEntry() =>
            new(Guid.ParseExact(Id, "D"), SqlText.ParseInstant(DoneAt), SqlText.ParseIdList(Seen));
    }

    /// <summary>Aplica a página e grava <paramref name="lastSeq"/>. Devolve as metas excluídas, mesmo as que já não tinham linha.</summary>
    public static async Task<IReadOnlyList<Guid>> ApplyPageAsync(SqliteConnection c, IDbTransaction tx,
        IReadOnlyList<IncomingRecord> records, long lastSeq, Guid deviceId, CancellationToken ct)
    {
        var apply = new SyncApply(c, tx, ct);
        var deleted = await apply.ApplyGoalsAsync(records, deviceId);
        await apply.ApplyCheckinsAsync(records);
        await apply.RecalculateCompensationsAsync();
        await apply.RunAsync("UPDATE sync_state SET last_seq = @LastSeq", new { LastSeq = lastSeq });
        return deleted;
    }

    public static Task BeginFullResyncAsync(SqliteConnection c, IDbTransaction tx, CancellationToken ct) =>
        new SyncApply(c, tx, ct).RunAsync("""
            UPDATE sync_state SET last_seq = 0, full_resync = 1;
            UPDATE sync_records SET confirmed = 0;
            """, null);

    /// <summary>
    /// Tira o que o servidor não devolveu e a fila não guarda, sem marca nova. O que está na fila volta a ser
    /// desconhecido do servidor: base 0 também no mapa, senão a próxima edição enfileiraria o <c>seq</c> antigo.
    /// </summary>
    public static async Task<IReadOnlyList<Guid>> FinishFullResyncAsync(SqliteConnection c, IDbTransaction tx, CancellationToken ct)
    {
        var apply = new SyncApply(c, tx, ct);
        if (await c.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT full_resync FROM sync_state", transaction: tx, cancellationToken: ct)) != 1)
            return [];

        var removed = new List<Guid>();
        foreach (var id in await apply.UnconfirmedAsync(SyncOutbox.Goal))
        {
            var goalId = Guid.ParseExact(id, "D");
            if (await apply.RemoveGoalAsync(goalId) > 0)
                removed.Add(goalId);
        }
        foreach (var id in await apply.UnconfirmedAsync(SyncOutbox.Checkin))
            await apply.RemoveCheckinAsync(id);

        await apply.RecalculateCompensationsAsync();
        await apply.RunAsync("""
            UPDATE sync_outbox SET base_seq = 0 WHERE record_id IN (SELECT record_id FROM sync_records WHERE confirmed = 0);
            UPDATE sync_records SET seq = 0 WHERE confirmed = 0;
            UPDATE sync_state SET full_resync = 0;
            """, null);
        return removed;
    }

    private async Task<List<Guid>> ApplyGoalsAsync(IReadOnlyList<IncomingRecord> records, Guid deviceId)
    {
        var deleted = new List<Guid>();
        foreach (var record in records)
        {
            if (record.Unreadable || record.Envelope is null)
            {
                if (await FindAsync(record.RecordKey) is not { Kind: SyncOutbox.Goal } known)
                    continue;
                if (record.Unreadable)
                {
                    await KeepLocalCopyAsync(record, known);
                    continue;
                }
                var goalId = Guid.ParseExact(known.RecordId, "D");
                await RemoveGoalAsync(goalId);
                await RunAsync("INSERT OR IGNORE INTO sync_tombstones (goal_id) VALUES (@Id)", new { Id = known.RecordId });
                deleted.Add(goalId);
            }
            else if (record.Envelope.Kind == SyncRecordKind.Goal)
            {
                await ApplyGoalAsync(record, record.Envelope, deviceId);
            }
        }
        return deleted;
    }

    /// <summary>Roda depois das metas: a exclusão de uma meta já tirou do mapa as chaves dos registros dela.</summary>
    private async Task ApplyCheckinsAsync(IReadOnlyList<IncomingRecord> records)
    {
        foreach (var record in records)
        {
            if (record.Unreadable || record.Envelope is null)
            {
                if (await FindAsync(record.RecordKey) is not { Kind: SyncOutbox.Checkin } known)
                    continue;
                if (record.Unreadable)
                    await KeepLocalCopyAsync(record, known);
                else
                    await RemoveCheckinAsync(known.RecordId);
            }
            else if (record.Envelope.Kind == SyncRecordKind.Checkin)
            {
                await ApplyCheckinAsync(record, record.Envelope);
            }
        }
    }

    private async Task ApplyGoalAsync(IncomingRecord record, SyncEnvelope envelope, Guid deviceId)
    {
        var goal = Decode(envelope, SyncCodec.DecodeGoal, g => g.Id);
        if (goal is null)
        {
            await KeepUnreadableAsync(record);
            return;
        }
        if (await IsTombstonedAsync(goal.Id))
        {
            await DiscardAsync(record, envelope, SyncOutbox.Goal);
            return;
        }

        var parameters = new { Id = SqlText.Id(goal.Id), record.Seq };
        if (await QueuedEditAsync(goal.Id) is { } localUpdatedAt
            && SyncMerge.LocalWins(localUpdatedAt, deviceId, goal.UpdatedAt, envelope.DeviceId))
        {
            await RunAsync("UPDATE sync_outbox SET base_seq = @Seq WHERE record_id = @Id", parameters);
        }
        else
        {
            if (!await UpsertGoalAsync(goal))
            {
                await KeepUnreadableAsync(record);
                return;
            }
            await RunAsync("DELETE FROM sync_outbox WHERE record_id = @Id AND op = 'upsert'", parameters);
        }

        await MapAsync(record, SqlText.Id(envelope.RecordId), SyncOutbox.Goal);
        await ApplyPendingAsync(goal.Id);
    }

    private async Task ApplyCheckinAsync(IncomingRecord record, SyncEnvelope envelope)
    {
        var checkin = Decode(envelope, SyncCodec.DecodeCheckin, r => r.Id);
        if (checkin is null)
        {
            await KeepUnreadableAsync(record);
            return;
        }
        if (await IsTombstonedAsync(checkin.GoalId))
        {
            await DiscardAsync(record, envelope, SyncOutbox.Checkin);
            return;
        }

        await MapAsync(record, SqlText.Id(envelope.RecordId), SyncOutbox.Checkin);
        if (await GoalExistsAsync(checkin.GoalId))
            await InsertCheckinAsync(checkin);
        else
            await RunAsync("""
                INSERT INTO sync_pending (record_id, goal_id, seq, payload) VALUES (@Id, @GoalId, @Seq, @Payload)
                ON CONFLICT (record_id) DO UPDATE SET goal_id = excluded.goal_id, seq = excluded.seq, payload = excluded.payload
                """, new { Id = SqlText.Id(checkin.Id), GoalId = SqlText.Id(checkin.GoalId), record.Seq, envelope.Payload });
    }

    private async Task ApplyPendingAsync(Guid goalId)
    {
        var rows = await _c.QueryAsync<PendingRow>(new CommandDefinition(
            "SELECT record_id AS RecordId, payload AS Payload FROM sync_pending WHERE goal_id = @Id",
            new { Id = SqlText.Id(goalId) }, _tx, cancellationToken: _ct));
        foreach (var row in rows)
        {
            try
            {
                await InsertCheckinAsync(SyncCodec.DecodeCheckin(row.Payload));
            }
            catch (FormatException e)
            {
                Trace.TraceError($"Registro pendente inválido ignorado na sincronização ({row.RecordId}): {e.Message}");
            }
            await RunAsync("DELETE FROM sync_pending WHERE record_id = @Id", new { Id = row.RecordId });
        }
    }

    /// <summary>Só acrescenta: o registro que já existe fica como está. Sem limite de valor (spec §3).</summary>
    private async Task InsertCheckinAsync(Checkin checkin)
    {
        var row = CheckinRepository.Row.FromDomain(checkin);
        await RunAsync("""
            INSERT OR IGNORE INTO checkins (id, goal_id, done_at, amount, note, updated_at, deleted_at, compensation_period, compensation_seen)
            VALUES (@Id, @GoalId, @DoneAt, @Amount, @Note, @UpdatedAt, @DeletedAt, @CompensationPeriod, @CompensationSeen);
            DELETE FROM sync_outbox WHERE record_id = @Id AND op = 'upsert';
            """, row);
        if (row.CompensationPeriod is { } period)
            _touched.Add(new CompensationGroup(row.GoalId, period));
    }

    /// <summary>ON CONFLICT em vez de REPLACE: apagar a linha alcançaria o que depende dela. deleted_at não viaja e fica como está.</summary>
    private async Task<bool> UpsertGoalAsync(Goal goal)
    {
        try
        {
            await RunAsync("""
                INSERT INTO goals (id, title, recurrence, target, measure, unit, icon, color, reminder_every_min, active_from,
                                   active_until, active_days, start_date, end_date, created_at, updated_at, archived_at)
                VALUES (@Id, @Title, @Recurrence, @Target, @Measure, @Unit, @Icon, @Color, @ReminderEveryMin, @ActiveFrom,
                        @ActiveUntil, @ActiveDays, @StartDate, @EndDate, @CreatedAt, @UpdatedAt, @ArchivedAt)
                ON CONFLICT (id) DO UPDATE SET title = excluded.title, recurrence = excluded.recurrence, target = excluded.target,
                    measure = excluded.measure, unit = excluded.unit, icon = excluded.icon, color = excluded.color,
                    reminder_every_min = excluded.reminder_every_min, active_from = excluded.active_from,
                    active_until = excluded.active_until, active_days = excluded.active_days, start_date = excluded.start_date,
                    end_date = excluded.end_date, created_at = excluded.created_at, updated_at = excluded.updated_at,
                    archived_at = excluded.archived_at
                """, GoalRow.FromDomain(goal));
            return true;
        }
        catch (SqliteException e) when (e.SqliteExtendedErrorCode == CheckViolation)
        {
            Trace.TraceError($"Meta inválida ignorada na sincronização ({goal.Id:D}): {e.Message}");
            return false;
        }
    }

    /// <summary>Tira a meta, os registros dela e tudo o que a sincronização guarda sobre eles. Devolve quantas linhas de meta saíram.</summary>
    private async Task<int> RemoveGoalAsync(Guid goalId)
    {
        await RunAsync("""
            DELETE FROM sync_outbox WHERE record_id = @Id
                OR record_id IN (SELECT id FROM checkins WHERE goal_id = @Id)
                OR record_id IN (SELECT record_id FROM sync_pending WHERE goal_id = @Id);
            DELETE FROM sync_records WHERE record_id = @Id
                OR record_id IN (SELECT id FROM checkins WHERE goal_id = @Id)
                OR record_id IN (SELECT record_id FROM sync_pending WHERE goal_id = @Id);
            DELETE FROM sync_pending WHERE goal_id = @Id;
            """, new { Id = SqlText.Id(goalId) });
        return await GoalDeletion.DeleteRowsAsync(_c, _tx, goalId, _ct);
    }

    private async Task RemoveCheckinAsync(string recordId)
    {
        var parameters = new { Id = recordId };
        var group = await _c.QueryFirstOrDefaultAsync<CompensationGroup>(new CommandDefinition("""
            SELECT goal_id AS GoalId, compensation_period AS Period FROM checkins
            WHERE id = @Id AND compensation_period IS NOT NULL
            """, parameters, _tx, cancellationToken: _ct));
        if (group is not null)
            _touched.Add(group);

        await RunAsync("""
            DELETE FROM checkins WHERE id = @Id;
            DELETE FROM sync_pending WHERE record_id = @Id;
            DELETE FROM sync_outbox WHERE record_id = @Id;
            DELETE FROM sync_records WHERE record_id = @Id;
            """, parameters);
    }

    /// <summary>A meta foi excluída aqui: o conteúdo não entra, e o servidor recebe a exclusão com a base que acabou de mostrar.</summary>
    private async Task DiscardAsync(IncomingRecord record, SyncEnvelope envelope, string kind)
    {
        await MapAsync(record, SqlText.Id(envelope.RecordId), kind);
        await RunAsync("""
            INSERT INTO sync_outbox (record_id, kind, op, base_seq) VALUES (@Id, @Kind, 'delete', @Seq)
            ON CONFLICT (record_id) DO UPDATE SET op = 'delete', base_seq = excluded.base_seq, revision = revision + 1
            """, new { Id = SqlText.Id(envelope.RecordId), Kind = kind, record.Seq });
    }

    private async Task KeepUnreadableAsync(IncomingRecord record)
    {
        if (await FindAsync(record.RecordKey) is { } known)
            await KeepLocalCopyAsync(record, known);
    }

    /// <summary>
    /// O conteúdo não entra, mas o registro existe no servidor. Sem confirmar, o fim de uma sincronização completa
    /// apagaria a cópia boa do aparelho; sem a base nova, a edição na fila daria conflito em todo ciclo.
    /// </summary>
    private async Task KeepLocalCopyAsync(IncomingRecord record, KnownRecord known)
    {
        await MapAsync(record, known.RecordId, known.Kind);
        await RunAsync("UPDATE sync_outbox SET base_seq = @Seq WHERE record_id = @Id", new { Id = known.RecordId, record.Seq });
    }

    private async Task RecalculateCompensationsAsync()
    {
        foreach (var group in _touched)
        {
            // Inclui as que já estavam fora: a chegada ou a saída de uma pode trazê-las de volta.
            var entries = await RowReader.ReadValidAsync<CompensationRow, CompensationEntry>(_c, new CommandDefinition("""
                SELECT id AS Id, done_at AS DoneAt, compensation_seen AS Seen FROM checkins
                WHERE goal_id = @GoalId AND compensation_period = @Period AND deleted_at IS NULL
                """, group, _tx, cancellationToken: _ct), "checkins", r => r.ToEntry());
            var superseded = CompensationRules.Superseded(entries);
            foreach (var entry in entries)
                await RunAsync("UPDATE checkins SET superseded = @Value WHERE id = @Id",
                    new { Id = SqlText.Id(entry.Id), Value = superseded.Contains(entry.Id) ? 1 : 0 });
        }
    }

    private static T? Decode<T>(SyncEnvelope envelope, Func<string, T> decode, Func<T, Guid> idOf) where T : class
    {
        try
        {
            var value = decode(envelope.Payload);
            if (idOf(value) == envelope.RecordId)
                return value;
            Trace.TraceError($"Registro inválido ignorado na sincronização ({envelope.Kind} {envelope.RecordId:D}): o conteúdo é de outro registro.");
        }
        catch (FormatException e)
        {
            Trace.TraceError($"Registro inválido ignorado na sincronização ({envelope.Kind} {envelope.RecordId:D}): {e.Message}");
        }
        return null;
    }

    /// <summary>Procura a chave no mapa e, para o aceite que se perdeu, na fila.</summary>
    private Task<KnownRecord?> FindAsync(string recordKey) =>
        _c.QueryFirstOrDefaultAsync<KnownRecord?>(new CommandDefinition("""
            SELECT record_id AS RecordId, kind AS Kind FROM sync_records WHERE record_key = @Key
            UNION ALL
            SELECT record_id, kind FROM sync_outbox WHERE record_key = @Key
            LIMIT 1
            """, new { Key = recordKey }, _tx, cancellationToken: _ct));

    /// <summary>Data da edição local que espera envio. Data que não converte não disputa: a linha já não seria enviada.</summary>
    private async Task<DateTimeOffset?> QueuedEditAsync(Guid goalId)
    {
        var text = await _c.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT g.updated_at FROM goals g JOIN sync_outbox o ON o.record_id = g.id
            WHERE g.id = @Id AND o.op = 'upsert'
            """, new { Id = SqlText.Id(goalId) }, _tx, cancellationToken: _ct));
        return DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
    }

    private Task<bool> IsTombstonedAsync(Guid goalId) => _c.ExecuteScalarAsync<bool>(new CommandDefinition(
        "SELECT EXISTS (SELECT 1 FROM sync_tombstones WHERE goal_id = @Id)",
        new { Id = SqlText.Id(goalId) }, _tx, cancellationToken: _ct));

    private Task<bool> GoalExistsAsync(Guid goalId) => _c.ExecuteScalarAsync<bool>(new CommandDefinition(
        "SELECT EXISTS (SELECT 1 FROM goals WHERE id = @Id)",
        new { Id = SqlText.Id(goalId) }, _tx, cancellationToken: _ct));

    private async Task<List<string>> UnconfirmedAsync(string kind) => (await _c.QueryAsync<string>(new CommandDefinition("""
        SELECT record_id FROM sync_records
        WHERE kind = @Kind AND confirmed = 0 AND record_id NOT IN (SELECT record_id FROM sync_outbox)
        """, new { Kind = kind }, _tx, cancellationToken: _ct))).ToList();

    private Task MapAsync(IncomingRecord record, string recordId, string kind) => RunAsync("""
        INSERT INTO sync_records (record_id, kind, seq, record_key, confirmed) VALUES (@Id, @Kind, @Seq, @RecordKey, 1)
        ON CONFLICT (record_id) DO UPDATE SET kind = excluded.kind, seq = excluded.seq, record_key = excluded.record_key, confirmed = 1
        """, new { Id = recordId, Kind = kind, record.Seq, record.RecordKey });

    private Task<int> RunAsync(string sql, object? parameters) =>
        _c.ExecuteAsync(new CommandDefinition(sql, parameters, _tx, cancellationToken: _ct));
}
