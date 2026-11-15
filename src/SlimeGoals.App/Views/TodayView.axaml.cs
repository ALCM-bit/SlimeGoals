using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Views;

public partial class TodayView : UserControl
{
    public TodayView()
    {
        InitializeComponent();
        // Depois do AutoCompleteBox: o Enter que escolhe uma sugestão já pôs o texto no campo quando chega aqui.
        AddHandler(KeyDownEvent, OnTaskKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, OnTaskLostFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>O cartão e a página do campo da tarefa em que o evento nasceu, ou nada se veio de outro controle.</summary>
    private (TodayPageViewModel Page, TodayGoalViewModel Item)? TaskField(object? source) =>
        (source as Visual)?.FindAncestorOfType<AutoCompleteBox>(includeSelf: true) is { } box
        && box.Classes.Contains("timer-task") && box.DataContext is TodayGoalViewModel item
        && DataContext is TodayPageViewModel page
            ? (page, item)
            : null;

    /// <summary>Enter grava o nome; no modo de troca, confirma a troca.</summary>
    private void OnTaskKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || TaskField(e.Source) is not var (page, item))
            return;
        e.Handled = true;
        if (item.IsSwitchingTask)
            page.ConfirmSwitchTaskCommand.Execute(item);
        else
            page.SaveTaskCommand.Execute(item);
    }

    /// <summary>Sair do campo grava o nome (fora do modo de troca, que tem os próprios botões).</summary>
    private void OnTaskLostFocus(object? sender, RoutedEventArgs e)
    {
        if (TaskField(e.Source) is var (page, item))
            page.SaveTaskCommand.Execute(item);
    }

    /// <summary>Escolher uma sugestão grava na hora, como o Enter.</summary>
    private void OnTaskSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && TaskField(sender) is var (page, item))
            page.SaveTaskCommand.Execute(item);
    }
}
