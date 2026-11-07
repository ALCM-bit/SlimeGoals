using System.Net;
using System.Text;
using System.Text.Json;
using SlimeGoals.Core.Sync;
using SlimeGoals.Sync;

namespace SlimeGoals.Server.Tests.Sync;

public sealed class HttpSyncTransportTests(PostgresFixture postgres)
{
    private const string EmptyPull = """{"records":[],"has_more":false}""";
    private const string EmptyPush = """{"accepted":[],"conflicts":[]}""";

    /// <summary>Responde sempre o mesmo e guarda o pedido, com o corpo já lido.</summary>
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static HttpSyncTransport Transport(StubHandler handler, string? token = "abc") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://servidor.test/") }, _ => ValueTask.FromResult(token));

    private static HttpSyncTransport Transport(HttpStatusCode status, string body) => Transport(new StubHandler(status, body));

    [Theory]
    [InlineData(5L, 10, false, "after=5&limit=10&full=0")]
    [InlineData(0L, 200, true, "after=0&limit=200&full=1")]
    public async Task Pull_sends_the_query_and_reads_the_page(long after, int limit, bool full, string query)
    {
        var handler = new StubHandler(
            HttpStatusCode.OK, """{"records":[{"key":"a","seq":6,"blob":"AQID"},{"key":"b","seq":7,"blob":null}],"has_more":true}""");

        var page = await Transport(handler).PullAsync(after, limit, full);

        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/sync/pull");
        handler.Request.RequestUri.Query.Should().Be("?" + query);
        page.HasMore.Should().BeTrue();
        page.Records.Should().HaveCount(2);
        page.Records[0].Should().BeEquivalentTo(new RemoteRecord("a", 6, [1, 2, 3]));
        page.Records[1].Should().BeEquivalentTo(new RemoteRecord("b", 7, null));
    }

    [Fact]
    public async Task Null_blob_is_sent_as_delete_and_a_blob_is_not()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"accepted":[{"key":"a","seq":1}],"conflicts":["b"]}""");

        var result = await Transport(handler).PushAsync([new PushItem("a", 3, [1, 2, 3]), new PushItem("b", 4, null)]);

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/sync/push");
        using var json = JsonDocument.Parse(handler.RequestBody!);
        var items = json.RootElement.GetProperty("items");
        items[0].GetProperty("record_key").GetString().Should().Be("a");
        items[0].GetProperty("base_seq").GetInt64().Should().Be(3);
        items[0].GetProperty("blob").GetString().Should().Be("AQID");
        items[0].GetProperty("delete").GetBoolean().Should().BeFalse();
        items[1].GetProperty("blob").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetProperty("delete").GetBoolean().Should().BeTrue();
        result.Accepted.Should().Equal(new PushAccepted("a", 1));
        result.Conflicts.Should().Equal("b");
    }

    [Fact]
    public async Task Sends_the_token_of_the_delegate_on_each_request()
    {
        var handler = new StubHandler(HttpStatusCode.OK, EmptyPull);
        var token = "primeiro";
        var transport = new HttpSyncTransport(
            new HttpClient(handler) { BaseAddress = new Uri("http://servidor.test/") }, _ => ValueTask.FromResult<string?>(token));

        await transport.PullAsync(0, 1, false);
        handler.Request!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be("primeiro");

        token = "segundo";
        await transport.PullAsync(0, 1, false);
        handler.Request.Headers.Authorization!.Parameter.Should().Be("segundo");
    }

    [Fact]
    public async Task Null_token_sends_no_header()
    {
        var pull = new StubHandler(HttpStatusCode.OK, EmptyPull);
        var push = new StubHandler(HttpStatusCode.OK, EmptyPush);

        await Transport(pull, token: null).PullAsync(0, 1, false);
        await Transport(push, token: null).PushAsync([]);

        pull.Request!.Headers.Authorization.Should().BeNull();
        push.Request!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Transport_maps_410_to_reset_and_401_to_http_failure()
    {
        var gone = Transport(HttpStatusCode.Gone, """{"error":"reset_required"}""");
        await gone.Invoking(t => t.PullAsync(0, 1, false)).Should().ThrowAsync<SyncResetRequiredException>();
        await gone.Invoking(t => t.PushAsync([])).Should().ThrowAsync<SyncResetRequiredException>();

        await using var factory = new ServerFactory(postgres);
        var real = new HttpSyncTransport(factory.CreateClient(), _ => ValueTask.FromResult<string?>("token-desconhecido"));
        var failure = await real.Invoking(t => t.PullAsync(0, 1, false)).Should().ThrowAsync<HttpRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Real_server_without_token_is_an_http_failure()
    {
        await using var factory = new ServerFactory(postgres);
        var transport = new HttpSyncTransport(factory.CreateClient(), _ => ValueTask.FromResult<string?>(null));

        var failure = await transport.Invoking(t => t.PushAsync([])).Should().ThrowAsync<HttpRequestException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Other_failures_keep_the_status_code(HttpStatusCode status)
    {
        var transport = Transport(status, """{"error":"x"}""");

        (await transport.Invoking(t => t.PullAsync(0, 1, false)).Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(status);
        (await transport.Invoking(t => t.PushAsync([])).Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"records":[]}""")]
    [InlineData("""{"has_more":false}""")]
    [InlineData("""{"records":null,"has_more":false}""")]
    [InlineData("""{"records":[null],"has_more":false}""")]
    [InlineData("""{"records":[{"key":null,"seq":1,"blob":null}],"has_more":false}""")]
    [InlineData("""{"records":[{"key":"a","blob":null}],"has_more":false}""")]
    [InlineData("""{"records":[{"key":"a","seq":1}],"has_more":false}""")]
    [InlineData("""{"records":[{"key":"a","seq":1,"blob":"###"}],"has_more":false}""")]
    public async Task Pull_body_it_cannot_read_is_a_failure(string body)
    {
        var failure = await Transport(HttpStatusCode.OK, body)
            .Invoking(t => t.PullAsync(0, 1, false)).Should().ThrowAsync<HttpRequestException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"accepted":[]}""")]
    [InlineData("""{"conflicts":[]}""")]
    [InlineData("""{"accepted":null,"conflicts":[]}""")]
    [InlineData("""{"accepted":[],"conflicts":null}""")]
    [InlineData("""{"accepted":[null],"conflicts":[]}""")]
    [InlineData("""{"accepted":[{"key":null,"seq":1}],"conflicts":[]}""")]
    [InlineData("""{"accepted":[{"key":"a"}],"conflicts":[]}""")]
    [InlineData("""{"accepted":[],"conflicts":[null]}""")]
    public async Task Push_body_it_cannot_read_is_a_failure(string body)
    {
        var failure = await Transport(HttpStatusCode.OK, body)
            .Invoking(t => t.PushAsync([])).Should().ThrowAsync<HttpRequestException>();

        failure.Which.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancelled_token_is_not_turned_into_a_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Transport(HttpStatusCode.OK, EmptyPull)
            .Invoking(t => t.PullAsync(0, 1, false, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
