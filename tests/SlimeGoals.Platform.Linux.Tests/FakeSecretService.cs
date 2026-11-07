using Tmds.DBus.Protocol;

namespace SlimeGoals.Platform.Linux.Tests;

/// <summary>
/// Secret Service mínimo, num barramento privado, para o caminho que o <c>gnome-keyring-daemon</c> de teste não
/// cobre: coleção travada que a pessoa destrava no prompt. <c>Unlock</c> devolve um caminho de prompt, e
/// <c>Prompt</c> responde e emite <c>Completed</c> (destravando ou recusando, conforme <see cref="DismissPrompt"/>).
/// A coleção não tem itens.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class FakeSecretService : IPathMethodHandler, IDisposable
{
    public const string Collection = "/org/freedesktop/secrets/collection/fake";
    private const string PromptPath = "/org/freedesktop/secrets/prompt/p1";
    private const string SessionPath = "/org/freedesktop/secrets/session/s1";

    private readonly DBusConnection _connection;
    private readonly List<string> _calls = [];

    private FakeSecretService(DBusConnection connection) => _connection = connection;

    public bool Locked { get; private set; } = true;

    public bool DismissPrompt { get; init; }

    public IReadOnlyList<string> Calls
    {
        get { lock (_calls) return _calls.ToArray(); }
    }

    public static async Task<FakeSecretService> StartAsync(IsolatedKeyring bus, bool dismissPrompt = false)
    {
        var connection = await bus.ConnectAsync();
        var fake = new FakeSecretService(connection) { DismissPrompt = dismissPrompt };
        connection.AddMethodHandler(fake);
        await connection.RequestNameAsync(IsolatedKeyring.Service);
        return fake;
    }

    public string Path => IsolatedKeyring.ServicePath;

    public bool HandlesChildPaths => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var request = context.Request;
        var member = request.MemberAsString ?? "";
        lock (_calls) _calls.Add(member);
        switch ((request.InterfaceAsString, member))
        {
            case ("org.freedesktop.Secret.Service", "OpenSession"):
                Reply(context, "vo", (ref MessageWriter w) =>
                {
                    w.WriteVariantString("");
                    w.WriteObjectPath(SessionPath);
                });
                break;
            case ("org.freedesktop.Secret.Service", "ReadAlias"):
                Reply(context, "o", (ref MessageWriter w) => w.WriteObjectPath(Collection));
                break;
            case ("org.freedesktop.Secret.Service", "Unlock"):
                Reply(context, "aoo", (ref MessageWriter w) =>
                {
                    w.WriteArray(Locked ? Array.Empty<ObjectPath>() : [new ObjectPath(Collection)]);
                    w.WriteObjectPath(Locked ? PromptPath : "/");
                });
                break;
            case ("org.freedesktop.Secret.Prompt", "Prompt"):
                Reply(context, null, null);
                if (!DismissPrompt) Locked = false;
                EmitCompleted(DismissPrompt);
                break;
            case ("org.freedesktop.Secret.Collection", "SearchItems"):
                Reply(context, "ao", (ref MessageWriter w) => w.WriteArray(Array.Empty<ObjectPath>()));
                break;
            case ("org.freedesktop.Secret.Session", "Close"):
                Reply(context, null, null);
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
        return ValueTask.CompletedTask;
    }

    private static void Reply(MethodContext context, string? signature, IsolatedKeyring.BodyWriter? body)
    {
        var w = context.CreateReplyWriter(signature);
        try
        {
            body?.Invoke(ref w);
            context.Reply(w.CreateMessage());
        }
        finally
        {
            w.Dispose();
        }
    }

    private void EmitCompleted(bool dismissed)
    {
        var w = _connection.GetMessageWriter();
        try
        {
            w.WriteSignalHeader(null, PromptPath, "org.freedesktop.Secret.Prompt", "Completed", "bv");
            w.WriteBool(dismissed);
            w.WriteSignature("ao");
            w.WriteArray(dismissed ? Array.Empty<ObjectPath>() : [new ObjectPath(Collection)]);
            _connection.TrySendMessage(w.CreateMessage());
        }
        finally
        {
            w.Dispose();
        }
    }

    public void Dispose() => _connection.Dispose();
}
