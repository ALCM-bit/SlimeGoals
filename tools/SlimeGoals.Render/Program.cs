using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SlimeGoals.App.ViewModels;
using SlimeGoals.App.Views;
using SlimeGoals.Render;
using ScrollViewer = Avalonia.Controls.ScrollViewer;
using Window = Avalonia.Controls.Window;

if (args.Length < 1 || args.Length > 2 || (args.Length == 2 && args[1] is not ("phone" or "desktop" or "all" or "mascot" or "narrow")))
{
    Console.Error.WriteLine("Uso: dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all|mascot|narrow]");
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
}
if (which == "mascot")
    RenderMascot();
return 0;

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
