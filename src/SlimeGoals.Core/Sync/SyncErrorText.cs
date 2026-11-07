using System.Net;

namespace SlimeGoals.Core.Sync;

/// <summary>
/// Texto de <c>last_error</c> para cada falha do ciclo. Só textos fixos: a mensagem da exceção, o corpo da resposta e o
/// endereço nunca chegam à pessoa. O detalhe técnico fica no <c>Trace</c>.
/// </summary>
public static class SyncErrorText
{
    public static string For(Exception e) => e switch
    {
        SyncUnauthorizedException => "A sessão deste aparelho foi encerrada. Entre de novo.",
        HttpRequestException { HttpRequestError: HttpRequestError.InvalidResponse } => "O servidor respondeu de um jeito inesperado.",
        HttpRequestException { StatusCode: null } => "Sem conexão com o servidor.",
        HttpRequestException { StatusCode: { } status } => ForStatus(status),
        TimeoutException or OperationCanceledException => "O servidor demorou para responder.",
        SyncProtocolException => "O servidor respondeu de um jeito inesperado.",
        _ => "Erro inesperado na sincronização.",
    };

    private static string ForStatus(HttpStatusCode status) => (int)status switch
    {
        >= 500 => "O servidor está com problemas. O app tenta de novo sozinho.",
        413 => "Um registro passou do tamanho aceito pelo servidor.",
        403 => "A conta chegou ao limite de registros do servidor.",
        429 => "Muitos pedidos ao servidor. O app tenta de novo daqui a pouco.",
        var code => $"O servidor recusou a sincronização (código {code}).",
    };
}
