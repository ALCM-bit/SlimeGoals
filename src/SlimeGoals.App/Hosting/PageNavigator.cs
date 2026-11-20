using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Pedido de troca de página vindo de dentro de uma página (ex.: "Ir para o Hoje" na aba Tempo). Existe porque o
/// <see cref="ShellViewModel"/> recebe as páginas: uma página que recebesse o shell criaria um ciclo no DI.
/// O shell assina <see cref="Requested"/> e troca a página atual na mesma thread de quem pediu.
/// </summary>
public sealed class PageNavigator
{
    public event Action<Type>? Requested;

    /// <summary>Pedido para a página de configurações abrir um grupo; dispara antes de <see cref="Requested"/>.</summary>
    public event Action<SettingsSection>? SettingsRequested;

    public void Show<T>() where T : PageViewModel => Requested?.Invoke(typeof(T));

    /// <summary>Mostra as configurações já com o grupo aberto.</summary>
    public void ShowSettings(SettingsSection section)
    {
        SettingsRequested?.Invoke(section);
        Show<SettingsPageViewModel>();
    }
}
