using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Render;

/// <summary>Banco temporário com metas e registros de exemplo, e a montagem do shell sobre ele.</summary>
public sealed class Sample : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"slimegoals-render-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _services;

    public Sample()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DataChangeNotifier>();
        services.AddSingleton<GoalService>();
        services.AddSingleton<CheckinService>();
        services.AddSingleton<TimerService>();
        services.AddSingleton<AppSettings>();
        services.AddSingleton<ProgressService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<ReminderPause>();
        services.AddSingleton<ReminderService>();
        services.AddSlimeGoalsInfrastructure(_databasePath);
        services.AddSingleton<IReminderFormatter, ReminderFormatter>();
        services.AddSingleton<INotificationService, NullNotificationService>();
        _services = services.BuildServiceProvider();

        using (var connection = _services.GetRequiredService<SqliteConnectionFactory>().Open())
            MigrationRunner.Migrate(connection);
        SeedAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Monta um shell novo (páginas novas, com os dados do banco de exemplo). Na variação
    /// <paramref name="phone"/>, com as capacidades do Android (nada de lembretes, autostart, janela nem Sair).
    /// </summary>
    public ShellViewModel CreateShell(bool phone)
    {
        var capabilities = phone ? new PlatformCapabilities(true, false, false, false) : PlatformCapabilities.Desktop;
        IReminderPermissions? permissions = phone ? new DeniedPermissions() : null;
        var s = _services;
        PageViewModel[] pages =
        [
            new TodayPageViewModel(s.GetRequiredService<ProgressService>(), s.GetRequiredService<CheckinService>(),
                s.GetRequiredService<TimerService>(), s.GetRequiredService<GoalService>(), TimeProvider.System,
                s.GetRequiredService<ReminderService>(), s.GetRequiredService<DataChangeNotifier>(),
                s.GetRequiredService<ReminderPause>(), s.GetRequiredService<ReportService>(), capabilities, permissions),
            new GoalsPageViewModel(s.GetRequiredService<GoalService>(), TimeProvider.System),
            new ReportsPageViewModel(s.GetRequiredService<ReportService>(), TimeProvider.System,
                s.GetRequiredService<DataChangeNotifier>()),
            new SettingsPageViewModel(s.GetRequiredService<AppSettings>(), capabilities: capabilities, permissions: permissions,
                changes: s.GetRequiredService<DataChangeNotifier>()),
        ];
        return new ShellViewModel(pages, capabilities: capabilities);
    }

    private async Task SeedAsync()
    {
        var goals = _services.GetRequiredService<GoalService>();
        var checkins = _services.GetRequiredService<ICheckinRepository>();
        var from = new TimeOnly(8, 0);
        var until = new TimeOnly(22, 0);

        var water = await goals.CreateAsync(new GoalDraft("Beber água", Recurrence.Daily, 8, "water", GoalColor.Pink,
            120, from, until, WeekDays.All, null, null, Measure.Quantity, "copos"));
        var reading = await goals.CreateAsync(new GoalDraft("Leitura", Recurrence.Daily, 30, "book", GoalColor.Purple,
            180, from, until, WeekDays.All, null, null, Measure.Duration));
        var workout = await goals.CreateAsync(new GoalDraft("Treino", Recurrence.Weekly, 300, "workout", GoalColor.Green,
            180, from, until, WeekDays.All, null, null, Measure.Duration));
        var english = await goals.CreateAsync(new GoalDraft("Aula de inglês com a professora particular às terças", Recurrence.Weekly, 1, "language", GoalColor.Amber,
            180, from, until, WeekDays.All, null, null, Measure.Completion));

        var now = DateTimeOffset.Now;
        int[] waterCups = [8, 6, 8, 3, 8, 7, 9, 8, 5, 4];
        int[] readingMinutes = [30, 0, 35, 20, 30, 30, 0, 45, 10, 15];
        for (var back = 0; back < 10; back++)
        {
            var day = now.AddDays(-back);
            var noon = new DateTimeOffset(day.Year, day.Month, day.Day, 12, 0, 0, day.Offset);
            await AddAsync(water.Id, noon, waterCups[back]);
            await AddAsync(reading.Id, noon.AddHours(7), readingMinutes[back]);
            if (back is 1 or 3 or 5)
                await AddAsync(workout.Id, noon.AddHours(5), 60 + back * 5);
            if (back is 2 or 8)
                await AddAsync(english.Id, noon.AddHours(6), 1);
        }

        async Task AddAsync(Guid goalId, DateTimeOffset at, int amount)
        {
            if (amount <= 0 || at > now)
                return;
            await checkins.AddAsync(new Checkin { Id = Guid.NewGuid(), GoalId = goalId, DoneAt = at, Amount = amount, UpdatedAt = at });
        }
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
            if (File.Exists(path))
                File.Delete(path);
    }

    private sealed class NullNotificationService : INotificationService
    {
        public event EventHandler<ReminderActionEventArgs>? ActionReceived { add { } remove { } }
        public Task ShowAsync(Reminder reminder, CancellationToken ct = default) => Task.CompletedTask;
        public Task WithdrawAsync(Guid logId, CancellationToken ct = default) => Task.CompletedTask;
    }
}

/// <summary>Porta falsa do celular: as duas permissões negadas, para o aviso aparecer nas imagens.</summary>
internal sealed class DeniedPermissions : IReminderPermissions
{
    public ReminderPermissionStatus Get() => new(false, false);

    public void RequestNotifications()
    {
    }

    public void RequestExactAlarms()
    {
    }

    public void RequestBatteryUnrestricted()
    {
    }
}
