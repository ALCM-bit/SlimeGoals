using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Account;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Sync;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Contracts;

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
    /// <param name="account">Com ela, a seção "Conta e sincronização" aparece (capacidades do desktop com <c>Sync</c>).</param>
    /// <summary>Para as cenas que precisam de registros além dos da amostra (trechos de hoje).</summary>
    public CheckinService Checkins => _services.GetRequiredService<CheckinService>();

    /// <summary>
    /// O relógio do Pomodoro nas cenas: parado, e só anda quando a cena manda (os números grandes saem sempre iguais).
    /// O resto do app segue o relógio do sistema.
    /// </summary>
    public SceneClock Clock { get; } = new();

    /// <summary>Apaga a sessão de Pomodoro e os trechos guardados, para a próxima largura começar do preparo.</summary>
    public void ClearFocus() => _services.GetRequiredService<IFocusSessionRepository>().ClearAsync().GetAwaiter().GetResult();

    public ShellViewModel CreateShell(bool phone, AccountViewModel? account = null)
    {
        var capabilities = phone ? PlatformCapabilities.Android : PlatformCapabilities.Desktop;
        if (account is not null)
            capabilities = capabilities with { Sync = true };
        IReminderPermissions? permissions = phone ? new DeniedPermissions() : null;
        var s = _services;
        var navigator = new PageNavigator();
        var focus = new FocusService(s.GetRequiredService<IGoalRepository>(), s.GetRequiredService<IFocusSessionRepository>(),
            s.GetRequiredService<ITimerRepository>(), s.GetRequiredService<CheckinService>(), s.GetRequiredService<AppSettings>(),
            Clock, s.GetRequiredService<DataChangeNotifier>());
        PageViewModel[] pages =
        [
            new TodayPageViewModel(s.GetRequiredService<ProgressService>(), s.GetRequiredService<CheckinService>(),
                s.GetRequiredService<TimerService>(), s.GetRequiredService<GoalService>(), TimeProvider.System,
                s.GetRequiredService<ReminderService>(), s.GetRequiredService<DataChangeNotifier>(),
                s.GetRequiredService<ReminderPause>(), s.GetRequiredService<ReportService>(), capabilities, permissions,
                focus, navigator),
            new GoalsPageViewModel(s.GetRequiredService<GoalService>(), TimeProvider.System),
            new TimePageViewModel(new PomodoroViewModel(focus, s.GetRequiredService<GoalService>(), Clock,
                s.GetRequiredService<DataChangeNotifier>(), navigator)),
            new ReportsPageViewModel(s.GetRequiredService<ReportService>(), TimeProvider.System,
                s.GetRequiredService<DataChangeNotifier>()),
            new SettingsPageViewModel(s.GetRequiredService<AppSettings>(), capabilities: capabilities, permissions: permissions,
                changes: s.GetRequiredService<DataChangeNotifier>(), account: account),
        ];
        return new ShellViewModel(pages, capabilities: capabilities, navigator: navigator);
    }

    private async Task SeedAsync()
    {
        var goals = _services.GetRequiredService<GoalService>();
        var checkins = _services.GetRequiredService<ICheckinRepository>();
        var from = new TimeOnly(8, 0);
        var until = new TimeOnly(22, 0);

        var water = await goals.CreateAsync(new GoalDraft("Beber água", Recurrence.Daily, 8, "water", GoalColor.Pink,
            120, from, until, WeekDays.All, null, null, Measure.Quantity, "copos"));
        var reading = await goals.CreateAsync(new GoalDraft("Leitura", Recurrence.Daily, 9000, "book", GoalColor.Purple,
            180, from, until, WeekDays.All, null, null, Measure.Duration));
        var workout = await goals.CreateAsync(new GoalDraft("Treino", Recurrence.Weekly, 18000, "workout", GoalColor.Green,
            180, from, until, WeekDays.All, null, null, Measure.Duration));
        var english = await goals.CreateAsync(new GoalDraft("Aula de inglês com a professora particular às terças", Recurrence.Weekly, 1, "language", GoalColor.Amber,
            180, from, until, WeekDays.All, null, null, Measure.Completion));

        var now = DateTimeOffset.Now;
        int[] waterCups = [8, 6, 8, 3, 8, 7, 9, 8, 5, 4];
        // Tempo em segundos. Hoje (índice 0) entra à parte, logo abaixo, a qualquer hora que a amostra rode.
        int[] readingSeconds = [0, 0, 9000, 5400, 9300, 9000, 0, 10800, 2400, 3600];
        for (var back = 0; back < 10; back++)
        {
            var day = now.AddDays(-back);
            var noon = new DateTimeOffset(day.Year, day.Month, day.Day, 12, 0, 0, day.Offset);
            await AddAsync(water.Id, noon, waterCups[back]);
            await AddAsync(reading.Id, noon.AddHours(7), readingSeconds[back]);
            if (back is 1 or 3 or 5)
                await AddAsync(workout.Id, noon.AddHours(5), (60 + back * 5) * 60);
            if (back is 2 or 8)
                await AddAsync(english.Id, noon.AddHours(6), 1);
        }
        // O caso longo da conferência a 320 px: "1 h 12 min 40 s de 2 h 30 min" e um trecho de "2 min 40 s".
        var midnight = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        var earlier = now.AddMinutes(-2) < midnight ? midnight : now.AddMinutes(-2);
        await AddAsync(reading.Id, earlier, 4200);
        await AddAsync(reading.Id, earlier.AddSeconds(1), 160, "Revisar capítulo 3");

        async Task AddAsync(Guid goalId, DateTimeOffset at, int amount, string? note = null)
        {
            if (amount <= 0 || at > now)
                return;
            await checkins.AddAsync(new Checkin
            {
                Id = Guid.NewGuid(), GoalId = goalId, DoneAt = at, Amount = amount, UpdatedAt = at, Note = note,
            });
        }
    }

    /// <summary>
    /// A janelinha do cronômetro com a meta <paramref name="title"/> rodando há 23 min 51 s. Grava o cronômetro direto no
    /// repositório, sem as regras do <see cref="TimerService"/>: a meta longa de exemplo nem é de tempo.
    /// </summary>
    public MiniTimerViewModel CreateMiniTimer(string title)
    {
        var s = _services;
        var goal = s.GetRequiredService<GoalService>().ListAsync().GetAwaiter().GetResult().Single(g => g.Title == title);
        var timers = s.GetRequiredService<ITimerRepository>();
        timers.ClearAsync().GetAwaiter().GetResult();
        timers.TryStartAsync(new ActiveTimer(goal.Id, DateTimeOffset.UtcNow - new TimeSpan(0, 23, 51))).GetAwaiter().GetResult();
        var watcher = new RunningTimerWatcher(s.GetRequiredService<TimerService>(), s.GetRequiredService<IGoalRepository>(),
            s.GetRequiredService<DataChangeNotifier>());
        watcher.StartAsync().GetAwaiter().GetResult();
        return new MiniTimerViewModel(watcher, s.GetRequiredService<TimerService>(), new NoWindow(), CreateShell(false),
            new PageNavigator(), TimeProvider.System);
    }

    /// <summary>
    /// A janelinha com uma sessão de Pomodoro no primeiro foco de 25 min, com 1 min 1 s feito (falta "23:59"). Com
    /// <paramref name="goalTitle"/>, a sessão conta nessa meta; com <paramref name="paused"/>, fica pausada.
    /// </summary>
    public MiniTimerViewModel CreateMiniFocus(string? goalTitle, string? task, bool paused = false)
    {
        var s = _services;
        s.GetRequiredService<ITimerRepository>().ClearAsync().GetAwaiter().GetResult();
        ClearFocus();
        var goals = s.GetRequiredService<IGoalRepository>();
        var goalId = goalTitle is null ? (Guid?)null
            : s.GetRequiredService<GoalService>().ListAsync().GetAwaiter().GetResult().Single(g => g.Title == goalTitle).Id;
        var focus = new FocusService(goals, s.GetRequiredService<IFocusSessionRepository>(),
            s.GetRequiredService<ITimerRepository>(), s.GetRequiredService<CheckinService>(), s.GetRequiredService<AppSettings>(),
            Clock, s.GetRequiredService<DataChangeNotifier>());
        focus.StartAsync(FocusPlan.Default, goalId).GetAwaiter().GetResult();
        if (task is not null)
            focus.SetTaskAsync(task).GetAwaiter().GetResult();
        Clock.Advance(new TimeSpan(0, 1, 1));
        if (paused)
            focus.PauseAsync().GetAwaiter().GetResult();
        var watcher = new RunningTimerWatcher(s.GetRequiredService<TimerService>(), goals,
            s.GetRequiredService<DataChangeNotifier>(), focus);
        watcher.StartAsync().GetAwaiter().GetResult();
        return new MiniTimerViewModel(watcher, s.GetRequiredService<TimerService>(), new NoWindow(), CreateShell(false),
            new PageNavigator(), Clock);
    }

    private sealed class NoWindow : IWindowActivator
    {
        public void Activate() { }
    }

    /// <summary>A seção da conta sobre uma conta falsa (nenhuma rede), com o despacho direto na thread da Render.</summary>
    public AccountViewModel CreateAccount(SampleAccount account) =>
        new(account, _services.GetRequiredService<AppSettings>(), new AccountDefaults("notebook-da-ana"), a => a());

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

/// <summary>Relógio parado que só anda com <see cref="Advance"/>; os timers (o relógio de 1 s da tela) são os do sistema.</summary>
public sealed class SceneClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
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

/// <summary>
/// Conta falsa para as imagens: devolve o status que a Render mandar, um cadastro pendente e aparelhos de exemplo.
/// Nada sai para a rede, e os códigos são de mentira.
/// </summary>
public sealed class SampleAccount : ISyncAccount
{
    public AccountStatus Status { get; set; } = new(AccountState.SignedOut, null, null, null, 0, null);

    /// <summary>Lançada pelas operações de conta, para a imagem do erro.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Segura as operações, para a imagem do "ocupado".</summary>
    public bool Hang { get; set; }

    public int FlushResult { get; set; }

    public event EventHandler? StatusChanged { add { } remove { } }

    private async Task OpAsync()
    {
        if (Hang)
            await new TaskCompletionSource().Task;
        if (Failure is { } failure)
            throw failure;
    }

    public Task<AccountStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(Status);

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public async Task<PendingRegistration> RegisterAsync(Uri server, string email, string password, string? inviteCode, CancellationToken ct = default)
    {
        await OpAsync();
        return new PendingRegistration(server, email, "K7QM-3XRT-9PWD-HB2C-VN6F-YJ8L-4S", new byte[32], new byte[32]);
    }

    public Task ConfirmAsync(PendingRegistration pending, string code, string deviceName, CancellationToken ct = default) => OpAsync();

    public Task LoginAsync(Uri server, string email, string password, string deviceName, CancellationToken ct = default) => OpAsync();

    public async Task<string> RecoverAsync(Uri server, string email, string recoveryCode, string newPassword, string deviceName,
        CancellationToken ct = default)
    {
        await OpAsync();
        return "M2ZC-8TQX-5HRB-WJ3N-KD7P-FV9G-6L";
    }

    public Task ReauthenticateAsync(string password, string deviceName, CancellationToken ct = default) => OpAsync();

    public Task RetryVaultAsync(CancellationToken ct = default) => OpAsync();

    public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default) => OpAsync();

    public async Task<int> FlushAsync(CancellationToken ct = default)
    {
        await OpAsync();
        return FlushResult;
    }

    public Task SignOutAsync(CancellationToken ct = default) => OpAsync();

    public Task DeleteAccountAsync(string password, CancellationToken ct = default) => OpAsync();

    public async Task<IReadOnlyList<DeviceDto>> ListDevicesAsync(CancellationToken ct = default)
    {
        await OpAsync();
        var seen = DateTimeOffset.Now;
        return
        [
            new DeviceDto(Guid.NewGuid(), "notebook-da-ana", seen, true),
            new DeviceDto(Guid.NewGuid(), "Celular da Ana (Pixel 8 Pro, Android 15)", seen.AddHours(-3), false),
            new DeviceDto(Guid.NewGuid(), "desktop-escritorio", seen.AddDays(-12), false),
        ];
    }

    public Task RevokeDeviceAsync(Guid id, CancellationToken ct = default) => OpAsync();

    public void SyncNow()
    {
    }
}
