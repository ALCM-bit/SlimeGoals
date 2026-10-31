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
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty]
    private PageViewModel _currentPage;

    [RelayCommand]
    private void Navigate(PageViewModel page) => CurrentPage = page;
}
