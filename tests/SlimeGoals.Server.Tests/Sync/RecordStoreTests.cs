using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SlimeGoals.Server.Sync;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Sync;

public sealed class RecordStoreTests(PostgresFixture postgres)
{
    private static readonly byte[] Blob = [1, 2, 3];

    private static PushItemDto Upsert(string key, long baseSeq = 0) => new(key, baseSeq, Blob, false);

    private static PushItemDto Delete(string key, long baseSeq) => new(key, baseSeq, null, true);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, params PushItemDto[] items) =>
        client.PostAsJsonAsync("/sync/push", new PushRequest(items), SyncJsonContext.Default.PushRequest);

    private static Task<HttpResponseMessage> PostRawAsync(HttpClient client, string json) =>
        client.PostAsync("/sync/push", new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<PushResponse> PushAsync(HttpClient client, params PushItemDto[] items)
    {
        var response = await PostAsync(client, items);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.PushResponse))!;
    }

    private static async Task<PullResponse> PullAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/sync/pull" + query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.PullResponse))!;
    }

    private static async Task<long> NextSeqAsync(ServerFactory factory, string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<long>("SELECT next_seq FROM accounts WHERE email = @email", new { email });
    }

    private static async Task ShouldBeInvalidAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_request");
    }

    [Fact]
    public async Task Concurrent_pushes_leave_no_gaps()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        var pushes = Enumerable.Range(0, 8).Select(p =>
            PushAsync(account.Client, Enumerable.Range(0, 25).Select(i => Upsert($"p{p}-{i}")).ToArray()));
        var results = await Task.WhenAll(pushes);

        results.SelectMany(r => r.Accepted).Select(a => a.Seq).Order().Should().Equal(Enumerable.Range(1, 200).Select(i => (long)i));
        results.Should().OnlyContain(r => r.Conflicts.Length == 0);
        (await PullAsync(account.Client, "?limit=500")).Records.Select(r => r.Seq).Should().Equal(Enumerable.Range(1, 200).Select(i => (long)i));
        (await NextSeqAsync(factory, account.Email)).Should().Be(201);
    }

    [Fact]
    public async Task Concurrent_push_of_the_same_key_accepts_one()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PushAsync(account.Client, Upsert("a"))));

        results.SelectMany(r => r.Accepted).Should().Equal(new AcceptedDto("a", 1));
        results.SelectMany(r => r.Conflicts).Should().HaveCount(7).And.OnlyContain(key => key == "a");
        (await NextSeqAsync(factory, account.Email)).Should().Be(2);
    }

    [Fact]
    public async Task Pull_during_pushes_never_skips_a_seq()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var reader = await account.LoginAsync("leitor");

        var pushes = Task.WhenAll(Enumerable.Range(0, 8).Select(async p =>
        {
            for (var i = 0; i < 25; i++)
            {
                await PushAsync(account.Client, Upsert($"p{p}-{i}"));
            }
        }));

        var seen = new List<long>();
        var after = 0L;
        while (true)
        {
            // Olha antes de ler: a leitura que vem depois do fim dos envios é a que pega o resto.
            var finished = pushes.IsCompleted;
            var page = await PullAsync(reader.Client, $"?after={after}&limit=7");
            seen.AddRange(page.Records.Select(r => r.Seq));
            after = seen.Count > 0 ? seen[^1] : 0;
            if (finished && !page.HasMore)
            {
                break;
            }
        }

        await pushes;
        seen.Should().Equal(Enumerable.Range(1, 200).Select(i => (long)i));
    }

    [Fact]
    public async Task Account_never_reads_or_changes_another_account()
    {
        await using var factory = new ServerFactory(postgres);
        var first = await TestAccount.CreateAsync(factory);
        var second = await TestAccount.CreateAsync(factory);
        await PushAsync(first.Client, Upsert("x"), Upsert("a"));

        // A mesma chave na outra conta é outro registro: lá ela não existe, e o seq recomeça.
        var stale = await PushAsync(second.Client, Upsert("a", baseSeq: 2));
        var own = await PushAsync(second.Client, new PushItemDto("a", 0, [9], false));

        stale.Conflicts.Should().Equal("a");
        own.Accepted.Should().Equal(new AcceptedDto("a", 1));
        var firstRecords = (await PullAsync(first.Client)).Records;
        firstRecords.Select(r => r.Key).Should().Equal("x", "a");
        firstRecords[1].Blob.Should().Equal(Blob);
        var secondRecord = (await PullAsync(second.Client)).Records.Should().ContainSingle().Subject;
        secondRecord.Seq.Should().Be(1);
        secondRecord.Blob.Should().Equal(9);

        // Excluir numa conta não toca na outra.
        await PushAsync(second.Client, Delete("a", 1));
        (await PullAsync(first.Client)).Records[1].Blob.Should().Equal(Blob);
    }

    [Fact]
    public async Task Deleted_record_has_no_blob_in_the_database()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await PushAsync(account.Client, Upsert("a"));

        await PushAsync(account.Client, Delete("a", 1));

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        var row = await connection.QuerySingleAsync<(byte[]? Blob, DateTime? DeletedAt)>("SELECT blob, deleted_at FROM records WHERE record_key = 'a'");
        row.Blob.Should().BeNull();
        new DateTimeOffset(row.DeletedAt!.Value).Should().Be(factory.Time.GetUtcNow());
    }

    [Fact]
    public async Task Push_over_a_mark_clears_deleted_at()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await PushAsync(account.Client, Upsert("a"));
        await PushAsync(account.Client, Delete("a", 1));

        await PushAsync(account.Client, Upsert("a", baseSeq: 2));

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        var row = await connection.QuerySingleAsync<(byte[]? Blob, DateTime? DeletedAt)>("SELECT blob, deleted_at FROM records WHERE record_key = 'a'");
        row.Blob.Should().Equal(Blob);
        row.DeletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"record_key":"a","base_seq":0,"blob":"AQID","delete":true}""")]
    [InlineData("""{"record_key":"a","base_seq":0}""")]
    [InlineData("""{"record_key":"a","base_seq":0,"delete":false}""")]
    [InlineData("""{"record_key":"a","base_seq":0,"blob":null,"delete":false}""")]
    public async Task Item_with_blob_and_delete_or_with_neither_answers_400(string item)
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        await ShouldBeInvalidAsync(await PostRawAsync(account.Client, $$"""{"items":[{{item}}]}"""));
    }

    [Theory]
    [InlineData("""{"base_seq":0,"blob":"AQID"}""")]
    [InlineData("""{"record_key":null,"base_seq":0,"blob":"AQID"}""")]
    [InlineData("""{"record_key":"","base_seq":0,"blob":"AQID"}""")]
    [InlineData("""{"record_key":"a\u0000b","base_seq":0,"blob":"AQID"}""")]
    [InlineData("""{"record_key":"a\nb","base_seq":0,"blob":"AQID"}""")]
    [InlineData("""{"record_key":"a\ud800","base_seq":0,"blob":"AQID"}""")]
    [InlineData("""{"record_key":"a","blob":"AQID"}""")]
    [InlineData("""{"record_key":"a","base_seq":null,"blob":"AQID"}""")]
    [InlineData("""{"record_key":"a","base_seq":-1,"blob":"AQID"}""")]
    [InlineData("""{"record_key":"a","base_seq":"0","blob":"AQID"}""")]
    [InlineData("""{"record_key":"a","base_seq":0,"blob":"###"}""")]
    [InlineData("""{"record_key":"a","base_seq":0,"delete":null}""")]
    [InlineData("null")]
    public async Task Invalid_item_rejects_the_whole_batch(string item)
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        await ShouldBeInvalidAsync(await PostRawAsync(account.Client, $$"""{"items":[{"record_key":"ok","base_seq":0,"blob":"AQID"},{{item}}]}"""));

        (await PullAsync(account.Client)).Records.Should().BeEmpty();
        (await NextSeqAsync(factory, account.Email)).Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nem json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"items":null}""")]
    [InlineData("""{"items":{}}""")]
    public async Task Body_without_a_list_of_items_answers_400(string body)
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        await ShouldBeInvalidAsync(await PostRawAsync(account.Client, body));
    }

    [Fact]
    public async Task Record_key_of_128_chars_is_accepted_and_129_is_not()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        var longest = await PushAsync(account.Client, Upsert(new string('k', 128)));
        var tooLong = await PostAsync(account.Client, Upsert(new string('k', 129)));

        longest.Accepted.Should().ContainSingle();
        await ShouldBeInvalidAsync(tooLong);
    }

    [Theory]
    [InlineData("?after=-1")]
    [InlineData("?after=abc")]
    [InlineData("?after=")]
    [InlineData("?after=1.5")]
    [InlineData("?after=1&after=2")]
    [InlineData("?after=99999999999999999999")]
    [InlineData("?limit=0")]
    [InlineData("?limit=-1")]
    [InlineData("?limit=abc")]
    [InlineData("?limit=")]
    [InlineData("?limit=1&limit=2")]
    [InlineData("?full=2")]
    [InlineData("?full=true")]
    [InlineData("?full=")]
    [InlineData("?full=1&full=0")]
    public async Task Pull_with_negative_after_or_zero_limit_answers_400(string query)
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        await ShouldBeInvalidAsync(await account.Client.GetAsync("/sync/pull" + query));
    }

    [Theory]
    [InlineData("?full=0")]
    [InlineData("?full=1")]
    [InlineData("?after=0&limit=1&full=1")]
    public async Task Pull_accepts_the_values_of_full(string query)
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        (await account.Client.GetAsync("/sync/pull" + query)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pull_limit_above_the_maximum_is_reduced()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await PushAsync(account.Client, Enumerable.Range(0, 501).Select(i => Upsert($"k{i}")).ToArray());

        var reduced = await PullAsync(account.Client, "?limit=501");
        var huge = await PullAsync(account.Client, "?limit=99999999999");
        var byDefault = await PullAsync(account.Client);

        reduced.Records.Should().HaveCount(500);
        reduced.HasMore.Should().BeTrue();
        huge.Records.Should().HaveCount(500);
        byDefault.Records.Should().HaveCount(200);
        byDefault.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Sync_routes_require_a_token()
    {
        await using var factory = new ServerFactory(postgres);
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/sync/pull")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(anonymous, Upsert("a"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Push_of_an_account_that_no_longer_exists_gives_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        var store = factory.Services.GetRequiredService<RecordStore>();

        var result = await store.PushAsync(Guid.NewGuid(), [Upsert("a")], CancellationToken.None);

        result.Should().BeNull();
    }
}
