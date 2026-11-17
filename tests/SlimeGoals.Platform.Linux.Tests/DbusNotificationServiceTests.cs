using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Platform.Linux.Notifications;

namespace SlimeGoals.Platform.Linux.Tests;

public sealed class DbusNotificationServiceTests
{
    private static Reminder ReminderWith(string body) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new ReminderText("P&D <título>", body, "Feito"));

    [Fact]
    public async Task Body_is_escaped_when_the_server_reads_markup_and_the_title_stays_plain()
    {
        var bus = new FakeNotificationBus { BodyMarkup = true };
        using var service = new DbusNotificationService(bus);

        await service.ShowAsync(ReminderWith("3 de 5 P&D <b>x</b>"));

        bus.Notified.Single().Body.Should().Be("3 de 5 P&amp;D &lt;b&gt;x&lt;/b&gt;");
        bus.Notified.Single().Summary.Should().Be("P&D <título>");
    }

    [Fact]
    public async Task Body_stays_plain_when_the_server_has_no_markup()
    {
        var bus = new FakeNotificationBus();
        using var service = new DbusNotificationService(bus);

        await service.ShowAsync(ReminderWith("P&D"));

        bus.Notified.Single().Body.Should().Be("P&D");
    }

    [Fact]
    public async Task Capability_failure_still_shows_the_reminder_with_plain_body()
    {
        var bus = new FakeNotificationBus { CapabilitiesFailure = new InvalidOperationException("sem servidor") };
        using var service = new DbusNotificationService(bus);

        await service.ShowAsync(ReminderWith("P&D"));

        bus.Notified.Single().Body.Should().Be("P&D");
    }

    [Fact]
    public async Task Capabilities_are_asked_only_once()
    {
        var bus = new FakeNotificationBus { BodyMarkup = true };
        using var service = new DbusNotificationService(bus);

        await service.ShowAsync(ReminderWith("a"));
        await service.ShowAsync(ReminderWith("b"));

        bus.CapabilityQueries.Should().Be(1);
    }

    private static Reminder NewReminder() =>
        new(Guid.NewGuid(), Guid.NewGuid(), new ReminderText("Título", "Corpo", "Feito"));

    private static (FakeNotificationBus Bus, DbusNotificationService Service, List<ReminderActionEventArgs> Received) Create()
    {
        var bus = new FakeNotificationBus();
        var service = new DbusNotificationService(bus);
        var received = new List<ReminderActionEventArgs>();
        service.ActionReceived += (_, e) => received.Add(e);
        return (bus, service, received);
    }

    [Fact]
    public async Task Show_sends_actions_in_order_with_labels()
    {
        var (bus, service, _) = Create();
        var reminder = new Reminder(Guid.NewGuid(), Guid.NewGuid(), new ReminderText("T", "B", "+1"));

        await service.ShowAsync(reminder);

        var call = bus.Notified.Should().ContainSingle().Subject;
        call.Summary.Should().Be("T");
        call.Body.Should().Be("B");
        call.Actions.Should().Equal(("primary", "+1"), ("snooze", "Adiar"), ("ignore", "Ignorar"), ("default", "Abrir"));
        (call.Critical, call.SoundName).Should().Be((false, (string?)null), "lembrete não é despertador");
    }

    [Theory]
    [InlineData("primary", ReminderAction.Primary)]
    [InlineData("snooze", ReminderAction.Snooze)]
    [InlineData("ignore", ReminderAction.Ignore)]
    [InlineData("default", ReminderAction.Open)]
    public async Task Action_maps_to_reminder_action_with_log_id(string key, ReminderAction expected)
    {
        var (bus, service, received) = Create();
        var reminder = NewReminder();
        await service.ShowAsync(reminder);

        bus.RaiseAction(1, key);

        var e = received.Should().ContainSingle().Subject;
        e.LogId.Should().Be(reminder.LogId);
        e.Action.Should().Be(expected);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public async Task Closed_by_user_without_action_is_ignore(uint reason)
    {
        var (bus, service, received) = Create();
        var reminder = NewReminder();
        await service.ShowAsync(reminder);

        bus.RaiseClosed(1, reason);

        var e = received.Should().ContainSingle().Subject;
        e.LogId.Should().Be(reminder.LogId);
        e.Action.Should().Be(ReminderAction.Ignore);
    }

    [Fact]
    public async Task Closed_after_action_raises_nothing()
    {
        var (bus, service, received) = Create();
        await service.ShowAsync(NewReminder());

        bus.RaiseAction(1, "snooze");
        bus.RaiseClosed(1, 2);
        bus.RaiseClosed(1, 2);

        received.Should().ContainSingle().Which.Action.Should().Be(ReminderAction.Snooze);
    }

    [Theory]
    [InlineData(3u)]
    [InlineData(4u)]
    public async Task Closed_by_app_or_other_reason_raises_nothing(uint reason)
    {
        var (bus, service, received) = Create();
        await service.ShowAsync(NewReminder());

        bus.RaiseClosed(1, reason);
        bus.RaiseClosed(1, 2);

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task Withdraw_closes_and_later_closed_signal_is_silent()
    {
        var (bus, service, received) = Create();
        var reminder = NewReminder();
        await service.ShowAsync(reminder);

        await service.WithdrawAsync(reminder.LogId);
        bus.RaiseClosed(1, 3);
        bus.RaiseClosed(1, 2);

        bus.Closed.Should().Equal(1u);
        received.Should().BeEmpty();
    }

    [Fact]
    public async Task Withdraw_unknown_log_id_does_nothing()
    {
        var (bus, service, _) = Create();

        await service.WithdrawAsync(Guid.NewGuid());

        bus.Closed.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_ids_and_keys_are_ignored()
    {
        var (bus, service, received) = Create();
        await service.ShowAsync(NewReminder());

        bus.RaiseAction(99, "primary");
        bus.RaiseAction(1, "weird");
        bus.RaiseClosed(99, 2);

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task Subscriber_exception_does_not_propagate_to_bus()
    {
        var (bus, service, _) = Create();
        service.ActionReceived += (_, _) => throw new InvalidOperationException("boom");
        await service.ShowAsync(NewReminder());

        var act = () => bus.RaiseAction(1, "primary");

        act.Should().NotThrow();
    }
}
