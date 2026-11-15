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
        AddHandler(PointerPressedEvent, (_, _) => _pointerDown = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Depois do Click do botão solto (que roda na bolha, antes de chegar aqui).
        AddHandler(PointerReleasedEvent, OnPointerReleasedAfterClick, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private bool _pointerDown;
    private readonly List<Action> _afterRelease = [];

    /// <summary>
    /// Roda <paramref name="action"/> agora ou, com o dedo (ou o botão do mouse) ainda pressionado, só depois de soltar.
    /// Recarregar os cartões ou devolver o destaque no meio do toque tira o botão de baixo do dedo, e o toque se perde.
    /// </summary>
    private void AfterRelease(Action action)
    {
        if (_pointerDown)
            _afterRelease.Add(action);
        else
            action();
    }

    private void OnPointerReleasedAfterClick(object? sender, PointerReleasedEventArgs e)
    {
        _pointerDown = false;
        if (_afterRelease.Count == 0)
            return;
        var pending = _afterRelease.ToArray();
        _afterRelease.Clear();
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var action in pending)
                action();
        }, DispatcherPriority.Background);
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
        if (!open)
        {
            // O teclado fecha no toque que tira o foco do campo (■, ⇄): o destaque só volta depois de soltar.
            AfterRelease(() => PageRoot.Classes.Set("keyboard", _inputPane?.State == InputPaneState.Open));
            return;
        }
        PageRoot.Classes.Set("keyboard", true);
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Control focused)
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

    /// <summary>
    /// Sair do campo grava o nome (fora do modo de troca, que tem os próprios botões). Se foi um toque, grava depois de
    /// soltar, no cartão que estiver na tela: ■ e ⇄ já gravam o texto digitado eles mesmos.
    /// </summary>
    private void OnTaskLostFocus(object? sender, RoutedEventArgs e)
    {
        if (TaskField(e.Source) is not var (page, item))
            return;
        var goalId = item.Goal.Id;
        AfterRelease(() =>
        {
            if (page.Goals.FirstOrDefault(g => g.Goal.Id == goalId) is { } current)
                page.SaveTaskCommand.Execute(current);
        });
    }

    /// <summary>
    /// Escolher uma sugestão na lista grava na hora, como o Enter. A escolha fecha a lista antes de mudar a seleção; com
    /// a lista aberta, a seleção mudou porque o texto digitado passou por uma sugestão ("Ler" a caminho de "Ler
    /// capítulo 2"), e gravar ali tiraria o foco no meio da digitação.
    /// </summary>
    private void OnTaskSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && sender is AutoCompleteBox { IsDropDownOpen: false } && TaskField(sender) is var (page, item))
            page.SaveTaskCommand.Execute(item);
    }
}
