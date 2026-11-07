using System.Text.Json.Serialization;

namespace SlimeGoals.Sync.Contracts;

// Nas respostas, campo ausente é erro de leitura: sem isso, um "blob" que faltasse viraria exclusão.

/// <summary>Registro de uma página do <c>pull</c>. Blob nulo = excluído.</summary>
public sealed record RecordDto(
    [property: JsonRequired] string Key,
    [property: JsonRequired] long Seq,
    [property: JsonRequired] byte[]? Blob);

public sealed record PullResponse(
    [property: JsonRequired] RecordDto[] Records,
    [property: JsonRequired] bool HasMore);

/// <summary>Item do lote: leva <c>blob</c> ou <c>delete: true</c>, nunca os dois (decisão 4).</summary>
public sealed record PushItemDto(string? RecordKey, long? BaseSeq, byte[]? Blob, bool Delete);

public sealed record PushRequest(PushItemDto[]? Items);

public sealed record AcceptedDto(
    [property: JsonRequired] string Key,
    [property: JsonRequired] long Seq);

/// <summary>Todo item do lote aparece em uma das listas, na ordem do lote.</summary>
public sealed record PushResponse(
    [property: JsonRequired] AcceptedDto[] Accepted,
    [property: JsonRequired] string[] Conflicts);
