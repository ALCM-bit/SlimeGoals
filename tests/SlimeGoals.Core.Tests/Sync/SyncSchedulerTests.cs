using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Sync;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Sync;

public class SyncSchedulerTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
    private readonly DataChangeNotifier _changes = new();
    private readonly Queue<SyncCycleResult> _results = new();
    private int _runs;
    private Func<CancellationToken, Task<SyncCycleResult>>? _override;

    private SyncScheduler Create() => new(async ct =>
    {
        _runs++;
        if (_override is { } custom)
            return await custom(ct);
        return _results.Count > 0 ? _results.Dequeue() : SyncCycleResult.Done;
    }, _changes, _time);

    [Fact]
    public void Start_runs_a_cycle()
    {
        using var scheduler = Create();
        scheduler.Start();
        _runs.Should().Be(1);
    }

    [Fact]
    public void Change_runs_five_seconds_after_the_last_one()
    {
        using var scheduler = Create();
        scheduler.Start();
        _changes.NotifyChanged();
        _time.Advance(TimeSpan.FromSeconds(3));
        _changes.NotifyChanged();
        _time.Advance(TimeSpan.FromSeconds(4));
        _runs.Should().Be(1);
        _time.Advance(TimeSpan.FromSeconds(1));
        _runs.Should().Be(2);
    }

    [Fact]
    public void Runs_every_fifteen_minutes()
    {
        using var scheduler = Create();
        scheduler.Start();
        _time.Advance(TimeSpan.FromMinutes(14));
        _runs.Should().Be(1);
        _time.Advance(TimeSpan.FromMinutes(1));
        _runs.Should().Be(2);
        _time.Advance(TimeSpan.FromMinutes(15));
        _runs.Should().Be(3);
    }

    [Fact]
    public void RequestNow_runs_at_once()
    {
        using var scheduler = Create();
        scheduler.Start();
        scheduler.RequestNow();
        _runs.Should().Be(2);
    }

    [Fact]
    public void Failure_retries_in_30_60_120_seconds_up_to_15_minutes()
    {
        for (var i = 0; i < 12; i++)
            _results.Enqueue(SyncCycleResult.Failed);
        using var scheduler = Create();
        scheduler.Start();

        var expected = new[] { 30, 60, 120, 240, 480, 900, 900 };
        var runs = 1;
        foreach (var seconds in expected)
        {
            _time.Advance(TimeSpan.FromSeconds(seconds - 1));
            _runs.Should().Be(runs);
            _time.Advance(TimeSpan.FromSeconds(1));
            _runs.Should().Be(++runs);
        }
    }

    [Fact]
    public void Success_resets_the_wait()
    {
        _results.Enqueue(SyncCycleResult.Failed);
        _results.Enqueue(SyncCycleResult.Failed);
        _results.Enqueue(SyncCycleResult.Done);
        using var scheduler = Create();
        scheduler.Start();
        _time.Advance(TimeSpan.FromSeconds(30));
        _time.Advance(TimeSpan.FromSeconds(60));
        _runs.Should().Be(3);

        _time.Advance(SyncScheduler.Period);
        _runs.Should().Be(4);

        _results.Enqueue(SyncCycleResult.Failed);
        _time.Advance(SyncScheduler.Period);
        _runs.Should().Be(5);
        _time.Advance(TimeSpan.FromSeconds(30));
        _runs.Should().Be(6);
    }

    [Fact]
    public void Paused_waits_for_the_period()
    {
        _results.Enqueue(SyncCycleResult.Paused);
        using var scheduler = Create();
        scheduler.Start();
        _time.Advance(TimeSpan.FromMinutes(14));
        _runs.Should().Be(1);
        _time.Advance(TimeSpan.FromMinutes(1));
        _runs.Should().Be(2);
    }

    [Fact]
    public void Stop_cancels_timers_and_ignores_changes()
    {
        CancellationToken seen = default;
        var gate = new TaskCompletionSource<SyncCycleResult>();
        _override = ct =>
        {
            seen = ct;
            return gate.Task;
        };
        using var scheduler = Create();
        scheduler.Start();
        scheduler.Stop();
        seen.IsCancellationRequested.Should().BeTrue();

        gate.SetCanceled(seen);
        _changes.NotifyChanged();
        scheduler.RequestNow();
        _time.Advance(TimeSpan.FromHours(1));
        _runs.Should().Be(1);
    }

    [Fact]
    public void Failing_cycle_never_throws()
    {
        var attempts = 0;
        _override = _ => ++attempts == 1 ? throw new InvalidOperationException("boom") : Task.FromResult(SyncCycleResult.Done);
        using var scheduler = Create();
        scheduler.Start();
        _time.Advance(TimeSpan.FromSeconds(30));
        _runs.Should().Be(2);
    }

    [Fact]
    public void Failing_cycle_traces_only_the_exception_type()
    {
        using var trace = new TraceCapture();
        _override = _ => throw new InvalidOperationException("detalhe-secreto-do-ciclo", new IOException("interna-secreta"));
        using var scheduler = Create();
        scheduler.Start();
        trace.Messages.Should().Contain(m => m.Contains("Ciclo de sincronização falhou: InvalidOperationException"));
        trace.Messages.Should().NotContain(m => m.Contains("detalhe-secreto-do-ciclo") || m.Contains("interna-secreta"));
    }

    [Fact]
    public void Failing_cycle_traces_the_http_status()
    {
        using var trace = new TraceCapture();
        _override = _ => throw new HttpRequestException("corpo-cru-agendador", null, System.Net.HttpStatusCode.BadGateway);
        using var scheduler = Create();
        scheduler.Start();
        trace.Messages.Should().Contain(m => m.Contains("Ciclo de sincronização falhou: HttpRequestException (HTTP 502)"));
        trace.Messages.Should().NotContain(m => m.Contains("corpo-cru-agendador"));
    }

    [Fact]
    public void Change_during_a_cycle_schedules_one_more_after_it_ends()
    {
        var gate = new TaskCompletionSource<SyncCycleResult>();
        _override = _ => gate.Task;
        using var scheduler = Create();
        scheduler.Start();
        _changes.NotifyChanged();
        _time.Advance(TimeSpan.FromSeconds(10));
        _runs.Should().Be(1);

        _override = _ => Task.FromResult(SyncCycleResult.Done);
        gate.SetResult(SyncCycleResult.Done);
        _runs.Should().Be(1);
        _time.Advance(TimeSpan.FromSeconds(5));
        _runs.Should().Be(2);
    }

    [Fact]
    public void Change_during_the_failure_wait_shortens_it_and_keeps_the_count()
    {
        _results.Enqueue(SyncCycleResult.Failed);
        _results.Enqueue(SyncCycleResult.Failed);
        using var scheduler = Create();
        scheduler.Start();
        _time.Advance(TimeSpan.FromSeconds(10));
        _changes.NotifyChanged();
        _time.Advance(TimeSpan.FromSeconds(4));
        _runs.Should().Be(1);
        _time.Advance(TimeSpan.FromSeconds(1));
        _runs.Should().Be(2);

        _time.Advance(TimeSpan.FromSeconds(59));
        _runs.Should().Be(2);
        _time.Advance(TimeSpan.FromSeconds(1));
        _runs.Should().Be(3);
    }

    [Fact]
    public void RequestNow_during_a_cycle_runs_again_when_it_ends()
    {
        var gate = new TaskCompletionSource<SyncCycleResult>();
        _override = _ => gate.Task;
        using var scheduler = Create();
        scheduler.Start();
        scheduler.RequestNow();
        _runs.Should().Be(1);

        _override = _ => Task.FromResult(SyncCycleResult.Done);
        gate.SetResult(SyncCycleResult.Done);
        _runs.Should().Be(2);
    }
}
