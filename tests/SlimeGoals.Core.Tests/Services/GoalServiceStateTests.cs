using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Core.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

/// <summary>Regras da Fase 2: arquivada somente leitura, desarquivar, fim no passado e arquivamento automático.</summary>
public class GoalServiceStateTests
{
    // 2026-11-02 09:00 em São Paulo (UTC-3): "hoje" = 02/11/2026.
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 2);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _repo = new();
    private GoalService Service => new(_repo, _time);

    public GoalServiceStateTests() =>
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private static GoalDraft Draft() => GoalValidatorTests.Valid();

    /// <summary>Grava direto no repositório estados que o serviço não produz sozinho (ex.: arquivada no passado).</summary>
    private async Task<Goal> Seed(Func<Goal, Goal> change)
    {
        var goal = change(await Service.CreateAsync(Draft()));
        await _repo.UpdateAsync(goal);
        return goal;
    }

    [Fact]
    public async Task Update_of_archived_goal_throws_GoalStateException()
    {
        var goal = await Service.CreateAsync(Draft());
        await Service.ArchiveAsync(goal.Id);

        var act = () => Service.UpdateAsync(goal.Id, Draft() with { Title = "Outro" });

        (await act.Should().ThrowAsync<GoalStateException>())
            .WithMessage("Meta arquivada não pode ser editada. Desarquive-a primeiro.")
            .Which.GoalId.Should().Be(goal.Id);
    }

    [Fact]
    public async Task Create_with_end_date_in_the_past_reports_EndDate_error()
    {
        var act = () => Service.CreateAsync(Draft() with { EndDate = Today.AddDays(-1) });

        (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should().ContainSingle()
            .Which.Should().Be(new ValidationError(nameof(GoalDraft.EndDate), "A data de fim não pode estar no passado."));
        _repo.Count.Should().Be(0);
    }

    [Fact]
    public async Task Create_with_end_date_today_is_accepted() =>
        (await Service.CreateAsync(Draft() with { EndDate = Today })).EndDate.Should().Be(Today);

    [Fact]
    public async Task Update_with_end_date_in_the_past_reports_EndDate_error()
    {
        var goal = await Service.CreateAsync(Draft());
        var act = () => Service.UpdateAsync(goal.Id, Draft() with { EndDate = Today.AddDays(-1) });
        (await act.Should().ThrowAsync<GoalValidationException>())
            .Which.Errors.Select(e => e.Field).Should().Equal(nameof(GoalDraft.EndDate));
    }

    [Fact]
    public async Task Past_end_date_error_is_reported_together_with_validator_errors()
    {
        var act = () => Service.CreateAsync(Draft() with { Title = "", EndDate = Today.AddDays(-1) });
        (await act.Should().ThrowAsync<GoalValidationException>())
            .Which.Errors.Select(e => e.Field).Should().BeEquivalentTo([nameof(GoalDraft.Title), nameof(GoalDraft.EndDate)]);
    }

    [Fact]
    public async Task Unarchive_restores_goal_and_bumps_updated_at()
    {
        var goal = await Service.CreateAsync(Draft());
        await Service.ArchiveAsync(goal.Id);
        _time.Advance(TimeSpan.FromHours(2));

        await Service.UnarchiveAsync(goal.Id);

        var restored = await _repo.GetAsync(goal.Id);
        restored!.ArchivedAt.Should().BeNull();
        restored.UpdatedAt.Should().Be(T0.AddHours(2));
    }

    [Fact]
    public async Task Unarchive_of_active_goal_does_nothing()
    {
        var goal = await Service.CreateAsync(Draft());
        _time.Advance(TimeSpan.FromHours(1));

        await Service.UnarchiveAsync(goal.Id);

        (await _repo.GetAsync(goal.Id)).Should().Be(goal);
    }

    [Fact]
    public async Task Unarchive_of_expired_goal_throws()
    {
        var goal = await Seed(g => g with { EndDate = new DateOnly(2026, 11, 1), ArchivedAt = T0 });

        var act = () => Service.UnarchiveAsync(goal.Id);

        await act.Should().ThrowAsync<GoalStateException>()
            .WithMessage("O período desta meta terminou em 01/11/2026. Crie uma nova meta para continuar.");
    }

    [Fact]
    public async Task ArchiveExpired_archives_only_active_goals_whose_end_passed()
    {
        var earlier = T0.AddDays(-3);
        var expired = await Seed(g => g with { EndDate = Today.AddDays(-1) });
        var endsToday = await Service.CreateAsync(Draft() with { EndDate = Today });
        var openEnded = await Service.CreateAsync(Draft());
        var alreadyArchived = await Seed(g => g with { EndDate = Today.AddDays(-5), ArchivedAt = earlier });
        _time.Advance(TimeSpan.FromMinutes(5));

        var count = await Service.ArchiveExpiredAsync();

        count.Should().Be(1);
        var archived = await _repo.GetAsync(expired.Id);
        archived!.ArchivedAt.Should().Be(T0.AddMinutes(5));
        archived.UpdatedAt.Should().Be(T0.AddMinutes(5));
        (await _repo.GetAsync(endsToday.Id))!.ArchivedAt.Should().BeNull();
        (await _repo.GetAsync(openEnded.Id))!.ArchivedAt.Should().BeNull();
        (await _repo.GetAsync(alreadyArchived.Id))!.ArchivedAt.Should().Be(earlier);
    }

    [Fact]
    public async Task Today_uses_local_time_zone()
    {
        // 03/11 01:30 UTC = 02/11 22:30 em São Paulo: "hoje" ainda é 02/11.
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 3, 1, 30, 0, TimeSpan.Zero));

        (await Service.CreateAsync(Draft() with { EndDate = Today })).EndDate.Should().Be(Today);
    }
}
