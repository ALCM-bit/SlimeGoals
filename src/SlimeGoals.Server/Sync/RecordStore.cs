using System.Data;
using Dapper;
using SlimeGoals.Server.Data;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Sync;

/// <summary>Registros cifrados de uma conta. A conta vem sempre da sessão, nunca do cliente (regra 6).</summary>
public sealed class RecordStore(Db db, TimeProvider time)
{
    /// <summary>Ponto de teste, chamado entre a leitura do ponto da limpeza e a dos registros.</summary>
    internal Func<Task>? AfterPointRead { get; set; }

    /// <summary>
    /// Página de registros com <c>seq</c> acima de <paramref name="after"/>, em ordem. Ponto da limpeza e registros saem da mesma
    /// foto do banco (decisão 9): com leituras separadas, uma limpeza entre as duas faria o aparelho perder marcas sem receber 410.
    /// </summary>
    /// <exception cref="ResetRequiredException"><paramref name="after"/> está abaixo do ponto e o pedido não é <paramref name="full"/>.</exception>
    public async Task<PullResponse> PullAsync(Guid account, long after, int limit, bool full, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY", transaction: transaction, cancellationToken: cancellationToken));
        var purgePoint = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT purge_point FROM accounts WHERE id = @account",
            new { account },
            transaction,
            cancellationToken: cancellationToken)) ?? 0;
        if (AfterPointRead is { } hook)
        {
            await hook();
        }

        // Registros vivos ficam abaixo do ponto: quem lê tudo passa por ele sem 410, senão nunca terminaria.
        if (!full && after < purgePoint)
        {
            throw new ResetRequiredException();
        }

        var rows = (await connection.QueryAsync<RecordDto>(new CommandDefinition(
            """
            SELECT record_key AS Key, seq AS Seq, blob AS Blob
            FROM records
            WHERE account_id = @account AND seq > @after
            ORDER BY seq
            LIMIT @take
            """,
            new { account, after, take = limit + 1 },
            transaction,
            cancellationToken: cancellationToken))).ToList();
        return new PullResponse(rows.Take(limit).ToArray(), rows.Count > limit);
    }

    /// <summary>
    /// Aplica o lote, já validado, e devolve nulo se a conta não existe mais. Tudo roda numa transação com a linha da conta
    /// travada até o commit: é a trava que faz os envios aparecerem em ordem de <c>seq</c>, sem buracos para o <c>pull</c> (regra 1).
    /// </summary>
    public async Task<PushResponse?> PushAsync(Guid account, IReadOnlyList<PushItemDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return new PushResponse([], []);
        }

        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var locked = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition(
            "SELECT next_seq FROM accounts WHERE id = @account FOR UPDATE",
            new { account },
            transaction,
            cancellationToken: cancellationToken));
        if (locked is not { } nextSeq)
        {
            return null;
        }

        var keys = items.Select(i => i.RecordKey!).Distinct().ToArray();
        var currentSeq = (await connection.QueryAsync<(string Key, long Seq)>(new CommandDefinition(
            "SELECT record_key, seq FROM records WHERE account_id = @account AND record_key = ANY(@keys)",
            new { account, keys },
            transaction,
            cancellationToken: cancellationToken))).ToDictionary(r => r.Key, r => r.Seq);

        var accepted = new List<AcceptedDto>();
        var conflicts = new List<string>();
        // Por chave, só o último aceito do lote: um mesmo INSERT não pode tocar duas vezes na mesma linha.
        var writes = new Dictionary<string, (long Seq, byte[]? Blob)>();
        foreach (var item in items)
        {
            var key = item.RecordKey!;
            if (item.BaseSeq != currentSeq.GetValueOrDefault(key))
            {
                conflicts.Add(key);
                continue;
            }

            var seq = nextSeq++;
            currentSeq[key] = seq;
            writes[key] = (seq, item.Blob);
            accepted.Add(new AcceptedDto(key, seq));
        }

        if (writes.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO records (account_id, record_key, seq, blob, deleted_at)
                SELECT @account, w.record_key, w.seq, w.blob, CASE WHEN w.blob IS NULL THEN @now END
                FROM unnest(@writeKeys, @writeSeqs, @writeBlobs) AS w(record_key, seq, blob)
                ON CONFLICT (account_id, record_key) DO UPDATE
                SET seq = EXCLUDED.seq, blob = EXCLUDED.blob, deleted_at = EXCLUDED.deleted_at
                """,
                new
                {
                    account,
                    now = time.GetUtcNow(),
                    writeKeys = writes.Keys.ToArray(),
                    writeSeqs = writes.Values.Select(w => w.Seq).ToArray(),
                    writeBlobs = writes.Values.Select(w => w.Blob).ToArray(),
                },
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE accounts SET next_seq = @nextSeq WHERE id = @account",
                new { account, nextSeq },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return new PushResponse(accepted.ToArray(), conflicts.ToArray());
    }
}

/// <summary>O aparelho parou antes do ponto da limpeza e precisa ler tudo de novo (<c>410</c>).</summary>
public sealed class ResetRequiredException : Exception;
