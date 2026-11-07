using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SlimeGoals.App.Account;

namespace SlimeGoals.App.Views;

public partial class AccountView : UserControl
{
    private AccountViewModel? _vm;

    public AccountView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm = DataContext as AccountViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    // O mesmo ScrollViewer serve todas as subpáginas: ao trocar de página, volta ao topo.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountViewModel.Page))
            Scroller.ScrollToHome();
    }

    /// <summary>"Copiar": leva o código mostrado à área de transferência do sistema. Nunca lança.</summary>
    private async void OnCopyRecoveryCode(object? sender, RoutedEventArgs e)
    {
        if (_vm?.ShownRecoveryCode is not { } code || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        try
        {
            await clipboard.SetTextAsync(code);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Falha ao copiar o código de recuperação: {ex.GetType().FullName}");
        }
    }
}
