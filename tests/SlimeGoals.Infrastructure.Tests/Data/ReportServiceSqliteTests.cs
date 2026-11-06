using Dapper;
using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class ReportServiceSqliteTests : IDisposable
{
    // Quarta, 04/11/2026 12:00 em São Paulo (UTC-3).
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 13, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(Now);

    public ReportServiceSqliteTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    public void Dispose() => _db.Dispose();

    private GoalRepository Goals => new(_db.Factory);
    private CheckinRepository Checkins => new(_db.Factory);
    private ReportService Service => new(Goals, Checkins, new ReminderLogRepository(_db.Factory), _time);

    private async Task<Goal> AddGoal(string title, DateTimeOffset createdAt)
    {
        var goal = new Goal
        {
            Id = Guid.CreateVersion7(), Title = title, Recurrence = Recurrence.Daily, Target = 1, Icon = "water",
            Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0),
            ActiveUntil = new TimeOnly(20, 0), ActiveDays = WeekDays.All, CreatedAt = createdAt, UpdatedAt = createdAt,
            Unit = "vezes",
        };
        await Goals.AddAsync(goal);
        return goal;
    }

    private Task Record(Goal goal, int month, int day, DateTimeOffset? deletedAt = null) =>
        Checkins.AddAsync(new Checkin
        {
            Id = Guid.CreateVersion7(), GoalId = goal.Id, Amount = 1, UpdatedAt = Now, DeletedAt = deletedAt,
            DoneAt = new DateTimeOffset(2026, month, day, 13, 0, 0, TimeSpan.Zero),
        });

    [Fact]
    public async Task Skipped_rows_only_remove_their_own_contribution_from_the_reports()
    {
        var kept = await AddGoal("Água", Created);
        await Record(kept, 11, 2);
        await Record(kept, 11, 3);

        var unreadable = await AddGoal("Ilegível", Created.AddMinutes(1));
        await Record(unreadable, 11, 2);
        await new ReminderLogRepository(_db.Factory).AddAsync(new ReminderLogEntry(Guid.CreateVersion7(), unreadable.Id,
            new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero)));
        _db.Anchor.Execute("UPDATE goals SET color = 'azul' WHERE id = @Id", new { Id = unreadable.Id.ToString("D") });

        // Linha de progresso inválida numa meta legível: aquele dia deixa de contar, os outros não.
        _db.Anchor.Execute("""
            UPDATE checkins SET updated_at = 'agora'
            WHERE goal_id = @Id AND done_at LIKE '2026-11-03%'
            """, new { Id = kept.Id.ToString("D") });

        var week = await Service.GetWeekAsync(new DateOnly(2026, 11, 4));
        week.Goals.Select(r => (r.Goal.Id, r.MetPeriods, r.CountedPeriods, r.Percent)).Should().Equal((kept.Id, 1, 3, 33));
        week.Percent.Should().Be(33);
        week.RemindersFired.Should().Be(0); // o disparo da meta ilegível sai junto com ela

        var month = await Service.GetMonthAsync(2026, 11);
        month.Goals.Select(r => (r.Goal.Id, r.MetPeriods, r.CountedPeriods)).Should().Equal((kept.Id, 1, 3));

        (await Service.GetTopStreakAsync()).Should().BeNull(); // sem 03/11, a sequência de 02/11 quebrou
    }

    [Fact]
    public async Task Reports_with_every_goal_unreadable_are_empty_and_do_not_fail()
    {
        var unreadable = await AddGoal("Ilegível", Created);
        await Record(unreadable, 11, 2);
        _db.Anchor.Execute("UPDATE goals SET color = 'azul'");

        (await Service.GetWeekAsync(new DateOnly(2026, 11, 4))).Percent.Should().BeNull();
        (await Service.GetMonthAsync(2026, 11)).Percent.Should().BeNull();
        (await Service.GetTopStreakAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Reports_match_checkins_including_archived_goal_and_deleted_records()
    {
        var first = await AddGoal("Água", Created);
        await Record(first, 11, 2);
        await Record(first, 11, 3);

        var archived = await AddGoal("Arquivada", Created.AddMinutes(1));
        await Record(archived, 11, 2);
        await Record(archived, 11, 3, deletedAt: Now);
        await Goals.UpdateAsync(archived with { ArchivedAt = new DateTimeOffset(2026, 11, 3, 18, 0, 0, TimeSpan.Zero) });

        var deleted = await AddGoal("Excluída", Created.AddMinutes(2));
        await Record(deleted, 11, 2);
        await Goals.UpdateAsync(deleted with { DeletedAt = Now });

        var week = await Service.GetWeekAsync(new DateOnly(2026, 11, 4));
        week.Goals.Select(r => (r.Goal.Id, r.MetPeriods, r.CountedPeriods, r.Percent)).Should().Equal(
            (first.Id, 2, 3, 67), (archived.Id, 1, 2, 50)); // hoje (04/11) pendente; a arquivada para em 03/11

        var month = await Service.GetMonthAsync(2026, 11);
        month.Goals.Select(r => (r.Goal.Id, r.MetPeriods, r.CountedPeriods)).Should().Equal(
            (first.Id, 2, 3), (archived.Id, 1, 2));
    }
}
