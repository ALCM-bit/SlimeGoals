using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SlimeGoals.Server.Accounts;
using SlimeGoals.Server.Limits;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class EmailDispatchTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/auth/register", TestAccount.NewRegisterRequest(email), SyncJsonContext.Default.RegisterRequest);

    private static WebApplicationFactory<Program> WithSender(ServerFactory factory, IEmailSender sender, ConfirmationEmailQueue? queue = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton(sender));
            if (queue is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(queue));
            }
        }));

    [Fact]
    public async Task Register_responds_before_email_is_sent()
    {
        await using var factory = new ServerFactory(postgres);
        var sender = new BlockingSender();
        await using var server = WithSender(factory, sender);
        using var client = server.CreateClient();

        var response = await RegisterAsync(client, "ana@exemplo.com").WaitAsync(TimeSpan.FromSeconds(10));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        sender.Finished.Should().Be(0);
        await sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        sender.Release.SetResult();
        await server.WaitForEmailQueueAsync();
        sender.Finished.Should().Be(1);
        (await CountAccountsAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Failed_send_after_retries_deletes_pending_account()
    {
        await using var factory = new ServerFactory(postgres);
        var sender = new ThrowingSender();
        await using var server = WithSender(factory, sender);
        using var client = server.CreateClient();

        (await RegisterAsync(client, "ana@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await server.WaitForEmailQueueAsync();

        sender.Attempts.Should().Be(3);
        (await CountAccountsAsync(factory)).Should().Be(0);
        var logs = string.Join('\n', factory.Logs.Lines);
        logs.Should().Contain(RateLimits.MaskEmail("ana@exemplo.com"));
        logs.Should().NotContain("ana@exemplo.com");
        logs.Should().NotContain(sender.LastToken);

        sender.Fail = false;
        (await RegisterAsync(client, "ana@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await server.WaitForEmailQueueAsync();
        (await CountAccountsAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Full_queue_deletes_pending_account_and_still_answers_202()
    {
        await using var factory = new ServerFactory(postgres);
        var sender = new BlockingSender();
        await using var server = WithSender(factory, sender, new ConfirmationEmailQueue(capacity: 1));
        using var client = server.CreateClient();

        var first = await RegisterAsync(client, "ana@exemplo.com").WaitAsync(TimeSpan.FromSeconds(10));
        var second = await RegisterAsync(client, "bia@exemplo.com").WaitAsync(TimeSpan.FromSeconds(10));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await CountAccountsAsync(factory, "bia@exemplo.com")).Should().Be(0);
        (await CountAccountsAsync(factory, "ana@exemplo.com")).Should().Be(1);
        string.Join('\n', factory.Logs.Lines).Should().NotContain("bia@exemplo.com");
        sender.Release.SetResult();
        await server.WaitForEmailQueueAsync();
        sender.Finished.Should().Be(1);
    }

    private static async Task<int> CountAccountsAsync(ServerFactory factory, string? email = null)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM accounts WHERE @email::text IS NULL OR email = @email", new { email });
    }

    /// <summary>Só termina o envio quando o teste solta <see cref="Release"/>.</summary>
    private sealed class BlockingSender : IEmailSender
    {
        private int _finished;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Finished => Volatile.Read(ref _finished);

        public async Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            Interlocked.Increment(ref _finished);
        }
    }

    /// <summary>Lança em todo envio enquanto <see cref="Fail"/> estiver ligado.</summary>
    private sealed class ThrowingSender : IEmailSender
    {
        private int _attempts;

        public bool Fail { get; set; } = true;

        public int Attempts => Volatile.Read(ref _attempts);

        public string LastToken { get; private set; } = "";

        public Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken)
        {
            LastToken = token;
            if (!Fail)
            {
                return Task.CompletedTask;
            }

            Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException($"falha simulada para {email}");
        }
    }
}
