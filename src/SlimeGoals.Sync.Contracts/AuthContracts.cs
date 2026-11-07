namespace SlimeGoals.Sync.Contracts;

/// <summary>Pedido do sal e dos parâmetros de derivação de um e-mail.</summary>
public sealed record PreloginRequest(string? Email);

public sealed record PreloginResponse(byte[] KdfSalt, string KdfParams);

/// <summary>Cadastro: o servidor só recebe a auth_key, a chave de recuperação e chaves já cifradas.</summary>
public sealed record RegisterRequest(
    string? Email,
    byte[]? KdfSalt,
    string? KdfParams,
    byte[]? AuthKey,
    byte[]? RecoveryKey,
    byte[]? WrappedKeyPassword,
    byte[]? WrappedKeyRecovery);

public sealed record ConfirmRequest(string? Token);

public sealed record LoginRequest(string? Email, byte[]? AuthKey, string? DeviceName);

public sealed record LoginResponse(string Token, Guid DeviceId, byte[] WrappedKeyPassword);

/// <summary>Corpo de todo erro: <c>{"error":"codigo"}</c>.</summary>
public sealed record ErrorResponse(string Error);

/// <summary>Troca de senha: prova a senha atual e envia o novo material de chave, já cifrado.</summary>
public sealed record ChangePasswordRequest(
    byte[]? CurrentAuthKey,
    byte[]? AuthKey,
    byte[]? KdfSalt,
    string? KdfParams,
    byte[]? WrappedKeyPassword);

/// <summary>Primeira etapa da recuperação: confere o código e pede a chave cifrada por ele.</summary>
public sealed record RecoverBeginRequest(string? Email, byte[]? RecoveryKey);

public sealed record RecoverBeginResponse(byte[] WrappedKeyRecovery);

/// <summary>Segunda etapa da recuperação: confere o código de novo e define a nova senha.</summary>
public sealed record RecoverRequest(
    string? Email,
    byte[]? RecoveryKey,
    byte[]? AuthKey,
    byte[]? KdfSalt,
    string? KdfParams,
    byte[]? WrappedKeyPassword);

public sealed record DeviceDto(Guid Id, string Name, DateTimeOffset LastSeenAt, bool IsCurrent);

/// <summary>Apagar a conta exige a auth_key, além do token do aparelho.</summary>
public sealed record DeleteAccountRequest(byte[]? AuthKey);
