using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Testing;

namespace SlimeGoals.Infrastructure.Tests.Services;

/// <summary>
/// O cronômetro rodando, lido dos repositórios de verdade sobre SQLite em memória: excluir a meta apaga o
/// cronômetro no banco, e é isso que a janelinha e a notificação precisam acompanhar.
/// </summary>
public sealed class RunningTimerWatcherTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(T0);
    private readonly DataChangeNotifier _changes = new();
    private readonly FlakyGoalRepository _goals;
    private readonly GoalService _goalService;
    private readonly TimerService _timers;
    private readonly RunningTimerWatcher _watcher;

    public RunningTimerWatcherTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        _goals = new FlakyGoalRepository(new GoalRepository(_db.Factory));
        var timerRepository = new TimerRepository(_db.Factory);
        var checkins = new CheckinService(_goals, new CheckinRepository(_db.Factory), _time, _changes);
        _goalService = new GoalService(_goals, new CheckinRepository(_db.Factory), timerRepository, _time, _changes);
        _timers = new TimerService(_goals, timerRepository, checkins, _time, _changes);
        _watcher = new RunningTimerWatcher(_timers, _goals, _changes);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _db.Dispose();
    }

    private Task<Goal> Study(string title = "Estudar inglês") => _goalService.CreateAsync(new GoalDraft(
        Title: title, Recurrence: Recurrence.Daily, Target: 60, Icon: "book", Color: GoalColor.Green, ReminderEveryMinutes: 120,
        ActiveFrom: new TimeOnly(0, 0), ActiveUntil: new TimeOnly(23, 59), ActiveDays: WeekDays.All, StartDate: null, EndDate: null,
        Measure: Measure.Duration, Unit: null));

    /// <summary>Faz <paramref name="act"/> e espera o aviso que a mudança deve causar.</summary>
    private async Task<int> ChangesDuring(Func<Task> act)
    {
        var count = 0;
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) =>
        {
            Interlocked.Increment(ref count);
            first.TrySetResult();
        };
        _watcher.Changed += handler;
        try
        {
            await act();
            await Task.WhenAny(first.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            await _watcher.RefreshAsync(); // uma leitura a mais não pode gerar aviso repetido
            return Volatile.Read(ref count);
        }
        finally
        {
            _watcher.Changed -= handler;
        }
    }

    [Fact]
    public async Task Start_reads_the_running_timer_with_the_goal_data()
    {
        var goal = await Study();
        await _timers.StartAsync(goal.Id);

        await _watcher.StartAsync();

        _watcher.Current.Should().Be(new RunningTimer(goal.Id, "Estudar inglês", "book", GoalColor.Green, T0));
    }

    [Fact]
    public async Task Without_a_timer_current_is_null()
    {
        await Study();

        await _watcher.StartAsync();

        _watcher.Current.Should().BeNull();
    }

    [Fact]
    public async Task Starting_a_timer_after_start_publishes_it()
    {
        var goal = await Study();
        await _watcher.StartAsync();

        (await ChangesDuring(() => _timers.StartAsync(goal.Id))).Should().Be(1);

        _watcher.Current!.GoalId.Should().Be(goal.Id);
    }

    [Fact]
    public async Task Stop_register_and_discard_clear_it()
    {
        var goal = await Study();
        await _timers.StartAsync(goal.Id);
        await _watcher.StartAsync();

        (await ChangesDuring(() => _timers.StopAsync())).Should().Be(1);
        _watcher.Current.Should().BeNull();

        (await ChangesDuring(() => _timers.RegisterAsync(5))).Should().Be(0); // parado → limpo: segue nulo

        await _timers.StartAsync(goal.Id);
        await _watcher.RefreshAsync();
        _watcher.Current.Should().NotBeNull();
        (await ChangesDuring(() => _timers.DiscardAsync())).Should().Be(1);
        _watcher.Current.Should().BeNull();
    }

    [Fact]
    public async Task Changed_fires_only_when_the_value_changes()
    {
        var goal = await Study();
        var other = await Study("Ler");
        await _timers.StartAsync(goal.Id);
        await _watcher.StartAsync();

        var count = await ChangesDuring(async () =>
        {
            var draft = new GoalDraft("Ler mais", other.Recurrence, other.Target, other.Icon, other.Color, other.ReminderEveryMinutes,
                other.ActiveFrom, other.ActiveUntil, other.ActiveDays, null, null, Measure.Duration, null);
            await _goalService.UpdateAsync(other.Id, draft);
        });

        count.Should().Be(0);
    }

    [Fact]
    public async Task Deleted_or_archived_goal_is_not_running()
    {
        var deleted = await Study();
        await _timers.StartAsync(deleted.Id);
        await _watcher.StartAsync();

        (await ChangesDuring(() => _goalService.DeleteAsync(deleted.Id))).Should().Be(1);
        _watcher.Current.Should().BeNull();

        // Arquivada por fora do GoalService (como chega da sincronização): o cronômetro fica no banco, mas não conta.
        var archived = await Study("Correr");
        await _timers.StartAsync(archived.Id);
        await _watcher.RefreshAsync();
        _watcher.Current.Should().NotBeNull();
        (await ChangesDuring(async () =>
        {
            await _goals.UpdateAsync(archived with { ArchivedAt = T0, UpdatedAt = T0.AddSeconds(1) });
            _changes.NotifyChanged();
        })).Should().Be(1);
        _watcher.Current.Should().BeNull();
        (await _timers.GetAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Renaming_the_goal_updates_the_title()
    {
        var goal = await Study();
        await _timers.StartAsync(goal.Id);
        await _watcher.StartAsync();

        var count = await ChangesDuring(async () =>
        {
            var draft = new GoalDraft("Estudar alemão", goal.Recurrence, goal.Target, goal.Icon, goal.Color, goal.ReminderEveryMinutes,
                goal.ActiveFrom, goal.ActiveUntil, goal.ActiveDays, null, null, Measure.Duration, null);
            await _goalService.UpdateAsync(goal.Id, draft);
        });

        count.Should().Be(1);
        _watcher.Current!.Title.Should().Be("Estudar alemão");
    }

    [Fact]
    public async Task Failure_keeps_the_last_value_and_does_not_throw()
    {
        var goal = await Study();
        await _timers.StartAsync(goal.Id);
        await _watcher.StartAsync();
        var before = _watcher.Current;
        using var trace = new TraceCapture();

        _goals.FailWith = "segredo-da-falha";
        await _watcher.RefreshAsync();

        _watcher.Current.Should().Be(before);
        trace.Messages.Should().Contain(m => m.Contains("cronômetro"));
        trace.Messages.Should().NotContain(m => m.Contains("segredo-da-falha"));
    }

    /// <summary>Repositório real que passa a falhar ao ler, sob comando.</summary>
    private sealed class FlakyGoalRepository(IGoalRepository inner) : IGoalRepository
    {
        public string? FailWith { get; set; }

        public Task AddAsync(Goal goal, CancellationToken ct = default) => inner.AddAsync(goal, ct);
        public Task UpdateAsync(Goal goal, CancellationToken ct = default) => inner.UpdateAsync(goal, ct);
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);

        public Task<Goal?> GetAsync(Guid id, CancellationToken ct = default) =>
            FailWith is { } message ? throw new InvalidOperationException(message) : inner.GetAsync(id, ct);

        public Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default) =>
            inner.ListAsync(includeArchived, ct);
    }
}
