using CommunityToolkit.Mvvm.ComponentModel;

namespace SlimeGoals.App.ViewModels;

/// <summary>Base de toda página navegável pelo shell (barra lateral no desktop, navegação inferior no Android).</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Chamado pelo shell quando a página passa a ser exibida (ex.: recarregar dados).</summary>
    public virtual void Activate() { }
}
