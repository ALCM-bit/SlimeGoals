using System.Net;
using SlimeGoals.Core.Sync;

namespace SlimeGoals.Core.Tests.Sync;

public sealed class SyncErrorTextTests
{
    public static TheoryData<Exception, string> Cases => new()
    {
        { new SyncUnauthorizedException(), "A sessão deste aparelho foi encerrada. Entre de novo." },
        { new HttpRequestException("Connection refused"), "Sem conexão com o servidor." },
        { new HttpRequestException(null, null, HttpStatusCode.InternalServerError), "O servidor está com problemas. O app tenta de novo sozinho." },
        { new HttpRequestException(null, null, HttpStatusCode.BadGateway), "O servidor está com problemas. O app tenta de novo sozinho." },
        { new HttpRequestException(null, null, HttpStatusCode.RequestEntityTooLarge), "Um registro passou do tamanho aceito pelo servidor." },
        { new HttpRequestException(null, null, HttpStatusCode.Forbidden), "A conta chegou ao limite de registros do servidor." },
        { new HttpRequestException(null, null, HttpStatusCode.TooManyRequests), "Muitos pedidos ao servidor. O app tenta de novo daqui a pouco." },
        { new HttpRequestException(null, null, HttpStatusCode.BadRequest), "O servidor recusou a sincronização (código 400)." },
        { new HttpRequestException(HttpRequestError.InvalidResponse, "ilegível", null, HttpStatusCode.OK), "O servidor respondeu de um jeito inesperado." },
        { new TimeoutException("segredo"), "O servidor demorou para responder." },
        { new TaskCanceledException("segredo"), "O servidor demorou para responder." },
        { new OperationCanceledException(), "O servidor demorou para responder." },
        { new SyncProtocolException("página que não avança"), "O servidor respondeu de um jeito inesperado." },
        { new InvalidOperationException("segredo"), "Erro inesperado na sincronização." },
        { new SyncResetRequiredException(), "Erro inesperado na sincronização." },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void For_maps_each_failure_to_its_text(Exception e, string expected) =>
        SyncErrorText.For(e).Should().Be(expected);
}
