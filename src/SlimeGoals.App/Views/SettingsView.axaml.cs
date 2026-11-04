using System.ComponentModel;
using Avalonia.Controls;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Views;

public partial class SettingsView : UserControl
{
    private SettingsPageViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = DataContext as SettingsPageViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    // O mesmo ScrollViewer serve todas as licenças: ao trocar o texto, volta ao topo.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPageViewModel.SelectedLicenseText))
            LicenseTextScroller.ScrollToHome();
    }
}
