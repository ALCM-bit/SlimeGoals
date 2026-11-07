using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Limits;

public sealed class QuotaTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> PushAsync(HttpClient client, params PushItemDto[] items) =>
        client.PostAsJsonAsync("/sync/push", new PushRequest(items), SyncJsonContext.Default.PushRequest);

    private static PushItemDto Upsert(string key, long baseSeq = 0) => new(key, baseSeq, [1, 2, 3], false);

    private static async Task<long> RecordCountAsync(ServerFactory factory)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM records");
    }

    private static Dictionary<string, string?> Quota(string value) => new() { ["Sync:MaxRecordsPerAccount"] = value };

    [Fact]
    public async Task Quota_is_off_by_default()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        var response = await PushAsync(account.Client, Enumerable.Range(0, 50).Select(i => Upsert($"k{i}")).ToArray());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RecordCountAsync(factory)).Should().Be(50);
    }

    [Fact]
    public async Task Batch_past_the_quota_answers_403_and_saves_nothing()
    {
        await using var factory = new ServerFactory(postgres, Quota("3"));
        var account = await TestAccount.CreateAsync(factory);
        (await PushAsync(account.Client, Upsert("a"), Upsert("b"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PushAsync(account.Client, Upsert("c"), Upsert("d"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("quota_exceeded");
        (await RecordCountAsync(factory)).Should().Be(2);
        (await PushAsync(account.Client, Upsert("c"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Edit_at_the_quota_is_accepted()
    {
        await using var factory = new ServerFactory(postgres, Quota("2"));
        var account = await TestAccount.CreateAsync(factory);
        (await PushAsync(account.Client, Upsert("a"), Upsert("b"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var edit = await PushAsync(account.Client, new PushItemDto("a", 1, [9], false), new PushItemDto("b", 2, null, true));

        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PushAsync(account.Client, Upsert("c"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Quota_is_counted_per_account()
    {
        await using var factory = new ServerFactory(postgres, Quota("1"));
        var first = await TestAccount.CreateAsync(factory);
        var second = await TestAccount.CreateAsync(factory);

        (await PushAsync(first.Client, Upsert("a"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PushAsync(second.Client, Upsert("a"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
