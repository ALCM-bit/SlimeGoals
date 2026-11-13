using Dapper;
using SlimeGoals.Server.Data;

namespace SlimeGoals.Server.Accounts;

public sealed record AccountKdf(byte[] KdfSalt, string KdfParams);

public sealed record NewAccount(
    string Email,
    byte[] KdfSalt,
    string KdfParams,
    byte[] AuthHash,
    byte[] RecoveryHash,
    byte[] WrappedKeyPassword,
    byte[] WrappedKeyRecovery,
    byte[] ConfirmHash,
    DateTimeOffset ConfirmExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record LoginAccount(Guid Id, byte[] AuthHash, byte[] WrappedKeyPassword);

public sealed record DeviceIdentity(Guid AccountId, Guid DeviceId);

public sealed record RecoveryAccount(Guid Id, byte[] RecoveryHash, byte[] WrappedKeyRecovery);

public sealed record NewPassword(byte[] AuthHash, byte[] KdfSalt, string KdfParams, byte[] WrappedKeyPassword);

/// <summary>O código de recuperação que substitui o usado: o hash da sua chave e a chave de dados cifrada por ele.</summary>
public sealed record NewRecovery(byte[] RecoveryHash, byte[] WrappedKeyRecovery);

public sealed record DeviceInfo(Guid Id, string Name, DateTimeOffset LastSeenAt);

internal sealed record DeviceListRow(Guid Id, string Name, DateTime LastSeenAt);

internal sealed record PendingAccount(Guid Id, byte[] AuthHash);

internal sealed record DeviceRow(Guid AccountId, Guid DeviceId, DateTime LastSeenAt);

/// <summary>Consultas de conta e aparelho. Todo e-mail chega aqui já normalizado (decisão 15).</summary>
public sealed class AccountStore(Db db)
{
    private static readonly TimeSpan LastSeenInterval = TimeSpan.FromHours(1);

    public async Task<AccountKdf?> FindKdfAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountKdf>(new CommandDefinition(
            "SELECT kdf_salt AS KdfSalt, kdf_params AS KdfParams FROM accounts WHERE email = @email AND email_confirmed_at IS NOT NULL",
            new { email },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Cria a conta ou substitui, numa só instrução, a conta ainda não confirmada do e-mail: material de chave,
    /// código e vencimento novos, e o código antigo deixa de valer. Conta confirmada fica intacta e devolve falso.
    /// O <c>ON CONFLICT ... DO UPDATE</c> trava a linha, então dois cadastros simultâneos nunca criam duas linhas
    /// nem deixam uma linha meio trocada.
    /// </summary>
    public async Task<bool> TryCreateOrReplacePendingAsync(NewAccount account, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO accounts (id, email, kdf_salt, kdf_params, auth_hash, recovery_hash,
                                  wrapped_key_password, wrapped_key_recovery, confirm_hash, confirm_expires_at, created_at)
            VALUES (@id, @Email, @KdfSalt, @KdfParams, @AuthHash, @RecoveryHash,
                    @WrappedKeyPassword, @WrappedKeyRecovery, @ConfirmHash, @ConfirmExpiresAt, @CreatedAt)
            ON CONFLICT (email) DO UPDATE
               SET kdf_salt = EXCLUDED.kdf_salt, kdf_params = EXCLUDED.kdf_params, auth_hash = EXCLUDED.auth_hash,
                   recovery_hash = EXCLUDED.recovery_hash, wrapped_key_password = EXCLUDED.wrapped_key_password,
                   wrapped_key_recovery = EXCLUDED.wrapped_key_recovery, confirm_hash = EXCLUDED.confirm_hash,
                   confirm_expires_at = EXCLUDED.confirm_expires_at, created_at = EXCLUDED.created_at
             WHERE accounts.email_confirmed_at IS NULL
            """,
            new
            {
                id = Guid.NewGuid(),
                account.Email,
                account.KdfSalt,
                account.KdfParams,
                account.AuthHash,
                account.RecoveryHash,
                account.WrappedKeyPassword,
                account.WrappedKeyRecovery,
                account.ConfirmHash,
                account.ConfirmExpiresAt,
                account.CreatedAt,
            },
            cancellationToken: cancellationToken));
        return rows == 1;
    }

    /// <summary>Desfaz um cadastro cujo e-mail não saiu, achando a linha pelo hash da confirmação.</summary>
    public async Task DeleteUnconfirmedAsync(byte[] confirmHash, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM accounts WHERE confirm_hash = @confirmHash AND email_confirmed_at IS NULL",
            new { confirmHash },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Confirma a conta com o token e a auth_key de quem cadastrou; o código vale uma vez e até o vencimento.
    /// A linha fica travada entre a conferência e a confirmação, para um cadastro simultâneo não trocar a chave no meio.
    /// </summary>
    public async Task<bool> ConfirmAsync(byte[] tokenHash, byte[] authHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var pending = await connection.QuerySingleOrDefaultAsync<PendingAccount>(new CommandDefinition(
            """
            SELECT id AS Id, auth_hash AS AuthHash FROM accounts
             WHERE confirm_hash = @tokenHash AND confirm_expires_at >= @now AND email_confirmed_at IS NULL
               FOR UPDATE
            """,
            new { tokenHash, now },
            transaction,
            cancellationToken: cancellationToken));
        if (pending is null || !SecretHash.Equal(pending.AuthHash, authHash))
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE accounts SET email_confirmed_at = @now, confirm_hash = NULL, confirm_expires_at = NULL WHERE id = @id",
            new { now, id = pending.Id },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<LoginAccount?> FindForLoginAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LoginAccount>(new CommandDefinition(
            """
            SELECT id AS Id, auth_hash AS AuthHash, wrapped_key_password AS WrappedKeyPassword
              FROM accounts WHERE email = @email AND email_confirmed_at IS NOT NULL
            """,
            new { email },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Cria o aparelho só se a conta ainda existe com a mesma auth_key que o login conferiu. A linha da conta
    /// fica travada com FOR SHARE, que conflita com a troca de senha e a exclusão: ou o aparelho nasce antes
    /// da revogação e é revogado, ou o insert já vê o hash novo. Nulo quando não pode criar.
    /// </summary>
    public async Task<Guid?> CreateDeviceAsync(
        Guid accountId, byte[] verifiedAuthHash, string name, byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var currentHash = await connection.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition(
            "SELECT auth_hash FROM accounts WHERE id = @accountId FOR SHARE",
            new { accountId },
            transaction,
            cancellationToken: cancellationToken));
        if (currentHash is null || !SecretHash.Equal(currentHash, verifiedAuthHash))
        {
            return null;
        }

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO devices (id, account_id, name, token_hash, last_seen_at) VALUES (@id, @accountId, @name, @tokenHash, @now)",
            new { id, accountId, name, tokenHash, now },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Revoga o aparelho, sempre dentro da conta da sessão.</summary>
    public async Task RevokeDeviceAsync(Guid accountId, Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE devices SET revoked_at = @now WHERE id = @deviceId AND account_id = @accountId AND revoked_at IS NULL",
            new { now, deviceId, accountId },
            cancellationToken: cancellationToken));
    }

    /// <summary>Hash da auth_key da conta da sessão.</summary>
    public async Task<byte[]?> FindAuthHashAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<byte[]>(new CommandDefinition(
            "SELECT auth_hash FROM accounts WHERE id = @accountId",
            new { accountId },
            cancellationToken: cancellationToken));
    }

    /// <summary>Conta confirmada de um e-mail, com o necessário para a recuperação.</summary>
    public async Task<RecoveryAccount?> FindForRecoveryAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RecoveryAccount>(new CommandDefinition(
            """
            SELECT id AS Id, recovery_hash AS RecoveryHash, wrapped_key_recovery AS WrappedKeyRecovery
              FROM accounts WHERE email = @email AND email_confirmed_at IS NOT NULL
            """,
            new { email },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Troca o material de senha e revoga aparelhos na mesma transação: os outros aparelhos da conta
    /// (<paramref name="keepDeviceId"/> fica) ou todos, se nulo. Só vale se a auth_key atual ainda for a esperada.
    /// </summary>
    public async Task<bool> SetPasswordAsync(
        Guid accountId, byte[]? expectedAuthHash, NewPassword password, Guid? keepDeviceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE accounts
               SET auth_hash = @AuthHash, kdf_salt = @KdfSalt, kdf_params = @KdfParams, wrapped_key_password = @WrappedKeyPassword
             WHERE id = @accountId AND (@expectedAuthHash::bytea IS NULL OR auth_hash = @expectedAuthHash::bytea)
            """,
            new { password.AuthHash, password.KdfSalt, password.KdfParams, password.WrappedKeyPassword, accountId, expectedAuthHash },
            transaction,
            cancellationToken: cancellationToken));
        if (rows != 1)
        {
            return false;
        }

        await RevokeDevicesAsync(connection, transaction, accountId, keepDeviceId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Recuperação: troca o material de senha e o código de recuperação e revoga todos os aparelhos, numa transação.
    /// Só vale se o código atual ainda for o conferido (<paramref name="usedRecoveryHash"/>): de dois pedidos com o
    /// mesmo código, só o primeiro passa, e o código usado nunca vale duas vezes.
    /// </summary>
    public async Task<bool> RecoverAsync(
        Guid accountId, byte[] usedRecoveryHash, NewPassword password, NewRecovery recovery, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE accounts
               SET auth_hash = @AuthHash, kdf_salt = @KdfSalt, kdf_params = @KdfParams, wrapped_key_password = @WrappedKeyPassword,
                   recovery_hash = @RecoveryHash, wrapped_key_recovery = @WrappedKeyRecovery
             WHERE id = @accountId AND recovery_hash = @usedRecoveryHash
            """,
            new
            {
                password.AuthHash, password.KdfSalt, password.KdfParams, password.WrappedKeyPassword,
                recovery.RecoveryHash, recovery.WrappedKeyRecovery, accountId, usedRecoveryHash,
            },
            transaction,
            cancellationToken: cancellationToken));
        if (rows != 1)
        {
            return false;
        }

        await RevokeDevicesAsync(connection, transaction, accountId, null, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static Task RevokeDevicesAsync(
        System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, Guid accountId, Guid? keepDeviceId,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE devices SET revoked_at = @now
             WHERE account_id = @accountId AND revoked_at IS NULL AND (@keepDeviceId::uuid IS NULL OR id <> @keepDeviceId::uuid)
            """,
            new { now, accountId, keepDeviceId },
            transaction,
            cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<DeviceListRow>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, last_seen_at AS LastSeenAt FROM devices WHERE account_id = @accountId AND revoked_at IS NULL ORDER BY last_seen_at DESC, id",
            new { accountId },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new DeviceInfo(r.Id, r.Name, new DateTimeOffset(DateTime.SpecifyKind(r.LastSeenAt, DateTimeKind.Utc))))];
    }

    /// <summary>Revoga um aparelho da conta; falso se ele não existe nesta conta. Já revogado conta como sucesso, sem mudar nada.</summary>
    public async Task<bool> RevokeOwnedDeviceAsync(Guid accountId, Guid deviceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            WITH target AS (SELECT id FROM devices WHERE id = @deviceId AND account_id = @accountId),
                 revoked AS (UPDATE devices SET revoked_at = @now WHERE id IN (SELECT id FROM target) AND revoked_at IS NULL)
            SELECT EXISTS (SELECT 1 FROM target)
            """,
            new { now, deviceId, accountId },
            cancellationToken: cancellationToken));
    }

    /// <summary>Apaga a conta; aparelhos e registros saem em cascata.</summary>
    public async Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM accounts WHERE id = @accountId",
            new { accountId },
            cancellationToken: cancellationToken));
    }

    /// <summary>Acha o aparelho ativo do token, sem cache, e atualiza last_seen_at no máximo uma vez por hora.</summary>
    public async Task<DeviceIdentity?> FindActiveDeviceAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var device = await connection.QuerySingleOrDefaultAsync<DeviceRow>(new CommandDefinition(
            "SELECT account_id AS AccountId, id AS DeviceId, last_seen_at AS LastSeenAt FROM devices WHERE token_hash = @tokenHash AND revoked_at IS NULL",
            new { tokenHash },
            cancellationToken: cancellationToken));
        if (device is null)
        {
            return null;
        }

        if (now - new DateTimeOffset(device.LastSeenAt, TimeSpan.Zero) >= LastSeenInterval)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE devices SET last_seen_at = @now WHERE id = @id AND last_seen_at < @now",
                new { now, id = device.DeviceId },
                cancellationToken: cancellationToken));
        }

        return new DeviceIdentity(device.AccountId, device.DeviceId);
    }
}
