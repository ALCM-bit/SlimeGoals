using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class ReminderPauseTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemorySettingsRepository _settings = new();
    private readonly DataChangeNotifier _changes = new();
    private int _notified;

    public ReminderPauseTests() => _changes.Changed += (_, _) => _notified++;

    private ReminderPause Pause => new(_settings, _time, _changes);

    [Fact]
    public async Task Starts_not_paused()
    {
        var state = await Pause.GetAsync();

        state.Should().Be(new PauseState(false, null));
    }

    [Fact]
    public async Task Pause_survives_new_instances()
    {
        await Pause.PauseAsync();

        (await new ReminderPause(_settings, _time).GetAsync()).IsPaused.Should().BeTrue();
    }

    [Fact]
    public async Task Resume_clears_pause_and_records_utc_instant()
    {
        await Pause.PauseAsync();
        await Pause.ResumeAsync();

        var state = await Pause.GetAsync();
        state.IsPaused.Should().BeFalse();
        state.ResumedAt.Should().Be(_time.GetUtcNow());
        (await _settings.GetAsync(ReminderPause.ResumedAtKey)).Should().Be("2026-11-04T12:00:00.0000000Z");
    }

    [Fact]
    public async Task Pause_and_resume_notify_once_each()
    {
        await Pause.PauseAsync();
        await Pause.PauseAsync();
        _notified.Should().Be(1);

        await Pause.ResumeAsync();
        await Pause.ResumeAsync();
        _notified.Should().Be(2);
    }

    [Fact]
    public async Task Unreadable_resumed_at_is_ignored()
    {
        await _settings.SetAsync(ReminderPause.ResumedAtKey, "não é data");

        (await Pause.GetAsync()).ResumedAt.Should().BeNull();
    }
}
