using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class SettingsRepositoryTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private SettingsRepository Repo => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Missing_key_returns_null()
    {
        (await Repo.GetAsync("reminders.paused")).Should().BeNull();
    }

    [Fact]
    public async Task Set_then_get_round_trips()
    {
        await Repo.SetAsync("reminders.paused", "1");

        (await Repo.GetAsync("reminders.paused")).Should().Be("1");
    }

    [Fact]
    public async Task Set_twice_replaces_value()
    {
        await Repo.SetAsync("reminders.resumed_at", "2026-11-02T12:00:00.0000000Z");
        await Repo.SetAsync("reminders.resumed_at", "2026-11-02T15:30:00.0000000Z");

        (await Repo.GetAsync("reminders.resumed_at")).Should().Be("2026-11-02T15:30:00.0000000Z");
    }

    [Fact]
    public async Task Remove_deletes_and_missing_remove_is_silent()
    {
        await Repo.SetAsync("reminders.paused", "1");

        await Repo.RemoveAsync("reminders.paused");
        (await Repo.GetAsync("reminders.paused")).Should().BeNull();

        var act = async () => await Repo.RemoveAsync("reminders.paused");
        await act.Should().NotThrowAsync();
    }
}
