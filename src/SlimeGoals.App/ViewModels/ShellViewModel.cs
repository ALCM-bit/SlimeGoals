using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.Hosting;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Raiz da navegação. Só conhece a lista de páginas e a página atual;
/// a forma visual (barra lateral ou inferior) fica a cargo de cada View.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IAppLifetime? _lifetime;

    private readonly PlatformCapabilities _capabilities;

    public ShellViewModel(IEnumerable<PageViewModel> pages, IAppLifetime? lifetime = null,
        PlatformCapabilities? capabilities = null)
    {
        _lifetime = lifetime;
        _capabilities = capabilities ?? PlatformCapabilities.Desktop;
        Pages = pages.ToList();
        if (Pages.Count == 0)
            throw new ArgumentException("O shell precisa de ao menos uma página.", nameof(pages));
        _currentPage = Pages[0];
        _currentPage.Activate();
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    private PageViewModel _currentPage;

    /// <summary>
    /// Página atual. Ignora null: o ListBox pode desmarcar o item (Ctrl+clique) e o binding
    /// TwoWay tentaria apagar a página. Notifica mesmo assim para a View voltar à seleção válida.
    /// </summary>
    public PageViewModel CurrentPage
    {
        get => _currentPage;
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }
            if (SetProperty(ref _currentPage, value))
                value.Activate();
        }
    }

    /// <summary>"Sair" só aparece onde a plataforma permite encerrar o app.</summary>
    public bool ShowQuit => _capabilities.Quit;

    /// <summary>
    /// Botão Voltar do sistema: fecha o editor de meta aberto; senão volta para Hoje.
    /// Devolve falso em Hoje, para o sistema tratar (segundo plano).
    /// </summary>
    public bool GoBack()
    {
        if (CurrentPage is GoalsPageViewModel goals && goals.CloseEditorIfOpen())
            return true;
        if (!ReferenceEquals(CurrentPage, Pages[0]))
        {
            CurrentPage = Pages[0];
            return true;
        }
        return false;
    }

    [RelayCommand]
    private void Navigate(PageViewModel page) => CurrentPage = page;

    /// <summary>"Sair": encerra o processo (fechar a janela só a oculta). Sem lifetime, fica desabilitado.</summary>
    [RelayCommand(CanExecute = nameof(CanQuit))]
    private void Quit() => _lifetime?.Quit();

    private bool CanQuit() => _lifetime is not null;
}
