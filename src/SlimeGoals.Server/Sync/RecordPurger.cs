using Dapper;
using Microsoft.Extensions.Options;
using SlimeGoals.Server.Data;

namespace SlimeGoals.Server.Sync;

/// <summary>O que uma limpeza removeu: marcas de exclusão e contas não confirmadas.</summary>
public sealed record PurgeResult(int Marks, int Accounts);

/// <summary>
/// Limpeza diária (§5, regra 5). Remove marcas de exclusão antigas e avança o <c>purge_point</c> da conta, que é a base do
/// <c>410</c>; remove também contas que ninguém confirmou (decisão 6).
/// </summary>
public class RecordPurger(Db db, TimeProvider time, IOptions<ServerOptions> options)
{
    public virtual async Task<PurgeResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var retention = options.Value.Retention;
        var markCutoff = now.AddDays(-retention.TombstoneDays);
        var accountCutoff = now.AddHours(-retention.UnconfirmedAccountHours);

        await using var connection = await db.OpenAsync(cancellationToken);
        var accounts = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM accounts WHERE email_confirmed_at IS NULL AND created_at < @accountCutoff",
            new { accountCutoff },
            cancellationToken: cancellationToken));

        var candidates = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT DISTINCT account_id FROM records WHERE deleted_at < @markCutoff",
            new { markCutoff },
            cancellationToken: cancellationToken))).ToList();
        var marks = 0;
        foreach (var account in candidates)
        {
            marks += await PurgeAccountAsync(connection, account, markCutoff, cancellationToken);
        }

        return new PurgeResult(marks, accounts);
    }

    /// <summary>
    /// Com a linha da conta travada: <c>candidato</c> é o maior <c>seq</c> das marcas velhas mais um; o ponto nunca recua nem
    /// passa do maior <c>seq</c> da conta (<c>next_seq - 1</c>), para quem lê tudo sempre alcançá-lo (decisão 10).
    /// </summary>
    private static async Task<int> PurgeAccountAsync(
        Npgsql.NpgsqlConnection connection, Guid account, DateTimeOffset markCutoff, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var locked = await connection.QuerySingleOrDefaultAsync<(long NextSeq, long PurgePoint)?>(new CommandDefinition(
            "SELECT next_seq AS NextSeq, purge_point AS PurgePoint FROM accounts WHERE id = @account FOR UPDATE",
            new { account },
            transaction,
            cancellationToken: cancellationToken));
        if (locked is not { } state)
        {
            return 0;
        }

        var newest = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT MAX(seq) FROM records WHERE account_id = @account AND deleted_at < @markCutoff",
            new { account, markCutoff },
            transaction,
            cancellationToken: cancellationToken));
        if (newest is not { } candidate)
        {
            return 0;
        }

        var point = Math.Max(state.PurgePoint, Math.Min(candidate + 1, state.NextSeq - 1));
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE accounts SET purge_point = @point WHERE id = @account",
            new { account, point },
            transaction,
            cancellationToken: cancellationToken));
        var removed = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM records WHERE account_id = @account AND deleted_at < @markCutoff AND seq < @point",
            new { account, markCutoff, point },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return removed;
    }
}
