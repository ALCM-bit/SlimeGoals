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
using SlimeGoals.Sync.Integrity;
using SlimeGoals.App.Views;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;
using SlimeGoals.Render;
using SlimeGoals.Sync;
using ScrollViewer = Avalonia.Controls.ScrollViewer;
using Window = Avalonia.Controls.Window;

if (args.Length < 1 || args.Length > 2 || (args.Length == 2 && args[1] is not ("phone" or "desktop" or "all" or "mascot" or "narrow" or "time" or "account" or "mini" or "sync-warning" or "today-filter")))
{
    Console.Error.WriteLine("Uso: dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all|mascot|narrow|time|account|mini|sync-warning|today-filter]");
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
    // As páginas do celular também nas larguras mais estreitas (Hoje e Relatórios com os textos longos de tempo)
    Render(true, "phone-320", 320, 915);
    Render(true, "phone-360", 360, 915);
    RenderNarrow(320);
    RenderNarrow(360);
    RenderTodayWithTimer(320);
    RenderTodayWithTimer(360);
    RenderTodayWithTimer(1100);
}
// A aba Tempo vem no narrow e, sozinha, no time (bem mais rápido para conferir só o Pomodoro)
if (which is "narrow" or "time")
{
    RenderTime(320, 640);
    RenderTime(360, 760);
    RenderTime(412, 915);
    RenderTime(1100, 720);
}
if (which == "mascot")
    RenderMascot();
if (which == "mini")
{
    RenderMini("mini-timer", () => sample.CreateMiniTimer("Leitura"));
    RenderMini("mini-timer-long", () => sample.CreateMiniTimer("Aula de inglês com a professora particular às terças"));
    RenderMini("mini-timer-hover", () => sample.CreateMiniTimer("Leitura"), hover: true);
    // Sessão de Pomodoro: sem o "Parar", contando para baixo
    RenderMini("mini-focus", () => sample.CreateMiniFocus(null, null));
    RenderMini("mini-focus-goal-task", () => sample.CreateMiniFocus("Leitura", "Revisar capítulo 3"));
    RenderMini("mini-focus-paused-hover", () => sample.CreateMiniFocus("Leitura", null, paused: true), hover: true);
    sample.ClearFocus();
}
// O filtro de tags do Hoje vem no all e, sozinho, no today-filter
if (which is "all" or "today-filter")
{
    RenderTodayTags(320, 900);
    RenderTodayTags(360, 900);
    RenderTodayTags(412, 915);
    RenderTodayTags(1100, 720);
    RenderTodayEmpty(360, 900);
    RenderTodayEmpty(1100, 720);
}
if (which == "sync-warning")
{
    RenderSyncWarning(320, 700, true);
    RenderSyncWarning(360, 700, true);
    RenderSyncWarning(412, 915, true);
    RenderSyncWarning(1100, 720, false);
}
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
    Shot("insecure-session", new AccountStatus(AccountState.InsecureSession, "ana@exemplo.com", new Uri("http://sync.exemplo.com/"), null, 2,
        null), _ => { });
    Shot("untrusted-app", new AccountStatus(AccountState.UntrustedApp, null, null, null, 0, null), _ => { },
        integrity: new Sample.SampleIntegrity(IntegrityIssue.Debugger));
    Shot("rooted", signedIn, _ => { }, integrity: new Sample.SampleIntegrity(IntegrityIssue.Rooted));

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

    // Sincronizar agora: rodando, concluído e sem conexão (o motivo na faixa, abaixo da linha).
    var synced = signedIn with { LastSyncAt = DateTimeOffset.Now, Pending = 0, LastError = null };
    Shot("sync-running", synced, vm => vm.SyncNowCommand.Execute(null), a => a.Hang = true);
    Shot("sync-done", synced, vm => vm.SyncNowCommand.Execute(null));
    Shot("sync-failed", signedIn, vm => vm.SyncNowCommand.Execute(null), a => a.SyncResult = SyncCycleResult.Failed);

    void Shot(string name, AccountStatus status, Action<AccountViewModel> open, Action<SampleAccount>? setup = null,
        IAppIntegrity? integrity = null)
    {
        var fake = new SampleAccount { Status = status };
        setup?.Invoke(fake);
        var account = sample.CreateAccount(fake, integrity);
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

// Hoje com a conta ligada e sem sincronizar há 4 dias; no celular, junto do aviso de permissões (os dois empilhados).
void RenderSyncWarning(int width, int height, bool phone)
{
    var server = new Uri("https://sync.exemplo.com/");
    var stale = new SampleAccount
    {
        Status = new AccountStatus(AccountState.SignedIn, "ana@exemplo.com", server, DateTimeOffset.UtcNow.AddDays(-4), 0, null),
    };
    var shell = sample.CreateShell(phone, syncAccount: stale);
    var window = new Window { Width = width, Height = height, Content = new ShellView { DataContext = shell } };
    window.Show();
    shell.CurrentPage = shell.Pages.OfType<TodayPageViewModel>().Single();
    Pump();
    var path = Path.Combine(outDir, $"today-sync-warning-{width}.png");
    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sem quadro para o aviso de sincronização.");
    using (var stream = File.Create(path))
    {
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
        frame.Save(stream);
#pragma warning restore CS0618
    }
    Console.WriteLine(path);
    window.Close();
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

// A janelinha do cronômetro de verdade, sobre um fundo neutro com folga, para ver os cantos arredondados.
// Com hover, o "×" aparece como com o mouse sobre a janelinha (a captura não tem ponteiro).
void RenderMini(string name, Func<MiniTimerViewModel> viewModel, bool hover = false)
{
    var window = new MiniTimerWindow
    {
        DataContext = viewModel(),
        Background = new SolidColorBrush(Color.Parse("#3B4250")),
        Padding = new Thickness(14),
    };
    window.Show();
    if (hover)
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("mini-timer-hide")).Opacity = 1;
    Pump();
    var path = Path.Combine(outDir, $"{name}.png");
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

// Aba Tempo: cada momento do Pomodoro (spec Pomodoro §6), em altura de celular de verdade, para ver as ações presas
// embaixo. O relógio do Pomodoro é o da amostra, parado: cada cena avança o tempo que precisa.
void RenderTime(int width, int height)
{
    sample.ClearFocus();
    var shell = sample.CreateShell(width < 720);
    var window = new Window { Width = width, Height = height, Content = new ShellView { DataContext = shell } };
    window.Show();
    var page = shell.Pages.OfType<TimePageViewModel>().Single();
    shell.CurrentPage = page;
    Pump();
    var vm = page.Pomodoro;
    var clock = sample.Clock;

    // Preparo: a sequência do esboço aprovado (40, 20, 1 h, 30), "Começar · 2 h 30 min"
    SetPlan([(FocusBlockKind.Focus, 40), (FocusBlockKind.Break, 20), (FocusBlockKind.Focus, 60), (FocusBlockKind.Break, 30)], 1);
    Pump();
    Capture("time-prepare");
    vm.EditBlockCommand.Execute(vm.Blocks[2]);
    Pump();
    Capture("time-block-edit");
    vm.CloseBlockEditorCommand.Execute(null);

    // Rodando: duas rodadas, meta Leitura, no segundo foco da primeira rodada com 47:12 de 1 h
    vm.RepeatInput = 2;
    vm.SelectedGoal = vm.GoalOptions.First(o => o.Title == "Leitura");
    Run(vm.StartCommand.ExecuteAsync(null));
    clock.Advance(TimeSpan.FromMinutes(40));
    Run(vm.AdvanceCommand.ExecuteAsync(null));
    clock.Advance(TimeSpan.FromMinutes(20));
    Run(vm.AdvanceCommand.ExecuteAsync(null));
    vm.TaskInput = "Revisar capítulo 3";
    Run(vm.SaveTaskCommand.ExecuteAsync(null));
    clock.Advance(new TimeSpan(0, 12, 48));
    Reload();
    Capture("time-running");
    // Trocando de subatividade: o campo vazio com Cancelar e Trocar
    Run(vm.BeginSwitchTaskCommand.ExecuteAsync(null));
    Capture("time-switch");
    vm.CancelSwitchTaskCommand.Execute(null);

    Run(vm.PauseResumeCommand.ExecuteAsync(null));
    Capture("time-paused");
    Run(vm.PauseResumeCommand.ExecuteAsync(null));

    // Fim do foco, 2 min depois: "Foco concluído", "+2 min", [Encerrar] [Começar pausa]
    clock.Advance(new TimeSpan(0, 49, 12));
    Reload();
    Capture("time-waiting");
    Run(vm.AdvanceCommand.ExecuteAsync(null));
    // Fim da pausa, 3 min depois: [Encerrar] [Voltar ao foco]
    clock.Advance(TimeSpan.FromMinutes(33));
    Reload();
    Capture("time-waiting-break");
    Run(vm.AdvanceCommand.ExecuteAsync(null));
    // Foco esquecido (35 min de extra): o toque abre a conferência do último trecho, com os segundos
    clock.Advance(new TimeSpan(0, 75, 20));
    Reload();
    Run(vm.AdvanceCommand.ExecuteAsync(null));
    Capture("time-review");
    (vm.ReviewHoursInput, vm.ReviewMinutesInput, vm.ReviewSecondsInput) = (0, 45, 0);
    Run(vm.ConfirmReviewCommand.ExecuteAsync(null));
    // ✕ na pausa: a confirmação, e a tela de fim de uma sessão com meta
    vm.AskEndCommand.Execute(null);
    Pump();
    Capture("time-confirm-end");
    Run(vm.ConfirmEndCommand.ExecuteAsync(null));
    Capture("time-end-linked");
    vm.NewSessionCommand.Execute(null);
    Pump();

    // Sessão sem meta: 58 min 40 s em uma subatividade e 1 h 12 min 40 s em outra, de nome longo, encerrada no meio do foco
    SetPlan([(FocusBlockKind.Focus, 200), (FocusBlockKind.Break, 20)], 1);
    Run(vm.StartCommand.ExecuteAsync(null));
    vm.TaskInput = "Revisar capítulo 3";
    Run(vm.SaveTaskCommand.ExecuteAsync(null));
    clock.Advance(new TimeSpan(0, 58, 40));
    Reload();
    Run(vm.BeginSwitchTaskCommand.ExecuteAsync(null));
    vm.TaskInput = "Revisar o capítulo 3 do livro de arquitetura";
    Run(vm.SwitchTaskCommand.ExecuteAsync(null));
    clock.Advance(new TimeSpan(1, 12, 40));
    Reload();
    vm.AskEndCommand.Execute(null);
    Run(vm.ConfirmEndCommand.ExecuteAsync(null));
    Capture("time-end-unassigned");
    vm.AskDiscardCommand.Execute(null);
    Pump();
    Capture("time-discard");
    vm.CancelDiscardCommand.Execute(null);
    // "Criar uma meta com ele": o editor de meta, já como meta de tempo
    vm.CreateGoalCommand.Execute(null);
    Pump();
    Capture("time-create-goal");
    vm.Editor!.CancelCommand.Execute(null);
    Pump();

    // Hoje com a sessão aberta: a faixa que leva à aba Tempo, na altura das outras cenas do Hoje no celular (915)
    window.Height = width < 720 ? 915 : height;
    shell.CurrentPage = shell.Pages.OfType<TodayPageViewModel>().Single();
    Pump();
    Capture("today-focus-band");
    window.Close();
    sample.ClearFocus();

    void SetPlan((FocusBlockKind Kind, int Minutes)[] blocks, int repeat)
    {
        vm.Blocks.Clear();
        foreach (var (kind, minutes) in blocks)
            vm.Blocks.Add(new FocusBlockViewModel(new FocusBlock(kind, minutes)));
        vm.RepeatInput = repeat;
    }

    void Run(Task task)
    {
        while (!task.IsCompleted)
            Pump();
        task.GetAwaiter().GetResult();
        Pump();
    }

    void Reload() => Run(vm.LoadCommand.ExecuteAsync(null));

    void Capture(string name)
    {
        var path = Path.Combine(outDir, $"{name}-{width}.png");
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"Sem quadro para {name}.");
        using (var stream = File.Create(path))
        {
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
            frame.Save(stream);
#pragma warning restore CS0618
        }
        Console.WriteLine(path);
    }
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

// Hoje sem metas (today-empty): o aviso logo abaixo do cabeçalho, numa amostra sem dados.
void RenderTodayEmpty(int width, int height)
{
    using var empty = new Sample(seed: false);
    var shell = empty.CreateShell(width < 720);
    var window = new Window { Width = width, Height = height, Content = new ShellView { DataContext = shell } };
    window.Show();
    shell.CurrentPage = shell.Pages.OfType<TodayPageViewModel>().Single();
    Pump();
    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sem quadro para capturar.");
    var path = Path.Combine(outDir, $"today-empty-{width}.png");
    using (var stream = File.Create(path))
    {
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
        frame.Save(stream);
#pragma warning restore CS0618
    }
    Console.WriteLine(path);
    window.Close();
}

// Hoje com tags: a linha de filtros sem seleção (idle), com duas tags marcadas (today-filter: Limpar e "x de y metas"),
// com uma tag que esconde cartões (single) e sem nenhuma meta com tag (today-filter-empty: a linha some).
void RenderTodayTags(int width, int height)
{
    var shell = sample.CreateShell(width < 720);
    var window = new Window { Width = width, Height = height, Content = new ShellView { DataContext = shell } };
    window.Show();
    var today = shell.Pages.OfType<TodayPageViewModel>().Single();
    shell.CurrentPage = today;
    Pump();
    Capture($"today-filter-idle-{width}.png");
    foreach (var name in new[] { "saúde", "estudos" })
        today.Filter.ToggleCommand.Execute(today.Filter.Chips.Single(c => c.Name == name));
    Pump();
    Capture($"today-filter-{width}.png");
    // Uma tag que esconde cartões: só "treino" (a meta Treino) fica na lista.
    today.Filter.ClearCommand.Execute(null);
    today.Filter.ToggleCommand.Execute(today.Filter.Chips.Single(c => c.Name == "treino"));
    Pump();
    Capture($"today-filter-single-{width}.png");
    today.Filter.ClearCommand.Execute(null);
    // O estado vazio que se alcança: nenhuma meta de hoje tem tag, então não há linha de filtros.
    // "Nenhuma meta de hoje com essas tags." não tem captura: hoje nenhuma combinação de chips esvazia a lista.
    var goalService = sample.Goals;
    var tagged = goalService.ListAsync().GetAwaiter().GetResult().Where(g => g.Tags.Count > 0).ToList();
    SetTags(g => []);
    today.LoadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
    Pump();
    Capture($"today-filter-empty-{width}.png");
    SetTags(g => g.Tags); // devolve as tags: a mesma amostra serve às outras larguras e telas
    window.Close();

    void SetTags(Func<Goal, IReadOnlyList<string>> tagsOf)
    {
        foreach (var g in tagged)
            goalService.UpdateAsync(g.Id, new GoalDraft(g.Title, g.Recurrence, g.Target, g.Icon, g.Color, g.ReminderEveryMinutes,
                g.ActiveFrom, g.ActiveUntil, g.ActiveDays, g.StartDate, g.EndDate, g.Measure, g.Unit) with
            { Tags = tagsOf(g) })
                .GetAwaiter().GetResult();
    }

    void Capture(string name)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sem quadro para capturar.");
        using var stream = File.Create(Path.Combine(outDir, name));
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
        frame.Save(stream);
#pragma warning restore CS0618
        Console.WriteLine(Path.Combine(outDir, name));
    }
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
    var running = today.Goals.Single(g => g.IsTimerRunning);
    running.TaskInput = "Revisar o capítulo 3 do livro de arquitetura";
    today.SaveTaskCommand.Execute(running);
    Pump();
    Capture($"today-timer-{width}.png");
    // Editando o nome gravado: o campo com ✓ e ✕
    running = today.Goals.Single(g => g.IsTimerRunning);
    today.BeginEditTaskCommand.Execute(running);
    Pump();
    Capture($"today-timer-edit-{width}.png");
    today.CancelEditTaskCommand.Execute(running);
    Pump();
    // Trechos de hoje: a lista aberta e o editor do registro mais recente
    running = today.Goals.Single(g => g.IsTimerRunning);
    if (!running.HasChunks)
    {
        sample.Checkins.AdjustAsync(running.Goal.Id, 25 * 60, "Inglês").GetAwaiter().GetResult();
        sample.Checkins.AdjustAsync(running.Goal.Id, 160).GetAwaiter().GetResult();
        today.LoadCommand.Execute(null);
        Pump();
        running = today.Goals.Single(g => g.IsTimerRunning);
    }
    today.ToggleChunksCommand.Execute(running);
    Pump();
    // O cartão do cronômetro, com a lista aberta, fica abaixo da dobra: rola até o começo da lista
    window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("chunks-toggle") && b.DataContext == running)
        .BringIntoView(new Rect(0, 0, 1, 400));
    Pump();
    Capture($"today-chunks-{width}.png");
    var edited = running.Chunks.First(c => c.CanEdit);
    today.EditChunkCommand.Execute(edited);
    Pump();
    // O cartão do cronômetro, com a lista aberta, fica abaixo da dobra: rola até o começo da lista e depois até o fim do
    // editor, para Cancelar e Salvar aparecerem abaixo dos três campos empilhados
    window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("chunks-toggle") && b.DataContext == running)
        .BringIntoView(new Rect(0, 0, 1, 400));
    Pump();
    window.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("inline-panel") && b.IsEffectivelyVisible && b.DataContext == edited)
        .BringIntoView();
    Pump();
    Capture($"today-chunk-edit-{width}.png");
    today.CancelChunkEditCommand.Execute(edited);
    today.ToggleChunksCommand.Execute(running);
    // Painel de lançar tempo, para comparar com o editor do trecho na mesma largura
    var idle = today.Goals.First(g => g.IsDuration && !g.IsTimerRunning);
    today.OpenPanelCommand.Execute(idle);
    Pump();
    window.GetVisualDescendants().OfType<TodayView>().Single()
        .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToHome();
    Pump();
    window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("inline-panel") && b.IsEffectivelyVisible)
        .BringIntoView();
    Pump();
    Capture($"today-panel-{width}.png");
    today.CancelPanelCommand.Execute(idle);
    window.GetVisualDescendants().OfType<TodayView>().Single()
        .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToHome();
    Pump();
    // Modo de troca: o campo vazio com Cancelar e Trocar
    running = today.Goals.Single(g => g.IsTimerRunning);
    today.BeginSwitchTaskCommand.Execute(running);
    Pump();
    Capture($"today-timer-switch-{width}.png");
    today.CancelSwitchTaskCommand.Execute(running);
    Pump();
    // Cronômetro parado: a conferência em h, min e s. O tempo é de exemplo (o relógio aqui é o real); o cronômetro
    // é descartado depois, e a próxima largura começa outro.
    today.StopTimerCommand.Execute(running);
    Pump();
    var stopped = today.Goals.Single(g => g.IsTimerStopped);
    (stopped.StoppedHoursInput, stopped.StoppedMinutesInput, stopped.StoppedSecondsInput) = (1, 25, 40);
    window.GetVisualDescendants().OfType<TodayView>().Single()
        .GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToHome();
    Pump();
    window.GetVisualDescendants().OfType<Border>()
        .Single(b => b.Classes.Contains("inline-panel") && b.IsEffectivelyVisible && b.DataContext == stopped)
        .BringIntoView();
    Pump();
    Capture($"today-timer-stopped-{width}.png");
    today.DiscardTimerCommand.Execute(stopped);
    Pump();
    window.Close();

    void Capture(string name)
    {
        var path = Path.Combine(outDir, name);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Sem quadro para capturar.");
        using (var stream = File.Create(path))
        {
#pragma warning disable CS0618 // a sobrecarga nova exige opções de codificação que não importam aqui
            frame.Save(stream);
#pragma warning restore CS0618
        }
        Console.WriteLine(path);
    }
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
    // O quadro de tags: um chip marcado e outro digitado, para ver os dois estados e a quebra de linha.
    goals.Editor!.Tags.ReloadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
    goals.Editor.Tags.ToggleCommand.Execute(goals.Editor.Tags.Chips[0]);
    goals.Editor.Tags.NewName = "organização da casa";
    goals.Editor.Tags.CreateCommand.Execute(null);
    Pump();
    Capture("goal-edit");
    // O quadro de tags inteiro, rolado até ele
    window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("tag-box")).BringIntoView();
    Pump();
    Capture("goal-edit-tags");
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
    foreach (var section in new[] { SettingsSection.Reminders, SettingsSection.Tags, SettingsSection.Window, SettingsSection.General, SettingsSection.About })
    {
        settings.OpenSectionCommand.Execute(section);
        if (settings.Section != section)
            continue;
        Pump();
        Capture($"settings-{section.ToString().ToLowerInvariant()}");
        if (section == SettingsSection.Tags)
            CaptureTagStates();
        settings.CloseSectionCommand.Execute(null);
    }

    window.Close();

    // Uma linha em cada estado (cores, edição com erro, junção, exclusão) e o campo de nova tag.
    void CaptureTagStates()
    {
        var tags = settings.Tags!;
        tags.LoadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        tags.Rows[0].TogglePickerCommand.Execute(null);
        tags.Rows[1].BeginEditCommand.Execute(null);
        tags.Rows[1].EditText = "a".PadRight(25, 'a');
        tags.Rows[1].ConfirmEditCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        tags.Rows[2].BeginEditCommand.Execute(null);
        tags.Rows[2].EditText = "estudos";
        tags.Rows[2].ConfirmEditCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        tags.Rows[3].BeginDeleteCommand.Execute(null);
        tags.BeginCreateCommand.Execute(null);
        tags.NewName = "organização da casa";
        Pump();
        Capture("settings-tags-edit");
    }

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
