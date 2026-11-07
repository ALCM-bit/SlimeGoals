using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using SlimeGoals.Server.Sync;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Sync;

public sealed class RecordPurgerTests(PostgresFixture postgres)
{
    private static readonly byte[] Blob = [1, 2, 3];

    private static async Task PushAsync(TestAccount account, params PushItemDto[] items)
    {
        var response = await account.Client.PostAsJsonAsync("/sync/push", new PushRequest(items), SyncJsonContext.Default.PushRequest);
        response.EnsureSuccessStatusCode();
    }

    private static PushItemDto Upsert(string key) => new(key, 0, Blob, false);

    private static PushItemDto Delete(string key, long baseSeq) => new(key, baseSeq, null, true);

    private static RecordPurger PurgerOf(ServerFactory factory) => factory.Services.GetRequiredService<RecordPurger>();

    private static async Task<long> PurgePointAsync(ServerFactory factory, string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<long>("SELECT purge_point FROM accounts WHERE email = @email", new { email });
    }

    private static async Task<string[]> KeysAsync(ServerFactory factory, string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return (await connection.QueryAsync<string>(
            "SELECT record_key FROM records r JOIN accounts a ON a.id = r.account_id WHERE a.email = @email ORDER BY seq",
            new { email })).ToArray();
    }

    [Fact]
    public async Task Purge_removes_only_marks_older_than_180_days()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await PushAsync(account, Upsert("a"), Upsert("b"));
        await PushAsync(account, Delete("a", 1));
        await PushAsync(account, Upsert("c"));

        factory.Time.Advance(TimeSpan.FromDays(179));
        var early = await PurgerOf(factory).RunAsync(CancellationToken.None);
        var keptAt179 = await KeysAsync(factory, account.Email);
        factory.Time.Advance(TimeSpan.FromDays(2));
        var late = await PurgerOf(factory).RunAsync(CancellationToken.None);

        early.Marks.Should().Be(0);
        keptAt179.Should().Equal("b", "a", "c");
        late.Marks.Should().Be(1);
        (await KeysAsync(factory, account.Email)).Should().Equal("b", "c");
        (await PurgePointAsync(factory, account.Email)).Should().Be(4);
    }

    [Fact]
    public async Task Purge_never_passes_the_highest_seq()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await PushAsync(account, Upsert("a"), Upsert("b"));
        await PushAsync(account, Delete("b", 2));

        factory.Time.Advance(TimeSpan.FromDays(181));
        var result = await PurgerOf(factory).RunAsync(CancellationToken.None);

        result.Marks.Should().Be(0);
        (await PurgePointAsync(factory, account.Email)).Should().Be(3);
        (await KeysAsync(factory, account.Email)).Should().Equal("a", "b");
    }

    [Fact]
    public async Task Purge_point_never_goes_back()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await PushAsync(account, Upsert("a"), Upsert("b"));
        await PushAsync(account, Delete("a", 1));
        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.ExecuteAsync("UPDATE accounts SET purge_point = 100 WHERE email = @email", new { email = account.Email });
        }

        factory.Time.Advance(TimeSpan.FromDays(181));
        await PurgerOf(factory).RunAsync(CancellationToken.None);

        (await PurgePointAsync(factory, account.Email)).Should().Be(100);
    }

    [Fact]
    public async Task Purge_does_not_touch_other_accounts()
    {
        await using var factory = new ServerFactory(postgres);
        var old = await TestAccount.CreateAsync(factory);
        await PushAsync(old, Upsert("a"), Upsert("b"));
        await PushAsync(old, Delete("a", 1));
        await PushAsync(old, Upsert("c"));
        factory.Time.Advance(TimeSpan.FromDays(100));
        var recent = await TestAccount.CreateAsync(factory);
        await PushAsync(recent, Upsert("x"), Upsert("y"));
        await PushAsync(recent, Delete("x", 1));
        await PushAsync(recent, Upsert("z"));

        factory.Time.Advance(TimeSpan.FromDays(81));
        await PurgerOf(factory).RunAsync(CancellationToken.None);

        (await KeysAsync(factory, old.Email)).Should().Equal("b", "c");
        (await PurgePointAsync(factory, old.Email)).Should().Be(4);
        (await KeysAsync(factory, recent.Email)).Should().Equal("y", "x", "z");
        (await PurgePointAsync(factory, recent.Email)).Should().Be(0);
    }

    [Fact]
    public async Task Purge_removes_unconfirmed_accounts_after_24_hours()
    {
        await using var factory = new ServerFactory(postgres);
        var confirmed = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        (await client.PostAsJsonAsync("/auth/register", request, SyncJsonContext.Default.RegisterRequest)).EnsureSuccessStatusCode();

        factory.Time.Advance(TimeSpan.FromHours(23));
        var early = await PurgerOf(factory).RunAsync(CancellationToken.None);
        factory.Time.Advance(TimeSpan.FromHours(2));
        var late = await PurgerOf(factory).RunAsync(CancellationToken.None);
        var again = await client.PostAsJsonAsync("/auth/register", request, SyncJsonContext.Default.RegisterRequest);
        again.EnsureSuccessStatusCode();
        await client.PostAsJsonAsync("/auth/confirm", new ConfirmRequest(factory.Email.LastToken, request.AuthKey), SyncJsonContext.Default.ConfirmRequest);

        early.Accounts.Should().Be(0);
        late.Accounts.Should().Be(1);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM accounts WHERE email = @email", new { email = confirmed.Email }))
            .Should().Be(1);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM accounts WHERE email_confirmed_at IS NOT NULL")).Should().Be(2);
    }

    [Fact]
    public async Task Pull_sees_reset_or_marks_when_a_purge_runs_between_reads()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await PushAsync(account, Upsert("a"), Upsert("b"));
        await PushAsync(account, Delete("a", 1));
        await PushAsync(account, Upsert("c"));
        factory.Time.Advance(TimeSpan.FromDays(181));
        var store = factory.Services.GetRequiredService<RecordStore>();
        store.AfterPointRead = async () =>
        {
            store.AfterPointRead = null;
            await PurgerOf(factory).RunAsync(CancellationToken.None);
        };

        var page = await account.Client.GetAsync("/sync/pull?after=0&full=0");

        (await KeysAsync(factory, account.Email)).Should().Equal("b", "c");
        if (page.StatusCode != System.Net.HttpStatusCode.Gone)
        {
            page.EnsureSuccessStatusCode();
            var body = (await page.Content.ReadFromJsonAsync(SyncJsonContext.Default.PullResponse))!;
            body.Records.Select(r => r.Key).Should().Contain("a");
        }
    }

    [Fact]
    public async Task Maintenance_survives_a_failed_run()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var purger = new FlakyPurger();
        var service = new MaintenanceService(
            purger, Options.Create(new ServerOptions()), time, NullLogger<MaintenanceService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await purger.WaitForRunsAsync(1);
        time.Advance(TimeSpan.FromHours(24));
        await purger.WaitForRunsAsync(2);
        await service.StopAsync(CancellationToken.None);

        purger.Runs.Should().Be(2);
    }

    private sealed class FlakyPurger() : RecordPurger(null!, TimeProvider.System, Options.Create(new ServerOptions()))
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public override Task<PurgeResult> RunAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _runs) == 1)
            {
                throw new NpgsqlException("banco fora do ar");
            }

            return Task.FromResult(new PurgeResult(0, 0));
        }

        public async Task WaitForRunsAsync(int runs)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (Runs < runs)
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }
}
