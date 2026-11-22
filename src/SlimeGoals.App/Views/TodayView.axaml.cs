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
        // A roda do mouse rola a linha de filtros de lado (o desktop não tem como arrastar com o dedo).
        TagStrip.PointerWheelChanged += OnTagStripWheel;
        TagStrip.ScrollChanged += (_, _) => UpdateTagStripHint();
        AddHandler(PointerPressedEvent, (_, _) => _pointerDown = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Depois do Click do botão solto (que roda na bolha, antes de chegar aqui).
        AddHandler(PointerReleasedEvent, OnPointerReleasedAfterClick, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnTagStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0 || sender is not ScrollViewer strip)
            return;
        var max = Math.Max(0, strip.Extent.Width - strip.Viewport.Width);
        strip.Offset = strip.Offset.WithX(Math.Clamp(strip.Offset.X - e.Delta.Y * 48, 0, max));
        e.Handled = true;
    }

    private void UpdateTagStripHint()
    {
        var more = TagStripScroll.CanScrollRight(TagStrip.Offset.X, TagStrip.Extent.Width, TagStrip.Viewport.Width);
        TagStripFade.IsVisible = TagStripMore.IsVisible = more;
    }

    private void OnTagStripMore(object? sender, RoutedEventArgs e) =>
        TagStrip.Offset = TagStrip.Offset.WithX(TagStripScroll.NextOffset(TagStrip.Offset.X, TagStrip.Extent.Width, TagStrip.Viewport.Width));

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

    /// <summary>
    /// Enter grava o nome (no modo de troca, confirma a troca); Esc desiste da troca ou da edição. Sair do campo não grava:
    /// o texto fica como rascunho, com o ✓ à vista (spec §7.1), e ■ e ⇄ usam o que foi digitado.
    /// </summary>
    private void OnTaskKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape) || TaskField(e.Source) is not var (page, item))
            return;
        if (e.Key == Key.Escape)
        {
            if (item.IsSwitchingTask)
                page.CancelSwitchTaskCommand.Execute(item);
            else if (item.IsEditingTask)
                page.CancelEditTaskCommand.Execute(item);
            else
                return;
            e.Handled = true;
            return;
        }
        e.Handled = true;
        if (item.IsSwitchingTask)
            page.ConfirmSwitchTaskCommand.Execute(item);
        else
            page.SaveTaskCommand.Execute(item);
    }

    /// <summary>
    /// ✎ põe o foco no campo que aparece no lugar do nome. O Click roda antes do comando, então o foco espera o campo
    /// ficar visível; com o dedo ainda na tela, espera também soltar, porque o teclado que abre muda a página de lugar.
    /// </summary>
    private void OnEditTaskClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Visual)?.FindAncestorOfType<Grid>()?.Parent is not Panel area)
            return;
        AfterRelease(() => Dispatcher.UIThread.Post(() =>
            area.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault(b => b.Classes.Contains("timer-task"))?.Focus(),
            DispatcherPriority.Background));
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
