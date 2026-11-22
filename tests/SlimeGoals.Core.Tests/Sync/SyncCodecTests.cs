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
        SyncCodec.DecodeGoal(SyncCodec.Encode(goal), SyncCodec.FormatVersion).Should().BeEquivalentTo(goal);
    }

    [Fact]
    public void Goal_with_only_required_fields_round_trips()
    {
        var goal = FullGoal() with { Unit = null, StartDate = null, EndDate = null, ArchivedAt = null };
        SyncCodec.DecodeGoal(SyncCodec.Encode(goal), SyncCodec.FormatVersion).Should().BeEquivalentTo(goal);
    }

    [Fact]
    public void Goal_does_not_carry_DeletedAt()
    {
        var goal = FullGoal() with { DeletedAt = T0 };
        SyncCodec.DecodeGoal(SyncCodec.Encode(goal), SyncCodec.FormatVersion).DeletedAt.Should().BeNull();
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
        var act = () => SyncCodec.DecodeGoal(json, SyncCodec.FormatVersion);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Malformed_json_throws_FormatException()
    {
        var act1 = () => SyncCodec.DecodeGoal("{ não é json", SyncCodec.FormatVersion);
        var act2 = () => SyncCodec.DecodeCheckin("{}");
        act1.Should().Throw<FormatException>();
        act2.Should().Throw<FormatException>();
    }

    [Fact]
    public void Format_1_duration_goal_target_becomes_seconds()
    {
        var duration = FullGoal() with { Measure = Measure.Duration, Unit = null, Target = 90 };
        var quantity = FullGoal() with { Target = 90 };

        SyncCodec.DecodeGoal(SyncCodec.Encode(duration), 1).Target.Should().Be(5400);
        SyncCodec.DecodeGoal(SyncCodec.Encode(duration), 2).Target.Should().Be(90);
        SyncCodec.DecodeGoal(SyncCodec.Encode(quantity), 1).Target.Should().Be(90);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Goal_of_unreadable_version_throws_FormatException(int version)
    {
        var act = () => SyncCodec.DecodeGoal(SyncCodec.Encode(FullGoal()), version);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Format_1_duration_goal_target_that_overflows_throws_FormatException()
    {
        var huge = FullGoal() with { Measure = Measure.Duration, Unit = null, Target = int.MaxValue / 60 + 1 };
        var act = () => SyncCodec.DecodeGoal(SyncCodec.Encode(huge), 1);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Format_1_duration_checkin_becomes_seconds()
    {
        var checkin = new Checkin { Id = Guid.NewGuid(), GoalId = Guid.NewGuid(), DoneAt = T0, Amount = -25, UpdatedAt = T0 };

        SyncCodec.FromFormat1Duration(checkin).Should().BeEquivalentTo(checkin with { Amount = -1500 });
    }

    [Fact]
    public void Format_1_duration_checkin_that_overflows_throws_FormatException()
    {
        var checkin = new Checkin { Id = Guid.NewGuid(), GoalId = Guid.NewGuid(), DoneAt = T0, Amount = int.MinValue / 60 - 1, UpdatedAt = T0 };
        var act = () => SyncCodec.FromFormat1Duration(checkin);
        act.Should().Throw<FormatException>();
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

    [Fact]
    public void Tag_roundtrips()
    {
        var tag = new Tag { Id = TagIds.ForTag("saúde"), Name = "saúde", Color = GoalColor.Pink, UpdatedAt = T0, DeletedAt = T0.AddDays(1) };
        SyncCodec.DecodeTag(SyncCodec.Encode(tag)).Should().BeEquivalentTo(tag);
        var live = tag with { DeletedAt = null };
        SyncCodec.DecodeTag(SyncCodec.Encode(live)).Should().BeEquivalentTo(live);
    }

    [Fact]
    public void GoalTag_roundtrips()
    {
        var goalId = Guid.NewGuid();
        var link = new GoalTag { Id = TagIds.ForLink(goalId, "estudos"), GoalId = goalId, TagName = "estudos", UpdatedAt = T0, RemovedAt = T0.AddHours(2) };
        SyncCodec.DecodeGoalTag(SyncCodec.Encode(link)).Should().BeEquivalentTo(link);
        var live = link with { RemovedAt = null };
        SyncCodec.DecodeGoalTag(SyncCodec.Encode(live)).Should().BeEquivalentTo(live);
    }

    [Fact]
    public void Tag_with_unnormalized_name_is_FormatException()
    {
        var json = SyncCodec.Encode(new Tag { Id = Guid.NewGuid(), Name = "Estudos", Color = GoalColor.Purple, UpdatedAt = T0 });
        var link = SyncCodec.Encode(new GoalTag { Id = Guid.NewGuid(), GoalId = Guid.NewGuid(), TagName = "Estudos", UpdatedAt = T0 });
        var empty = SyncCodec.Encode(new Tag { Id = Guid.NewGuid(), Name = "", Color = GoalColor.Purple, UpdatedAt = T0 });
        var act1 = () => SyncCodec.DecodeTag(json);
        var act2 = () => SyncCodec.DecodeGoalTag(link);
        var act3 = () => SyncCodec.DecodeTag(empty);
        act1.Should().Throw<FormatException>();
        act2.Should().Throw<FormatException>();
        act3.Should().Throw<FormatException>();
    }

    [Fact]
    public void Goal_format_3_reads_like_format_2()
    {
        SyncCodec.FormatVersion.Should().Be(3);
        var json = SyncCodec.Encode(FullGoal());
        SyncCodec.DecodeGoal(json, 3).Should().BeEquivalentTo(SyncCodec.DecodeGoal(json, 2));
    }

    [Fact]
    public void Format_4_is_unreadable()
    {
        var act = () => SyncCodec.DecodeGoal(SyncCodec.Encode(FullGoal()), 4);
        act.Should().Throw<FormatException>();
    }
}
