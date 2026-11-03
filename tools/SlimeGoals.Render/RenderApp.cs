using Avalonia;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using SlimeGoals.App.Converters;
using SlimeGoals.App.ViewModels;
using SlimeGoals.App.Views;

namespace SlimeGoals.Render;

/// <summary>Aplicação mínima que carrega o tema, os estilos e os DataTemplates do App (iguais aos do App.axaml).</summary>
public sealed class RenderApp : Application
{
    private static readonly Uri Base = new("avares://SlimeGoals.Render");

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Resources.MergedDictionaries.Add(new ResourceInclude(Base)
        {
            Source = new Uri("avares://SlimeGoals.App/Styles/Theme.axaml"),
        });
        Resources["IconGeometry"] = new IconKeyToGeometryConverter();
        Resources["GoalColorBrush"] = new GoalColorToBrushConverter();

        DataTemplates.Add(new FuncDataTemplate<TodayPageViewModel>((_, _) => new TodayView()));
        DataTemplates.Add(new FuncDataTemplate<GoalsPageViewModel>((_, _) => new GoalsView()));
        DataTemplates.Add(new FuncDataTemplate<ReportsPageViewModel>((_, _) => new ReportsView()));
        DataTemplates.Add(new FuncDataTemplate<SettingsPageViewModel>((_, _) => new SettingsView()));
        DataTemplates.Add(new FuncDataTemplate<GoalEditViewModel>((_, _) => new GoalEditView()));

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(Base) { Source = new Uri("avares://SlimeGoals.App/Styles/Controls.axaml") });
    }
}
