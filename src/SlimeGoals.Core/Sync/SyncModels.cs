namespace SlimeGoals.Core.Sync;

public enum SyncRecordKind { Goal, Checkin }

/// <summary>Conteúdo que o aparelho cifra: versão do formato, tipo, ids e o payload em JSON.</summary>
public sealed record SyncEnvelope(int Version, SyncRecordKind Kind, Guid RecordId, Guid DeviceId, string Payload);

/// <summary>Registro do servidor. Blob nulo = excluído.</summary>
public sealed record RemoteRecord(string RecordKey, long Seq, byte[]? Blob);

public sealed record SyncPullPage(IReadOnlyList<RemoteRecord> Records, bool HasMore);

public sealed record PushItem(string RecordKey, long BaseSeq, byte[]? Blob);

public sealed record PushAccepted(string RecordKey, long Seq);

public sealed record PushResult(IReadOnlyList<PushAccepted> Accepted, IReadOnlyList<string> Conflicts);

/// <summary>Registro já aberto, pronto para aplicar. Envelope nulo = excluído.</summary>
public sealed record IncomingRecord(string RecordKey, long Seq, SyncEnvelope? Envelope);

public sealed record OutboxItem(Guid RecordId, SyncRecordKind Kind, bool IsDelete, long BaseSeq, long Revision, string? Payload);

public sealed record PushConfirmation(Guid RecordId, SyncRecordKind Kind, bool IsDelete, string RecordKey, long Seq, long Revision);

public sealed record SyncStatus(DateTimeOffset? LastSyncAt, string? LastError, int Pending);

/// <summary>O servidor respondeu 410: o histórico local não vale mais e é preciso sincronizar tudo de novo.</summary>
public sealed class SyncResetRequiredException : Exception;

/// <summary>Versão do formato desconhecida.</summary>
public sealed class SyncFormatException(int version) : Exception($"Versão de formato de sincronização desconhecida: {version}.")
{
    public int Version { get; } = version;
}
