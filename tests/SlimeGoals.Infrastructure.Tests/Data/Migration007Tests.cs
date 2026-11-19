using System.Globalization;
using Dapper;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

/// <summary>Migração 007: nome da tarefa no cronômetro.</summary>
public class Migration007Tests
{
    private const string Now = "2026-11-15T12:00:00.0000000+00:00";

    [Fact]
    public async Task Existing_timer_keeps_running_with_no_task()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 6);
        var goal = Guid.CreateVersion7();
        c.Execute("""
            INSERT INTO goals (id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
                               active_days, created_at, updated_at, measure, unit)
            VALUES (@Id, 'Estudar', 'daily', 60, 'book', 'pink', 45, '09:00', '18:00', '1,2,3', @Now, @Now, 'duration', NULL)
            """, new { Id = goal.ToString("D"), Now });
        c.Execute("INSERT INTO active_timer (id, goal_id, started_at) VALUES (1, @Id, @Now)", new { Id = goal.ToString("D"), Now });

        MigrationRunner.Migrate(c).Should().Be(9);

        (await new TimerRepository(db.Factory).GetAsync())
            .Should().Be(new ActiveTimer(goal, DateTimeOffset.Parse(Now, CultureInfo.InvariantCulture)));
    }
}
