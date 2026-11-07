using System.Text.Json.Serialization;

namespace SlimeGoals.Sync.Contracts;

/// <summary>JSON do protocolo de sincronização: nomes em snake_case, bytes em base64.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PreloginRequest))]
[JsonSerializable(typeof(PreloginResponse))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(ConfirmRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(RecoverBeginRequest))]
[JsonSerializable(typeof(RecoverBeginResponse))]
[JsonSerializable(typeof(RecoverRequest))]
[JsonSerializable(typeof(DeviceDto[]))]
[JsonSerializable(typeof(DeleteAccountRequest))]
public sealed partial class SyncJsonContext : JsonSerializerContext;
