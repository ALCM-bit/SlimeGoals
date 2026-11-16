using Avalonia;
using Avalonia.Controls;
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

        DataTemplates.Add(Template<TodayPageViewModel>(() => new TodayView()));
        DataTemplates.Add(Template<GoalsPageViewModel>(() => new GoalsView()));
        DataTemplates.Add(Template<TimePageViewModel>(() => new TimeView()));
        DataTemplates.Add(Template<ReportsPageViewModel>(() => new ReportsView()));
        DataTemplates.Add(Template<SettingsPageViewModel>(() => new SettingsView()));
        DataTemplates.Add(Template<GoalEditViewModel>(() => new GoalEditView()));

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(Base) { Source = new Uri("avares://SlimeGoals.App/Styles/Controls.axaml") });
    }

    /// <summary>
    /// Como o <c>DataTemplate DataType=</c> do App.axaml, só casa com uma instância do tipo. O
    /// <c>FuncDataTemplate&lt;T&gt;</c> também casa com nulo, e um ComboBox sem seleção desenharia a primeira página.
    /// </summary>
    private static FuncDataTemplate Template<T>(Func<Control> view) => new(o => o is T, (_, _) => view());
}
