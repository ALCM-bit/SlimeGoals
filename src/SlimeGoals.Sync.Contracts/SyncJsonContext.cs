using System.Text.Json.Serialization;

namespace SlimeGoals.Sync.Contracts;

/// <summary>
/// JSON do protocolo de sincronização: nomes em snake_case, bytes em base64. Fica em <c>Metadata</c> porque o serializador
/// rápido gerado escreve um <c>byte[]</c> nulo como <c>""</c>, e um registro excluído viraria um vivo de blob vazio.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, GenerationMode = JsonSourceGenerationMode.Metadata)]
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
[JsonSerializable(typeof(PullResponse))]
[JsonSerializable(typeof(PushRequest))]
[JsonSerializable(typeof(PushResponse))]
public sealed partial class SyncJsonContext : JsonSerializerContext;
