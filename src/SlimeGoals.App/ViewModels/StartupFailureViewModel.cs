using CommunityToolkit.Mvvm.Input;

namespace SlimeGoals.App.ViewModels;

/// <summary>
/// Tela de erro de quando o banco não abre nem migra. Só avisa: nada aqui apaga ou conserta o arquivo.
/// Fechar é com o head: a janela no desktop, a Activity no Android.
/// </summary>
public sealed partial class StartupFailureViewModel(string databasePath)
{
    public string Message => "Não foi possível abrir os dados do app.";

    public string DatabasePath { get; } = databasePath;

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
