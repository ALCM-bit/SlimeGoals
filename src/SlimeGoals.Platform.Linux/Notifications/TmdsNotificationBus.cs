using System.Diagnostics;
using Tmds.DBus.Protocol;
using SlimeGoals.Core.Diagnostics;

namespace SlimeGoals.Platform.Linux.Notifications;

/// <summary>
/// <see cref="INotificationBus"/> sobre Tmds.DBus.Protocol. Uma única conexão de sessão, criada no primeiro uso
/// (o GNOME só entrega os sinais à conexão que enviou a notificação). Sem lógica além de marshalling.
/// </summary>
public sealed class TmdsNotificationBus : INotificationBus
{
    private const string Service = "org.freedesktop.Notifications";
    private const string Path = "/org/freedesktop/Notifications";

    private readonly SemaphoreSlim _connectLock = new(1);
    private DBusConnection? _connection;
    private readonly List<IDisposable> _watches = [];

    /// <inheritdoc />
    public event Action<uint, string>? ActionInvoked;

    /// <inheritdoc />
    public event Action<uint, uint>? NotificationClosed;

    /// <inheritdoc />
    public async Task<uint> NotifyAsync(string summary, string body, IReadOnlyList<(string Key, string Label)> actions,
        CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(ct).ConfigureAwait(false);
        var message = BuildNotify(connection, summary, body, actions);
        return await connection.CallMethodAsync(message, (m, _) => m.GetBodyReader().ReadUInt32(), null)
            .ConfigureAwait(false);
    }

    private static MessageBuffer BuildNotify(DBusConnection connection, string summary, string body,
        IReadOnlyList<(string Key, string Label)> actions)
    {
        using var w = connection.GetMessageWriter();
        w.WriteMethodCallHeader(Service, Path, Service, "Notify", "susssasa{sv}i", MessageFlags.None);
        w.WriteString("SlimeGoals");
        w.WriteUInt32(0);
        w.WriteString("");
        w.WriteString(summary);
        w.WriteString(body);
        w.WriteArray(actions.SelectMany(a => new[] { a.Key, a.Label }).ToArray());
        w.WriteDictionary(new Dictionary<string, VariantValue>
        {
            ["desktop-entry"] = VariantValue.String("slimegoals"),
            ["urgency"] = VariantValue.Byte(1),
        });
        w.WriteInt32(-1);
        return w.CreateMessage();
    }

    /// <inheritdoc />
    public async Task<bool> SupportsBodyMarkupAsync(CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(ct).ConfigureAwait(false);
        var capabilities = await connection.CallMethodAsync(BuildGetCapabilities(connection),
            (m, _) => m.GetBodyReader().ReadArrayOfString(), null).ConfigureAwait(false);
        return capabilities.Contains("body-markup");
    }

    private static MessageBuffer BuildGetCapabilities(DBusConnection connection)
    {
        using var w = connection.GetMessageWriter();
        w.WriteMethodCallHeader(Service, Path, Service, "GetCapabilities", null, MessageFlags.None);
        return w.CreateMessage();
    }

    /// <inheritdoc />
    public async Task CloseAsync(uint id, CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(ct).ConfigureAwait(false);
        await connection.CallMethodAsync(BuildClose(connection, id)).ConfigureAwait(false);
    }

    private static MessageBuffer BuildClose(DBusConnection connection, uint id)
    {
        using var w = connection.GetMessageWriter();
        w.WriteMethodCallHeader(Service, Path, Service, "CloseNotification", "u", MessageFlags.None);
        w.WriteUInt32(id);
        return w.CreateMessage();
    }

    private async Task<DBusConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is { } existing) return existing;
        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_connection is { } ready) return ready;
            var connection = new DBusConnection(DBusAddress.Session!);
            try
            {
                await connection.ConnectAsync().ConfigureAwait(false);
                _watches.Add(await Watch(connection, "ActionInvoked",
                    m => { var r = m.GetBodyReader(); return (r.ReadUInt32(), r.ReadString()); },
                    v => ActionInvoked?.Invoke(v.Item1, v.Item2)).ConfigureAwait(false));
                _watches.Add(await Watch(connection, "NotificationClosed",
                    m => { var r = m.GetBodyReader(); return (r.ReadUInt32(), r.ReadUInt32()); },
                    v => NotificationClosed?.Invoke(v.Item1, v.Item2)).ConfigureAwait(false));
            }
            catch
            {
                connection.Dispose();
                _watches.Clear();
                throw;
            }
            _connection = connection;
            return connection;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private static async Task<IDisposable> Watch<T>(DBusConnection connection, string member, Func<Message, T> read,
        Action<T> onSignal) =>
        await connection.WatchSignalAsync<T>(Service, Path, Service, member, (m, _) => read(m),
            n =>
            {
                try
                {
                    if (n.HasValue) onSignal(n.Value!);
                    else if (n.CompletionReason is { } reason) Trace.TraceError($"Sinal {member} encerrado: {reason}");
                }
                catch (Exception e)
                {
                    Trace.TraceError($"Falha ao tratar o sinal {member}: {TraceText.Of(e)}");
                }
            }, ObserverFlags.None, false, null).ConfigureAwait(false);

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var watch in _watches) watch.Dispose();
        _connection?.Dispose();
        _connectLock.Dispose();
    }
}
