using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Platform.Linux.Scheduling;

namespace SlimeGoals.Platform.Linux.Tests;

public sealed class ProcessFocusAlarmTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly ProcessFocusAlarm _alarm;
    private int _fired;

    public ProcessFocusAlarmTests()
    {
        _alarm = new ProcessFocusAlarm(_time);
        _alarm.Fired += (_, _) => _fired++;
    }

    public void Dispose() => _alarm.Dispose();

    [Fact]
    public void Alarm_fires_at_the_set_instant_and_cancel_stops_it()
    {
        _alarm.Set(T0.AddMinutes(25));

        _time.Advance(TimeSpan.FromMinutes(25) - TimeSpan.FromSeconds(1));
        _fired.Should().Be(0);
        _time.Advance(TimeSpan.FromSeconds(1));
        _fired.Should().Be(1);
        _time.Advance(TimeSpan.FromHours(1));
        _fired.Should().Be(1, "o alarme toca uma vez só");

        _alarm.Set(_time.GetUtcNow().AddMinutes(5));
        _alarm.Cancel();
        _time.Advance(TimeSpan.FromMinutes(10));
        _fired.Should().Be(1);
    }

    [Fact]
    public void Setting_again_replaces_the_armed_instant()
    {
        _alarm.Set(T0.AddMinutes(5));
        _alarm.Set(T0.AddMinutes(10));

        _time.Advance(TimeSpan.FromMinutes(5));
        _fired.Should().Be(0);
        _time.Advance(TimeSpan.FromMinutes(5));
        _fired.Should().Be(1);
    }

    [Fact]
    public void An_instant_in_the_past_fires_right_away()
    {
        _alarm.Set(T0.AddMinutes(-1));
        _time.Advance(TimeSpan.Zero);

        _fired.Should().Be(1);
    }

    [Fact]
    public void Cancel_without_an_alarm_and_after_dispose_do_nothing()
    {
        _alarm.Cancel();
        _alarm.Set(T0.AddMinutes(1));
        _alarm.Dispose();
        _alarm.Set(T0.AddMinutes(2));
        _alarm.Cancel();

        _time.Advance(TimeSpan.FromMinutes(5));
        _fired.Should().Be(0);
    }
}
