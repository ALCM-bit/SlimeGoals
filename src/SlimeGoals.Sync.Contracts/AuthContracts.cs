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
