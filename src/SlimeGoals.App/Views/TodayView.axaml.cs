using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

    private IInputPane? _inputPane;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inputPane = TopLevel.GetTopLevel(this)?.InputPane;
        if (_inputPane is not null)
            _inputPane.StateChanged += OnInputPaneChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_inputPane is not null)
            _inputPane.StateChanged -= OnInputPaneChanged;
        _inputPane = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Teclado do celular aberto: esconde o destaque e os blocos (classe <c>keyboard</c>) e rola até o campo em edição,
    /// que de outro modo ficaria atrás do teclado.
    /// </summary>
    private void OnInputPaneChanged(object? sender, InputPaneStateEventArgs e)
    {
        var open = e.NewState == InputPaneState.Open;
        PageRoot.Classes.Set("keyboard", open);
        if (open && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control focused)
            Dispatcher.UIThread.Post(() => focused.BringIntoView(), DispatcherPriority.Loaded);
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
