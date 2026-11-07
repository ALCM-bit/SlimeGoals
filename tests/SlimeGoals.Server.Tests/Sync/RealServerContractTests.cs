using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SlimeGoals.Core.Sync;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Server.Sync;
using SlimeGoals.Sync;

namespace SlimeGoals.Server.Tests.Sync;

public sealed class RealServerContractTests(PostgresFixture postgres) : SyncContractTests, IAsyncDisposable
{
    private readonly ServerFactory _factory = new(postgres);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    protected override async Task PurgeBeforeAsync(ISyncTransport account, long seq)
    {
        // Envelhece as marcas abaixo de seq para além dos 180 dias, e roda a limpeza como a manutenção diária faria.
        await using (var connection = new NpgsqlConnection(_factory.ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE records SET deleted_at = @old WHERE deleted_at IS NOT NULL AND seq < @seq",
                new { old = _factory.Time.GetUtcNow().AddDays(-181), seq });
        }

        await _factory.Services.GetRequiredService<RecordPurger>().RunAsync(CancellationToken.None);
    }

    protected override async Task<ISyncTransport> NewAccountAsync()
    {
        var account = await TestAccount.CreateAsync(_factory);
        return new HttpSyncTransport(_factory.CreateClient(), _ => ValueTask.FromResult<string?>(account.First.Token));
    }
}
