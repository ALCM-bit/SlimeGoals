using Microsoft.Extensions.Options;

namespace SlimeGoals.Server.Sync;

/// <summary>Roda a limpeza ao iniciar e a cada 24 h. Uma falha vai para o log e o laço continua.</summary>
public sealed partial class MaintenanceService(
    RecordPurger purger, IOptions<ServerOptions> options, TimeProvider time, ILogger<MaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Maintenance.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                var result = await purger.RunAsync(stoppingToken);
                LogPurged(result.Marks, result.Accounts);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Limpeza: {Marks} marcas e {Accounts} contas não confirmadas removidas.")]
    private partial void LogPurged(int marks, int accounts);

    [LoggerMessage(Level = LogLevel.Error, Message = "A limpeza diária falhou; tenta de novo no próximo ciclo.")]
    private partial void LogFailed(Exception exception);
}
