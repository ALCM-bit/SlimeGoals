using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SlimeGoals.App.Views;

/// <summary>
/// Chip de uma tag (<c>TagChipViewModel</c> como contexto), reaproveitado no quadro da meta, em Configurações e no
/// filtro do Hoje. Ao tocar, executa <see cref="Command"/> com o chip como parâmetro.
/// </summary>
public partial class TagChip : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<TagChip, ICommand?>(nameof(Command));

    public TagChip() => InitializeComponent();

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (Command is { } command && command.CanExecute(DataContext))
            command.Execute(DataContext);
    }
}
