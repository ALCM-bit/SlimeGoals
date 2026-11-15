using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.Support;

/// <summary>Cronômetro de verdade sobre repositórios em memória, com relógio falso e o aviso de mudanças ligado.</summary>
internal sealed class TimerFixture : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timerRepository = new();

    public TimerFixture()
    {
        Time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        Goals = new InMemoryGoalRepository(_checkins, timers: _timerRepository);
        GoalService = new GoalService(Goals, _checkins, _timerRepository, Time, Changes);
        Timers = new TimerService(Goals, _timerRepository, new CheckinService(Goals, _checkins, Time, Changes), Time, Changes);
        Watcher = new RunningTimerWatcher(Timers, Goals, Changes);
        Settings = new AppSettings(SettingsRepository, Changes);
    }

    public FakeTimeProvider Time { get; } = new(T0);
    public DataChangeNotifier Changes { get; } = new();
    public InMemoryGoalRepository Goals { get; }
    public GoalService GoalService { get; }
    public TimerService Timers { get; }
    public RunningTimerWatcher Watcher { get; }
    public InMemorySettingsRepository SettingsRepository { get; } = new();
    public AppSettings Settings { get; }

    public Task<Goal> StudyAsync(string title = "Estudar inglês") => GoalService.CreateAsync(new GoalDraft(
        Title: title, Recurrence: Recurrence.Daily, Target: 60, Icon: "book", Color: GoalColor.Green, ReminderEveryMinutes: 120,
        ActiveFrom: new TimeOnly(0, 0), ActiveUntil: new TimeOnly(23, 59), ActiveDays: WeekDays.All, StartDate: null, EndDate: null,
        Measure: Measure.Duration, Unit: null));

    public void Dispose() => Watcher.Dispose();
}
