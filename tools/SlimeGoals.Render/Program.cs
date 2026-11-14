using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SlimeGoals.App.Account;
using SlimeGoals.App.ViewModels;
using SlimeGoals.App.Views;
using SlimeGoals.Render;
using SlimeGoals.Sync;
using ScrollViewer = Avalonia.Controls.ScrollViewer;
using Window = Avalonia.Controls.Window;

if (args.Length < 1 || args.Length > 2 || (args.Length == 2 && args[1] is not ("phone" or "desktop" or "all" or "mascot" or "narrow" or "account")))
{
    Console.Error.WriteLine("Uso: dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all|mascot|narrow|account]");
    return 1;
}

var outDir = Path.GetFullPath(args[0]);
var which = args.Length == 2 ? args[1] : "all";
Directory.CreateDirectory(outDir);

AppBuilder.Configure<RenderApp>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

using var sample = new Sample();

if (which is "phone" or "all")
    Render(true, "phone", 412, 915);
if (which is "desktop" or "all")
    Render(false, "desktop", 1100, 720);
if (which == "narrow")
{
    RenderNarrow(320);
    RenderNarrow(360);
    RenderTodayWithTimer(320);
    RenderTodayWithTimer(360);
    RenderTodayWithTimer(1100);
}
if (which == "mascot")
    RenderMascot();
if (which == "account")
{
    RenderAccount(320, 1400);
    RenderAccount(360, 1400);
    RenderAccount(900, 800);
    RenderAccount(1100, 800);
}
return 0;

// Seção "Conta e sincronização" em cada estado e cada subpágina, sobre uma conta falsa. Cada imagem monta um shell novo.
void RenderAccount(int width, int height)
{
    var server = new Uri("http://127.0.0.1:8080/");
    var signedIn = new AccountStatus(AccountState.SignedIn, "ana@exemplo.com", server, DateTimeOffset.Now.AddMinutes(-7), 2,
        "Sem conexão com o servidor.");

    Shot("signed-out", new AccountStatus(AccountState.SignedOut, null, null, null, 0, null), _ => { });
    Shot("signed-in-error", signedIn, _ => { });
    Shot("session-expired", new AccountStatus(AccountState.SessionExpired, "ana@exemplo.com", server, null, 3,
        "A sessão deste aparelho foi encerrada. Entre de novo."), _ => { });
    Shot("vault-locked", new AccountStatus(AccountState.VaultLocked, null, null, null, 0, null), _ => { });
    Shot("session-unreadable", new AccountStatus(AccountState.SessionUnreadable, null, null, null, 0, null), _ => { });

    var signedOut = new AccountStatus(AccountState.SignedOut, null, null, null, 0, null);
    Shot("register", signedOut, vm => vm.OpenRegisterCommand.Execute(null));
    Shot("recovery-code", signedOut, vm =>
    {
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation) = ("ana@exemplo.com", "senha longa", "senha longa");
        vm.SubmitRegisterCommand.Execute(null);
    });
    Shot("confirm", signedOut, vm =>
    {
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation) = ("ana@exemplo.com", "senha longa", "senha longa");
        vm.SubmitRegisterCommand.Execute(null);
        Pump();
        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.Execute(null);
    });
    Shot("login", signedOut, vm =>
    {
        vm.OpenLoginCommand.Execute(null);
        (vm.Email, vm.Password) = ("ana@exemplo.com", "senha longa");
    });
    Shot("login-busy", signedOut, vm =>
    {
        vm.OpenLoginCommand.Execute(null);
        vm.Email = "ana@exemplo.com";
        vm.SubmitLoginCommand.Execute(null);
    }, a => a.Hang = true);
    Shot("login-error", signedOut, vm =>
    {
        vm.OpenLoginCommand.Execute(null);
        vm.Email = "ana@exemplo.com";
        vm.SubmitLoginCommand.Execute(null);
    }, a => a.Failure = new SlimeGoals.Sync.Account.AccountApiException(SlimeGoals.Sync.Account.AccountErrorKind.Unavailable));
    Shot("recover", signedOut, vm => vm.OpenRecoverCommand.Execute(null));
    Shot("recovered-code", signedOut, vm =>
    {
        vm.OpenRecoverCommand.Execute(null);
        vm.Email = "ana@exemplo.com";
        vm.SubmitRecoverCommand.Execute(null);
    });
    var expired = new AccountStatus(AccountState.SessionExpired, "ana@exemplo.com", server, null, 3, null);
    Shot("reauthenticate", expired, vm => vm.OpenReauthenticateCommand.Execute(null));
    Shot("change-password", signedIn, vm => vm.OpenChangePasswordCommand.Execute(null));
    Shot("devices", signedIn, vm => vm.OpenDevicesCommand.Execute(null));
    Shot("sign-out", signedIn, vm => vm.OpenSignOutCommand.Execute(null), a => a.FlushResult = 3);
    Shot("delete-account", signedIn, vm => vm.OpenDeleteAccountCommand.Execute(null));

    void Shot(string name, AccountStatus status, Action<AccountViewModel> open, Action<SampleAccount>? setup = null)
    {
        var fake = new SampleAccount { Status = status };
        setup?.Invoke(fake);
        var account = sample.CreateAccount(fake);
        var shell = sample.CreateShell(false, account);
        var window = new Window { Width = width, Height = height, Content = new ShellView { DataContext = shell } };
        window.Show();
        var settings = shell.Pages.OfType<SettingsPageViewModel>().Single();
        shell.CurrentPage = settings;
        settings.OpenSectionCommand.Execute(SettingsSection.Sync);
        Pump();
        open(account);
        Pump();
        var path = Path.Combine(outDir, $"account-{name}-{width}.png");
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"Sem quadro para {name}.");
        using (var stream = File.Create(path))
        {
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
            frame.Save(stream);
#pragma warning restore CS0618
        }
        Console.WriteLine(path);
        window.Close();
    }
}

void RenderMascot()
{
    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
    foreach (var pose in Enum.GetValues<MascotPose>())
    {
        var slime = new SlimeMascot { Pose = pose, Width = 160, Height = 160 };
        var cell = new StackPanel { Spacing = 8 };
        cell.Children.Add(slime);
        cell.Children.Add(new TextBlock
        {
            Text = AutomationProperties.GetName(slime),
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        row.Children.Add(cell);
    }
    var hero = (IBrush)Application.Current!.FindResource("HeroBrush")!;
    var window = new Window
    {
        Width = 4 * 160 + 3 * 16 + 48,
        Height = 160 + 8 + 20 + 48,
        Content = new Border { Background = hero, Padding = new Thickness(24), Child = row },
    };
    window.Show();
    Pump();
    var path = Path.Combine(outDir, "mascot.png");
    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sem quadro para mascot.");
    using (var stream = File.Create(path))
    {
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
        frame.Save(stream);
#pragma warning restore CS0618
    }
    Console.WriteLine(path);
    window.Close();
}

// Editor de uma meta existente, com a confirmação de exclusão aberta, em janela alta: o formulário cabe num quadro só.
void RenderNarrow(int width)
{
    var shell = sample.CreateShell(true);
    var window = new Window { Width = width, Height = 1700, Content = new ShellView { DataContext = shell } };
    window.Show();
    var goals = shell.Pages.OfType<GoalsPageViewModel>().Single();
    shell.CurrentPage = goals;
    Pump();
    goals.EditCommand.Execute(goals.Goals[0]);
    Pump();
    goals.Editor!.DeleteCommand.Execute(null);
    Pump();
    var path = Path.Combine(outDir, $"goal-edit-delete-{width}.png");
    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sem quadro para capturar.");
    using var stream = File.Create(path);
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
    frame.Save(stream);
#pragma warning restore CS0618
    Console.WriteLine(path);
    window.Close();
}

// Hoje com um cronômetro rodando: o cartão mais apertado (tempo + parar + mais opções).
void RenderTodayWithTimer(int width)
{
    var shell = sample.CreateShell(width < 720);
    var window = new Window { Width = width, Height = width < 720 ? 1100 : 720, Content = new ShellView { DataContext = shell } };
    window.Show();
    var today = shell.Pages.OfType<TodayPageViewModel>().Single();
    shell.CurrentPage = today;
    Pump();
    // O cronômetro fica rodando de uma largura para a outra: só inicia se ainda não houver um.
    if (today.Goals.FirstOrDefault(g => g.IsDuration && g.CanStartTimer) is { } timed)
    {
        today.StartTimerCommand.Execute(timed);
        Pump();
    }
    var path = Path.Combine(outDir, $"today-timer-{width}.png");
    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sem quadro para capturar.");
    using (var stream = File.Create(path))
    {
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
        frame.Save(stream);
#pragma warning restore CS0618
    }
    Console.WriteLine(path);
    window.Close();
}

void Render(bool phone, string suffix, int width, int height)
{
    var shell = sample.CreateShell(phone);
    var window = new Window { Width = width, Height = height, Content = new ShellView { DataContext = shell } };
    window.Show();
    Pump();

    var today = shell.Pages.OfType<TodayPageViewModel>().Single();
    var goals = shell.Pages.OfType<GoalsPageViewModel>().Single();
    var reports = shell.Pages.OfType<ReportsPageViewModel>().Single();
    var settings = shell.Pages.OfType<SettingsPageViewModel>().Single();

    Show("today", today);
    Show("goals", goals);

    goals.NewCommand.Execute(null);
    Pump();
    Capture("goal-edit");
    // O fim do formulário (horários, período e, no layout estreito, as ações) fica abaixo da dobra
    window.GetVisualDescendants().OfType<GoalEditView>().Single()
        .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
    Pump();
    Capture("goal-edit-end");
    goals.Editor = null;

    reports.Mode = ReportMode.Week;
    Show("reports-week", reports);
    reports.Mode = ReportMode.Month;
    Pump();
    Capture("reports-month");
    reports.Mode = ReportMode.Week;

    Show("settings", settings);
    // Cada grupo de Configurações na página dele (os que a plataforma tem).
    foreach (var section in new[] { SettingsSection.Reminders, SettingsSection.Window, SettingsSection.General, SettingsSection.About })
    {
        settings.OpenSectionCommand.Execute(section);
        if (settings.Section != section)
            continue;
        Pump();
        Capture($"settings-{section.ToString().ToLowerInvariant()}");
        settings.CloseSectionCommand.Execute(null);
    }

    window.Close();

    void Show(string name, PageViewModel page)
    {
        shell.CurrentPage = page;
        Pump();
        Capture(name);
    }

    void Capture(string name)
    {
        var path = Path.Combine(outDir, $"{name}-{suffix}.png");
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"Sem quadro para {name}.");
        using var stream = File.Create(path);
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
        frame.Save(stream);
#pragma warning restore CS0618
        Console.WriteLine(path);
    }
}

/// <summary>Deixa as cargas assíncronas das páginas terminarem e a interface se redesenhar.</summary>
static void Pump()
{
    for (var i = 0; i < 20; i++)
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(40);
    }
    Dispatcher.UIThread.RunJobs();
}
