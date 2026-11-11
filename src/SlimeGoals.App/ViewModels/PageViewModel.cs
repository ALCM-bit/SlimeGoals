using CommunityToolkit.Mvvm.ComponentModel;

namespace SlimeGoals.App.ViewModels;

/// <summary>Base de toda página navegável pelo shell (barra lateral no desktop, navegação inferior no Android).</summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Chave do ícone da navegação inferior (recurso <c>Icon{IconKey}</c> do tema); vazio = sem ícone.</summary>
    public virtual string IconKey => "";

    /// <summary>Chamado pelo shell quando a página passa a ser exibida (ex.: recarregar dados).</summary>
    public virtual void Activate() { }

    /// <summary>Chamado pelo shell quando a página deixa de ser exibida (ex.: esquecer o que foi digitado de sensível).</summary>
    public virtual void Deactivate() { }

    /// <summary>Voltar do sistema: fecha o que a página tem aberto por dentro. Falso se não havia nada a fechar.</summary>
    public virtual bool TryGoBack() => false;
}
