using System.Diagnostics;

namespace SlimeGoals.Testing;

/// <summary>Guarda o que passa pelo <see cref="Trace"/> enquanto existir. O Trace é global: confira por conteúdo.</summary>
public sealed class TraceCapture : TraceListener
{
    private readonly List<string> _messages = [];

    public TraceCapture() => Trace.Listeners.Add(this);

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
                return _messages.ToList();
        }
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        lock (_messages)
            _messages.Add(message ?? "");
    }

    protected override void Dispose(bool disposing)
    {
        Trace.Listeners.Remove(this);
        base.Dispose(disposing);
    }
}
