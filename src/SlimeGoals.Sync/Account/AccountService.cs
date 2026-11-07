using System.Security.Cryptography;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Sync.Contracts;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Sync.Account;

/// <summary>
/// Conta de sincronização (Sincronizacao_v1 §6 e §7): junta a cripto, a API de conta, o cofre e o estado local.
/// Não guarda sessão na memória: o que precisa de token ou chave de dados lê o segredo <c>sync-session</c> do cofre.
/// Chaves derivadas e abertas são zeradas assim que saem de uso.
/// </summary>
/// <param name="server">Endereço do servidor, gravado no segredo da sessão.</param>
public sealed class AccountService(HttpAccountApi api, ISecretVault vault, ISyncStateStore state, Uri server)
{
    private const int MaxEmailLength = 254;
    private const int MaxDeviceNameLength = 100;
    private const int SaltSize = 16;

    /// <summary>Cadastra a conta e devolve as chaves para a confirmação; nada vai ao cofre antes de confirmar (decisão 2).</summary>
    /// <exception cref="ArgumentException">E-mail ou senha inválidos.</exception>
    /// <exception cref="InvalidOperationException">A sincronização já está ligada.</exception>
    /// <exception cref="SecretVaultUnavailableException">Sem cofre; nenhum pedido sai.</exception>
    /// <exception cref="AccountApiException">O servidor recusou ou não respondeu.</exception>
    public async Task<PendingRegistration> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        PasswordKeys.Validate(password);
        await EnsureSignedOutAsync(ct);
        await EnsureVaultAsync(ct);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var dataKey = AesGcmSyncCrypto.NewDataKey();
        try
        {
            // Os using valem até o fim do método: as chaves só são zeradas depois do último uso.
            using var passwordKeys = PasswordKeys.Derive(password, salt, KdfParams.Default);
            using var code = RecoveryCode.Generate();
            using var recoveryKeys = code.Keys();
            var request = new RegisterRequest(
                email, salt, KdfParams.Default.ToJson(), passwordKeys.AuthKey, recoveryKeys.RecoveryKey,
                KeyWrap.Wrap(dataKey, passwordKeys.WrapKey, WrapPurpose.Password),
                KeyWrap.Wrap(dataKey, recoveryKeys.WrapKey, WrapPurpose.Recovery));
            await api.RegisterAsync(request, ct);
            return new PendingRegistration(email, code.Display, passwordKeys.AuthKey.ToArray(), passwordKeys.WrapKey.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>Confirma o e-mail e entra com as chaves do cadastro, sem um segundo Argon2id.</summary>
    /// <remarks>Se a confirmação falhar, o mesmo <paramref name="pending"/> serve para tentar de novo.</remarks>
    public async Task ConfirmAsync(PendingRegistration pending, string code, string deviceName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        deviceName = RequireDeviceName(deviceName);
        _ = pending.AuthKey; // confere que não foi descartado antes de qualquer pedido
        await EnsureSignedOutAsync(ct);
        await EnsureVaultAsync(ct);

        await api.ConfirmAsync(code.Trim(), pending.AuthKey, ct);
        await EnterAsync(pending.Email, pending.AuthKey, pending.WrapKey, deviceName, ct);
    }

    /// <summary>Entra numa conta existente e liga a sincronização.</summary>
    /// <exception cref="KdfParamsException">O servidor mandou parâmetros ou sal fora dos limites; nada é derivado nem enviado.</exception>
    public async Task LoginAsync(string email, string password, string deviceName, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        ArgumentNullException.ThrowIfNull(password);
        deviceName = RequireDeviceName(deviceName);
        await EnsureSignedOutAsync(ct);
        await EnsureVaultAsync(ct);

        using var keys = await DeriveForAccountAsync(email, password, ct);
        await EnterAsync(email, keys.AuthKey, keys.WrapKey, deviceName, ct);
    }

    /// <summary>
    /// Troca a senha (decisão 13): este aparelho continua, os outros caem. A chave de dados não muda, então o cofre também não.
    /// </summary>
    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(currentPassword);
        PasswordKeys.Validate(newPassword);
        using var session = await RequireSessionAsync(ct);

        using var current = await DeriveForAccountAsync(session.Email!, currentPassword, ct);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        using var next = PasswordKeys.Derive(newPassword, salt, KdfParams.Default);
        var request = new ChangePasswordRequest(
            current.AuthKey, next.AuthKey, salt, KdfParams.Default.ToJson(),
            KeyWrap.Wrap(session.DataKey!, next.WrapKey, WrapPurpose.Password));
        await api.ChangePasswordAsync(session.Token!, request, ct);
    }

    /// <summary>
    /// Recupera a conta pelo código: abre a chave de dados com ele, define a nova senha (o servidor derruba todos os
    /// aparelhos) e entra neste aparelho. Devolve o mesmo código, já formatado, para a tela lembrar de guardá-lo.
    /// </summary>
    /// <exception cref="FormatException">Código de recuperação malformado.</exception>
    /// <exception cref="CryptographicException">O embrulho de recuperação não abre com o código.</exception>
    public async Task<string> RecoverAsync(string email, string recoveryCode, string newPassword, string deviceName, CancellationToken ct = default)
    {
        email = NormalizeEmail(email);
        PasswordKeys.Validate(newPassword);
        deviceName = RequireDeviceName(deviceName);
        using var code = RecoveryCode.Parse(recoveryCode);
        await EnsureSignedOutAsync(ct);
        await EnsureVaultAsync(ct);

        using var recoveryKeys = code.Keys();
        var begin = await api.RecoverBeginAsync(email, recoveryKeys.RecoveryKey, ct);
        var dataKey = KeyWrap.Unwrap(begin.WrappedKeyRecovery, recoveryKeys.WrapKey, WrapPurpose.Recovery);
        try
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            using var next = PasswordKeys.Derive(newPassword, salt, KdfParams.Default);
            var request = new RecoverRequest(
                email, recoveryKeys.RecoveryKey, next.AuthKey, salt, KdfParams.Default.ToJson(),
                KeyWrap.Wrap(dataKey, next.WrapKey, WrapPurpose.Password));
            await api.RecoverAsync(request, ct);
            await EnterAsync(email, next.AuthKey, next.WrapKey, deviceName, ct);
            return code.Display;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>
    /// Sai da conta (decisão 14): tenta o <c>logout</c>, apaga o segredo e desliga a sincronização. Falha do servidor
    /// no <c>logout</c> não impede o resto.
    /// </summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        SessionSecret? session;
        try
        {
            session = await ReadSessionAsync(ct);
        }
        catch (InvalidDataException)
        {
            // Segredo ilegível: sem token para o logout, mas a limpeza local segue, senão a conta fica presa.
            session = null;
        }

        using (session)
        {
            if (session is not null)
                await TryLogoutAsync(session.Token!);
        }

        await ClearLocalAsync(ct);
    }

    /// <summary>
    /// Apaga a conta no servidor (decisão 15) e faz a limpeza local de sair, sem <c>logout</c>: o servidor já apagou os
    /// aparelhos. Metas e registros ficam no aparelho (RS06).
    /// </summary>
    public async Task DeleteAccountAsync(string password, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        using (var session = await RequireSessionAsync(ct))
        {
            using var keys = await DeriveForAccountAsync(session.Email!, password, ct);
            await api.DeleteAccountAsync(session.Token!, keys.AuthKey, ct);
        }

        await ClearLocalAsync(ct);
    }

    public async Task<IReadOnlyList<DeviceDto>> ListDevicesAsync(CancellationToken ct = default)
    {
        using var session = await RequireSessionAsync(ct);
        return await api.ListDevicesAsync(session.Token!, ct);
    }

    public async Task RevokeDeviceAsync(Guid id, CancellationToken ct = default)
    {
        using var session = await RequireSessionAsync(ct);
        await api.RevokeDeviceAsync(session.Token!, id, ct);
    }

    /// <summary>A cripto e o provedor de token para o motor, ou nulo se não há sessão no cofre. Quem recebe descarta.</summary>
    /// <exception cref="InvalidDataException">O segredo do cofre está ilegível.</exception>
    public async Task<SyncSession?> OpenSessionAsync(CancellationToken ct = default)
    {
        using var session = await ReadSessionAsync(ct);
        if (session is null)
            return null;
        return new SyncSession(new Uri(session.Server!), session.Email!, session.DeviceId!.Value, session.Token!,
            new AesGcmSyncCrypto(session.DataKey!));
    }

    /// <summary>
    /// Ordem de entrada (decisão 12): <c>login</c>, abrir a chave de dados, gravar no cofre, ligar. Se algo depois do
    /// <c>login</c> falhar, o token emitido é revogado, o cofre fica vazio e nada liga.
    /// </summary>
    private async Task EnterAsync(string email, byte[] authKey, byte[] wrapKey, string deviceName, CancellationToken ct)
    {
        var login = await api.LoginAsync(email, authKey, deviceName, ct);
        var stored = false;
        try
        {
            using (var secret = new SessionSecret
            {
                Server = server.ToString(),
                Email = email,
                DeviceId = login.DeviceId,
                Token = login.Token,
                DataKey = KeyWrap.Unwrap(login.WrappedKeyPassword, wrapKey, WrapPurpose.Password),
            })
            {
                var bytes = secret.ToBytes();
                try
                {
                    await vault.SetAsync(SessionSecret.Name, bytes, ct);
                    stored = true;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                }
            }

            await state.EnableAsync(ct);
        }
        catch
        {
            if (stored)
                await TryDeleteSecretAsync();
            await TryLogoutAsync(login.Token);
            throw;
        }
    }

    /// <summary><c>prelogin</c>, conferência dos parâmetros e do sal, e só então o Argon2id.</summary>
    private async Task<PasswordKeys> DeriveForAccountAsync(string email, string password, CancellationToken ct)
    {
        var prelogin = await api.PreloginAsync(email, ct);
        var kdf = KdfParams.Parse(prelogin.KdfParams);
        if (prelogin.KdfSalt.Length < SaltSize)
            throw new KdfParamsException("O sal de derivação do servidor é curto demais.");
        return PasswordKeys.Derive(password, prelogin.KdfSalt, kdf);
    }

    private async Task EnsureSignedOutAsync(CancellationToken ct)
    {
        if ((await state.GetAsync(ct)).Enabled)
            throw new InvalidOperationException("Saia da conta antes de entrar em outra.");
    }

    /// <summary>Cofre antes da rede (decisão 11): sem ele, nenhum pedido sai.</summary>
    private async Task EnsureVaultAsync(CancellationToken ct)
    {
        if (!await vault.IsAvailableAsync(ct))
            throw new SecretVaultUnavailableException("O cofre de segredos do sistema não está disponível.");
    }

    private async Task<SessionSecret?> ReadSessionAsync(CancellationToken ct)
    {
        var bytes = await vault.GetAsync(SessionSecret.Name, ct);
        if (bytes is null)
            return null;
        try
        {
            return SessionSecret.Parse(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private async Task<SessionSecret> RequireSessionAsync(CancellationToken ct) =>
        await ReadSessionAsync(ct) ?? throw new InvalidOperationException("Entre na conta antes.");

    private async Task ClearLocalAsync(CancellationToken ct)
    {
        await vault.DeleteAsync(SessionSecret.Name, ct);
        await state.DisableAsync(ct);
    }

    /// <summary>Melhor esforço: sem rede ou com token já revogado, segue sem erro.</summary>
    private async Task TryLogoutAsync(string token)
    {
        try
        {
            await api.LogoutAsync(token, CancellationToken.None);
        }
        catch (Exception e) when (e is AccountApiException or HttpRequestException)
        {
        }
    }

    private async Task TryDeleteSecretAsync()
    {
        try
        {
            await vault.DeleteAsync(SessionSecret.Name, CancellationToken.None);
        }
        catch (SecretVaultUnavailableException)
        {
        }
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxEmailLength || !normalized.Contains('@', StringComparison.Ordinal))
            throw new ArgumentException("Informe um e-mail válido, com até 254 caracteres.", nameof(email));
        return normalized;
    }

    private static string RequireDeviceName(string deviceName)
    {
        var trimmed = deviceName?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxDeviceNameLength)
            throw new ArgumentException("O nome do aparelho deve ter de 1 a 100 caracteres.", nameof(deviceName));
        return trimmed;
    }
}
