using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using SlimeGoals.App.ViewModels;
using SlimeGoals.App.Views;
using SlimeGoals.Render;
using Window = Avalonia.Controls.Window;

if (args.Length < 1 || args.Length > 2 || (args.Length == 2 && args[1] is not ("phone" or "desktop" or "all")))
{
    Console.Error.WriteLine("Uso: dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all]");
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
return 0;

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
