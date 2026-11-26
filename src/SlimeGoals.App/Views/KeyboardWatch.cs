using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Threading;

namespace SlimeGoals.App.Views;

/// <summary>
/// O teclado virtual (Android) para as telas: se está aberto e quanto da janela cobre, com um aviso a cada mudança.
/// O Avalonia 12 grava o estado do teclado (<see cref="IInputPane.State"/>) a cada entrega das áreas da janela, mas, do
/// Android 11 em diante, só avisa a troca (<see cref="IInputPane.StateChanged"/>) quando o teclado anima. Ir para segundo
/// plano com o teclado aberto e voltar o fecha sem animação: o estado vira fechado calado, e o espaço reservado para o
/// teclado ficava preso, com a tela encolhida. Por isso o estado é relido também no aviso das áreas da janela
/// (<see cref="IInsetsManager.SafeAreaChanged"/>), que chega em toda entrega.
/// </summary>
/// <param name="isOpen">Lê se o teclado está aberto.</param>
/// <param name="coveredHeight">Lê a altura que o teclado cobre na parte de baixo da janela.</param>
/// <param name="post">Põe uma ação na fila do dispatcher, para rodar depois da entrega em curso.</param>
public sealed class KeyboardWatch(Func<bool> isOpen, Func<double> coveredHeight, Action<Action> post) : IDisposable
{
    private Action? _detach;

    public bool IsOpen { get; private set; }

    /// <summary>A altura que o teclado cobre na parte de baixo da janela; 0 com ele fechado.</summary>
    public double Height { get; private set; }

    /// <summary>Avisa quando <see cref="IsOpen"/> ou <see cref="Height"/> mudam.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Acompanha o teclado de <paramref name="topLevel"/>; nada, se a plataforma não tem teclado virtual. Não lê o estado:
    /// quem chama assina <see cref="Changed"/> e depois chama <see cref="Refresh"/>.
    /// </summary>
    public static KeyboardWatch? Attach(TopLevel topLevel)
    {
        if (topLevel.InputPane is not { } pane)
            return null;
        var watch = new KeyboardWatch(
            () => pane.State == InputPaneState.Open,
            () => pane.OccludedRect.Height,
            action => Dispatcher.UIThread.Post(action));
        EventHandler<InputPaneStateEventArgs> onState = (_, _) => watch.Refresh();
        EventHandler<SafeAreaChangedArgs> onInsets = (_, _) => watch.OnInsetsChanged();
        var insets = topLevel.InsetsManager;
        pane.StateChanged += onState;
        if (insets is not null)
            insets.SafeAreaChanged += onInsets;
        watch._detach = () =>
        {
            pane.StateChanged -= onState;
            if (insets is not null)
                insets.SafeAreaChanged -= onInsets;
        };
        return watch;
    }

    /// <summary>Relê o estado do teclado e avisa se mudou.</summary>
    public void Refresh()
    {
        var open = isOpen();
        var height = open ? coveredHeight() : 0;
        if (open == IsOpen && height == Height)
            return;
        IsOpen = open;
        Height = height;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// As áreas da janela mudaram. O Avalonia avisa antes de gravar o estado novo do teclado, então a releitura espera a
    /// fila do dispatcher.
    /// </summary>
    public void OnInsetsChanged() => post(Refresh);

    /// <summary>Para de acompanhar o teclado; uma releitura ainda na fila não avisa mais ninguém.</summary>
    public void Dispose()
    {
        _detach?.Invoke();
        _detach = null;
        Changed = null;
    }
}
