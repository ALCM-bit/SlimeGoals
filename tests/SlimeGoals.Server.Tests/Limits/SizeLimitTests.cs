using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Npgsql;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Limits;

public sealed class SizeLimitTests(PostgresFixture postgres)
{
    private static Dictionary<string, string?> RealLimits() => new()
    {
        ["Limits:MaxBatchItems"] = "100",
        ["Limits:MaxBlobBytes"] = "65536",
        ["Limits:MaxBodyBytes"] = "10485760",
    };

    private static Task<HttpResponseMessage> PushAsync(HttpClient client, params PushItemDto[] items) =>
        client.PostAsJsonAsync("/sync/push", new PushRequest(items), SyncJsonContext.Default.PushRequest);

    private static async Task ShouldBeTooLargeAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("too_large");
    }

    private static async Task<long> RecordCountAsync(ServerFactory factory)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM records");
    }

    [Fact]
    public async Task Blob_above_64_KiB_answers_413_and_saves_nothing()
    {
        await using var factory = new ServerFactory(postgres, RealLimits());
        var account = await TestAccount.CreateAsync(factory);

        var response = await PushAsync(
            account.Client,
            new PushItemDto("ok", 0, new byte[10], false),
            new PushItemDto("big", 0, new byte[(64 * 1024) + 1], false));

        await ShouldBeTooLargeAsync(response);
        (await RecordCountAsync(factory)).Should().Be(0);
        (await PushAsync(account.Client, new PushItemDto("exact", 0, new byte[64 * 1024], false))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Batch_above_100_items_answers_400()
    {
        await using var factory = new ServerFactory(postgres, RealLimits());
        var account = await TestAccount.CreateAsync(factory);
        var items = Enumerable.Range(0, 101).Select(i => new PushItemDto($"k{i}", 0, [1], false)).ToArray();

        var response = await PushAsync(account.Client, items);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_request");
        (await RecordCountAsync(factory)).Should().Be(0);
        (await PushAsync(account.Client, items[..100])).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Body_above_10_MiB_answers_413()
    {
        await using var factory = new ServerFactory(postgres, RealLimits());
        var account = await TestAccount.CreateAsync(factory);
        var body = new string(' ', (10 * 1024 * 1024) + 1);

        var response = await account.Client.PostAsync("/sync/push", new StringContent(body, Encoding.UTF8, "application/json"));

        await ShouldBeTooLargeAsync(response);
    }

    [Fact]
    public async Task Body_above_the_limit_without_content_length_answers_413()
    {
        await using var factory = new ServerFactory(postgres, RealLimits());
        var account = await TestAccount.CreateAsync(factory);
        var content = new StreamContent(new MemoryStream(Enumerable.Repeat((byte)' ', (10 * 1024 * 1024) + 1).ToArray()));
        content.Headers.ContentType = new("application/json");
        content.Headers.ContentLength = null;
        var request = new HttpRequestMessage(HttpMethod.Post, "/sync/push") { Content = content };
        request.Headers.TransferEncodingChunked = true;

        await ShouldBeTooLargeAsync(await account.Client.SendAsync(request));
    }

    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/auth/register")]
    [InlineData("/auth/prelogin")]
    public async Task Auth_body_above_64_KiB_answers_413(string path)
    {
        await using var factory = new ServerFactory(postgres, RealLimits());
        using var client = factory.CreateClient();
        var body = "{\"email\":\"" + new string('a', 64 * 1024) + "\"}";

        var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

        await ShouldBeTooLargeAsync(response);
    }
}
