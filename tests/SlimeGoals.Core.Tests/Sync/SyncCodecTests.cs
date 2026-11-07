using System.Text.Json;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;

namespace SlimeGoals.Core.Tests.Sync;

public class SyncCodecTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 30, 15, 123, TimeSpan.Zero);

    private static Goal FullGoal() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Beber água",
        Recurrence = Recurrence.Weekly,
        Target = 8,
        Measure = Measure.Quantity,
        Unit = "copos",
        Icon = "droplet",
        Color = GoalColor.Amber,
        ReminderEveryMinutes = 90,
        ActiveFrom = new TimeOnly(8, 5),
        ActiveUntil = new TimeOnly(21, 45),
        ActiveDays = WeekDays.Monday | WeekDays.Friday,
        StartDate = new DateOnly(2026, 11, 1),
        EndDate = new DateOnly(2026, 12, 31),
        CreatedAt = T0,
        UpdatedAt = T0.AddHours(1),
        ArchivedAt = T0.AddDays(2),
    };

    [Fact]
    public void Goal_round_trips()
    {
        var goal = FullGoal();
        SyncCodec.DecodeGoal(SyncCodec.Encode(goal)).Should().BeEquivalentTo(goal);
    }

    [Fact]
    public void Goal_with_only_required_fields_round_trips()
    {
        var goal = FullGoal() with { Unit = null, StartDate = null, EndDate = null, ArchivedAt = null };
        SyncCodec.DecodeGoal(SyncCodec.Encode(goal)).Should().BeEquivalentTo(goal);
    }

    [Fact]
    public void Goal_does_not_carry_DeletedAt()
    {
        var goal = FullGoal() with { DeletedAt = T0 };
        SyncCodec.DecodeGoal(SyncCodec.Encode(goal)).DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Checkin_round_trips()
    {
        var checkin = new Checkin
        {
            Id = Guid.NewGuid(),
            GoalId = Guid.NewGuid(),
            DoneAt = T0,
            Amount = -2,
            Note = "ajuste",
            UpdatedAt = T0.AddMinutes(5),
            Compensation = new Compensation(new DateOnly(2026, 11, 2), [Guid.NewGuid(), Guid.NewGuid()]),
        };
        SyncCodec.DecodeCheckin(SyncCodec.Encode(checkin)).Should().BeEquivalentTo(checkin);
    }

    [Fact]
    public void Checkin_without_optionals_round_trips()
    {
        var checkin = new Checkin { Id = Guid.NewGuid(), GoalId = Guid.NewGuid(), DoneAt = T0, Amount = 1, UpdatedAt = T0 };
        SyncCodec.DecodeCheckin(SyncCodec.Encode(checkin)).Should().BeEquivalentTo(checkin);
    }

    [Fact]
    public void Unknown_enum_value_throws_FormatException()
    {
        var json = SyncCodec.Encode(FullGoal()).Replace("\"Weekly\"", "\"Yearly\"");
        var act = () => SyncCodec.DecodeGoal(json);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Malformed_json_throws_FormatException()
    {
        var act1 = () => SyncCodec.DecodeGoal("{ não é json");
        var act2 = () => SyncCodec.DecodeCheckin("{}");
        act1.Should().Throw<FormatException>();
        act2.Should().Throw<FormatException>();
    }

    [Fact]
    public void Payload_uses_invariant_formats()
    {
        using var doc = JsonDocument.Parse(SyncCodec.Encode(FullGoal()));
        var root = doc.RootElement;
        root.GetProperty("startDate").GetString().Should().Be("2026-11-01");
        root.GetProperty("activeFrom").GetString().Should().Be("08:05");
        root.GetProperty("activeUntil").GetString().Should().Be("21:45");
        root.GetProperty("createdAt").GetString().Should().Be(T0.ToString("O"));
    }
}
