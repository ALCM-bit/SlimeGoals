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

    /// <summary>
    /// Pedido para a página de configurações abrir um grupo; dispara antes de <see cref="Requested"/>.
    /// O segundo argumento é a página para onde o "voltar" do grupo leva (nulo: a lista de grupos).
    /// </summary>
    public event Action<SettingsSection, Type?>? SettingsRequested;

    public void Show<T>() where T : PageViewModel => Requested?.Invoke(typeof(T));

    /// <summary>Mostra a página do tipo dado (o destino de "voltar" guardado como <see cref="Type"/>).</summary>
    public void Show(Type page) => Requested?.Invoke(page);

    /// <summary>Mostra as configurações já com o grupo aberto; <paramref name="returnTo"/> é a página a que se volta dele.</summary>
    public void ShowSettings(SettingsSection section, Type? returnTo = null)
    {
        SettingsRequested?.Invoke(section, returnTo);
        Show<SettingsPageViewModel>();
    }
}
