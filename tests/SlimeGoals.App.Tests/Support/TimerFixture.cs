using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.Support;

/// <summary>
/// Cronômetro e sessão de Pomodoro de verdade sobre repositórios em memória, com relógio falso e o aviso de mudanças
/// ligado. O <see cref="Watcher"/> mostra o cronômetro e, sem ele, a sessão.
/// </summary>
internal sealed class TimerFixture : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timerRepository = new();

    public TimerFixture()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        Goals = new InMemoryGoalRepository(_checkins, timers: _timerRepository);
        Sessions = new InMemoryFocusSessionRepository(_checkins, _timerRepository);
        GoalService = new GoalService(Goals, _checkins, _timerRepository, Time, Changes);
        var checkins = new CheckinService(Goals, _checkins, Time, Changes);
        Timers = new TimerService(Goals, _timerRepository, checkins, Time, Changes, Sessions);
        Settings = new AppSettings(SettingsRepository, Changes);
        Focus = new FocusService(Goals, Sessions, _timerRepository, checkins, Settings, Time, Changes);
        Watcher = new RunningTimerWatcher(Timers, Goals, Changes, Focus);
    }

    public FakeTimeProvider Time { get; } = new(T0);
    public DataChangeNotifier Changes { get; } = new();
    public InMemoryGoalRepository Goals { get; }
    public GoalService GoalService { get; }
    public TimerService Timers { get; }
    public InMemoryFocusSessionRepository Sessions { get; }
    public FocusService Focus { get; }
    public RunningTimerWatcher Watcher { get; }
    public InMemorySettingsRepository SettingsRepository { get; } = new();
    public AppSettings Settings { get; }

    public Task<Goal> StudyAsync(string title = "Estudar inglês") => GoalService.CreateAsync(new GoalDraft(
        Title: title, Recurrence: Recurrence.Daily, Target: 60, Icon: "book", Color: GoalColor.Green, ReminderEveryMinutes: 120,
        ActiveFrom: new TimeOnly(0, 0), ActiveUntil: new TimeOnly(23, 59), ActiveDays: WeekDays.All, StartDate: null, EndDate: null,
        Measure: Measure.Duration, Unit: null));

    public void Dispose() => Watcher.Dispose();
}
