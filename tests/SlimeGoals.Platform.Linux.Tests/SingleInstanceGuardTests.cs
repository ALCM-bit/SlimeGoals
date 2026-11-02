using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using SlimeGoals.Platform.Linux.SingleInstance;

namespace SlimeGoals.Platform.Linux.Tests;

public sealed class SingleInstanceGuardTests : IDisposable
{
    private const string Key = "/home/ana/.local/share/SlimeGoals/slimegoals.db";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("sg");
    private string Dir => Path.Combine(_temp.FullName, "run");

    public void Dispose()
    {
        try { _temp.Delete(recursive: true); } catch (IOException) { }
    }

    private static string SocketPath(string dir, string key) =>
        Path.Combine(dir, $"slimegoals-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8]}.sock");

    private static TaskCompletionSource ShowSignal(SingleInstanceGuard guard, Action? onEach = null)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        guard.ShowRequested += (_, _) => { onEach?.Invoke(); tcs.TrySetResult(); };
        return tcs;
    }

    [Fact]
    public void First_acquire_succeeds_second_returns_null()
    {
        using var first = SingleInstanceGuard.TryAcquire(Dir, Key);
        first.Should().NotBeNull();

        using var second = SingleInstanceGuard.TryAcquire(Dir, Key);
        second.Should().BeNull();
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Creates_missing_directory_with_owner_only_permissions()
    {
        using var guard = SingleInstanceGuard.TryAcquire(Dir, Key);

        guard.Should().NotBeNull();
        File.GetUnixFileMode(Dir).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.Exists(SocketPath(Dir, Key)).Should().BeTrue();
    }

    [Fact]
    public async Task Signal_reaches_first_instance_and_returns_true()
    {
        using var guard = SingleInstanceGuard.TryAcquire(Dir, Key)!;
        var count = 0;
        var shown = ShowSignal(guard, () => Interlocked.Increment(ref count));

        SingleInstanceGuard.SignalExisting(Dir, Key).Should().BeTrue();

        await shown.Task.WaitAsync(Wait);
        Volatile.Read(ref count).Should().Be(1);
    }

    [Fact]
    public void Signal_without_instance_returns_false_quickly()
    {
        var watch = Stopwatch.StartNew();

        SingleInstanceGuard.SignalExisting(Dir, Key).Should().BeFalse();

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Dispose_releases_lock_and_next_acquire_succeeds()
    {
        var first = SingleInstanceGuard.TryAcquire(Dir, Key)!;
        first.Dispose();
        File.Exists(SocketPath(Dir, Key)).Should().BeFalse();

        using var next = SingleInstanceGuard.TryAcquire(Dir, Key);
        next.Should().NotBeNull();
        var shown = ShowSignal(next!);
        SingleInstanceGuard.SignalExisting(Dir, Key).Should().BeTrue();
        await shown.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task Orphan_socket_file_is_replaced()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(SocketPath(Dir, Key), []);

        using var guard = SingleInstanceGuard.TryAcquire(Dir, Key);
        guard.Should().NotBeNull();
        var shown = ShowSignal(guard!);

        SingleInstanceGuard.SignalExisting(Dir, Key).Should().BeTrue();
        await shown.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task Different_keys_do_not_conflict()
    {
        using var a = SingleInstanceGuard.TryAcquire(Dir, Key);
        using var b = SingleInstanceGuard.TryAcquire(Dir, "/tmp/outro/slimegoals.db");
        a.Should().NotBeNull();
        b.Should().NotBeNull();

        var aCount = 0;
        a!.ShowRequested += (_, _) => Interlocked.Increment(ref aCount);
        var bShown = ShowSignal(b!);

        SingleInstanceGuard.SignalExisting(Dir, "/tmp/outro/slimegoals.db").Should().BeTrue();
        await bShown.Task.WaitAsync(Wait);
        Volatile.Read(ref aCount).Should().Be(0);
    }

    [Fact]
    public async Task Unknown_line_gets_no_reply()
    {
        using var guard = SingleInstanceGuard.TryAcquire(Dir, Key)!;
        var raised = false;
        guard.ShowRequested += (_, _) => raised = true;

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath(Dir, Key)));
        await client.SendAsync(Encoding.UTF8.GetBytes("hello\n"));

        var buffer = new byte[16];
        var read = await client.ReceiveAsync(buffer.AsMemory()).AsTask().WaitAsync(Wait);

        read.Should().Be(0, "o servidor fecha a conexão sem responder");
        raised.Should().BeFalse();
    }
}
