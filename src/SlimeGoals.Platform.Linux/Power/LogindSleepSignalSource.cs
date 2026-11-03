using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace SlimeGoals.Platform.Linux.Power;

/// <summary>
/// <see cref="ISleepSignalSource"/> sobre o barramento do sistema: org.freedesktop.login1,
/// /org/freedesktop/login1, interface Manager, sinal PrepareForSleep(b).
/// </summary>
public sealed class LogindSleepSignalSource : ISleepSignalSource
{
    private const string Service = "org.freedesktop.login1";
    private const string Path = "/org/freedesktop/login1";
    private const string Interface = "org.freedesktop.login1.Manager";
    private const string Member = "PrepareForSleep";

    private DBusConnection? _connection;
    private IDisposable? _watch;

    public async Task StartAsync(Action<bool> onPrepareForSleep, CancellationToken ct = default)
    {
        var address = DBusAddress.System
                      ?? throw new InvalidOperationException("Sem barramento do sistema (D-Bus).");
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            _watch = await connection.WatchSignalAsync<bool>(Service, Path, Interface, Member,
                (m, _) => m.GetBodyReader().ReadBool(),
                n =>
                {
                    try
                    {
                        if (n.HasValue) onPrepareForSleep(n.Value);
                        else if (n.CompletionReason is { } reason)
                            Trace.TraceError($"Sinal {Member} encerrado: {reason}");
                    }
                    catch (Exception e)
                    {
                        Trace.TraceError($"Falha ao tratar o sinal {Member}: {e}");
                    }
                }, ObserverFlags.None, false, null).ConfigureAwait(false);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
        _connection = connection;
    }

    public ValueTask DisposeAsync()
    {
        _watch?.Dispose();
        _connection?.Dispose();
        return ValueTask.CompletedTask;
    }
}
