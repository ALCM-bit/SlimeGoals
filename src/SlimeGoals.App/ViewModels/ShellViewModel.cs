using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Raiz da navegação. Só conhece a lista de páginas e a página atual;
/// a forma visual (barra lateral ou inferior) fica a cargo de cada View.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(IEnumerable<PageViewModel> pages)
    {
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

    [RelayCommand]
    private void Navigate(PageViewModel page) => CurrentPage = page;
}
