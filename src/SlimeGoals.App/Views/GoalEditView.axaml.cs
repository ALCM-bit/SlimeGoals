using Avalonia.Controls;

namespace SlimeGoals.App.Views;

public partial class GoalEditView : UserControl
{
    public GoalEditView()
    {
        InitializeComponent();
        Form.AddHandler(RequestBringIntoViewEvent, OnRequestBringIntoView);
    }

    /// <summary>
    /// Ao aplicar a seleção inicial, as listas de ícone e cor pedem para mostrar o item selecionado, e a rolagem
    /// do formulário atenderia: ele abriria rolado, sem o campo Título. Só vale o pedido de um item com foco
    /// (teclado ou clique).
    /// </summary>
    private static void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (e.TargetObject is ListBoxItem { IsKeyboardFocusWithin: false })
            e.Handled = true;
    }
}
