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

public sealed record LoginAccount(Guid Id, byte[] AuthHash, bool Confirmed, byte[] WrappedKeyPassword);

public sealed record DeviceIdentity(Guid AccountId, Guid DeviceId);

internal sealed record DeviceRow(Guid AccountId, Guid DeviceId, DateTime LastSeenAt);

/// <summary>Consultas de conta e aparelho. Todo e-mail chega aqui já normalizado (decisão 15).</summary>
public sealed class AccountStore(Db db)
{
    private static readonly TimeSpan LastSeenInterval = TimeSpan.FromHours(1);

    public async Task<AccountKdf?> FindKdfAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountKdf>(new CommandDefinition(
            "SELECT kdf_salt AS KdfSalt, kdf_params AS KdfParams FROM accounts WHERE email = @email",
            new { email },
            cancellationToken: cancellationToken));
    }

    /// <summary>Cria a conta; devolve falso se o e-mail já existe (inclusive numa corrida).</summary>
    public async Task<bool> TryCreateAsync(NewAccount account, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO accounts (id, email, kdf_salt, kdf_params, auth_hash, recovery_hash,
                                  wrapped_key_password, wrapped_key_recovery, confirm_hash, confirm_expires_at, created_at)
            VALUES (@id, @Email, @KdfSalt, @KdfParams, @AuthHash, @RecoveryHash,
                    @WrappedKeyPassword, @WrappedKeyRecovery, @ConfirmHash, @ConfirmExpiresAt, @CreatedAt)
            ON CONFLICT (email) DO NOTHING
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

    /// <summary>Confirma a conta com o token; o código vale uma vez e até o vencimento.</summary>
    public async Task<bool> ConfirmAsync(byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE accounts
               SET email_confirmed_at = @now, confirm_hash = NULL, confirm_expires_at = NULL
             WHERE confirm_hash = @tokenHash AND confirm_expires_at >= @now AND email_confirmed_at IS NULL
            """,
            new { tokenHash, now },
            cancellationToken: cancellationToken));
        return rows == 1;
    }

    public async Task<LoginAccount?> FindForLoginAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await db.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<LoginAccount>(new CommandDefinition(
            """
            SELECT id AS Id, auth_hash AS AuthHash, (email_confirmed_at IS NOT NULL) AS Confirmed,
                   wrapped_key_password AS WrappedKeyPassword
              FROM accounts WHERE email = @email
            """,
            new { email },
            cancellationToken: cancellationToken));
    }

    public async Task<Guid> CreateDeviceAsync(
        Guid accountId, string name, byte[] tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = await db.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO devices (id, account_id, name, token_hash, last_seen_at) VALUES (@id, @accountId, @name, @tokenHash, @now)",
            new { id, accountId, name, tokenHash, now },
            cancellationToken: cancellationToken));
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
