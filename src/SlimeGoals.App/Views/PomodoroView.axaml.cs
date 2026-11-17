using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Views;

/// <summary>
/// Subaba Pomodoro. O campo da subatividade se comporta como o do cartão do cronômetro (spec das tarefas §7.1): Enter
/// grava (no modo de troca, troca), Esc desiste, escolher uma sugestão grava e ✎ põe o foco no campo.
/// </summary>
public partial class PomodoroView : UserControl
{
    public PomodoroView()
    {
        InitializeComponent();
        // Depois do AutoCompleteBox: o Enter que escolhe uma sugestão já pôs o texto no campo quando chega aqui.
        TaskField.AddHandler(KeyDownEvent, OnTaskKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private PomodoroViewModel? Session => DataContext as PomodoroViewModel;

    private void OnTaskKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape) || Session is not { } vm)
            return;
        if (e.Key == Key.Escape)
        {
            if (vm.IsSwitchingTask)
                Run(vm.CancelSwitchTaskCommand);
            else if (vm.IsEditingTask)
                Run(vm.CancelEditTaskCommand);
            else
                return;
            e.Handled = true;
            return;
        }
        e.Handled = true;
        if (vm.IsSwitchingTask)
            Run(vm.SwitchTaskCommand);
        else
            Run(vm.SaveTaskCommand);
    }

    /// <summary>
    /// Escolher uma sugestão na lista grava na hora, como o Enter. Com a lista aberta, a seleção mudou porque o texto
    /// digitado passou por uma sugestão, e gravar ali tiraria o foco no meio da digitação.
    /// </summary>
    private void OnTaskSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && sender is AutoCompleteBox { IsDropDownOpen: false } && Session is { IsSwitchingTask: false } vm)
            Run(vm.SaveTaskCommand);
    }

    /// <summary>Como um botão: o comando só roda se puder agora (ex.: nada a gravar, ou a sessão ocupada).</summary>
    private static void Run(ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }

    /// <summary>✎ põe o foco no campo que aparece no lugar do nome, depois de ele ficar visível.</summary>
    private void OnEditTaskClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (TaskField.IsEffectivelyVisible)
                TaskField.Focus();
        }, DispatcherPriority.Background);
}
