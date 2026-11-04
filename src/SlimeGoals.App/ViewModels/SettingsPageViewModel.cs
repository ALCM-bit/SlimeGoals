using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.App.About;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.ViewModels;

/// <summary>Uma opção do adiar na lista de Configurações.</summary>
public sealed record SnoozeOptionViewModel(int Minutes, string Label);

/// <summary>
/// Tela Configurações (spec RF08): cada mudança grava na hora; se a gravação falhar, o valor volta ao anterior
/// e a tela mostra o erro. Carregar não grava nada.
/// </summary>
public sealed partial class SettingsPageViewModel(AppSettings settings, IAutostart? autostart = null,
    PlatformCapabilities? capabilities = null, IReminderPermissions? permissions = null,
    DataChangeNotifier? changes = null, string? appVersion = null) : PageViewModel
{
    private readonly PlatformCapabilities _capabilities = capabilities ?? PlatformCapabilities.Desktop;

    private const string SaveError = "Não foi possível salvar a configuração.";
    private const string AutostartError = "Não foi possível alterar o início com o sistema.";

    private bool _loading;
    private bool _subscribed;
    private SynchronizationContext? _ui;

    public override string Title => "Configurações";

    /// <summary>Versão do app: a informada, ou a do assembly sem o sufixo depois de "+".</summary>
    public string VersionText { get; } = "SlimeGoals " + (appVersion ?? DefaultVersion());

    public string LicenseText => "Licença: PolyForm Noncommercial 1.0.0";

    public IReadOnlyList<LicenseEntry> Licenses => LicenseCatalog.All;

    [ObservableProperty] public partial bool LicensesOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLicenseText), nameof(IsLicenseTextOpen))]
    public partial LicenseEntry? SelectedLicense { get; set; }

    /// <summary>A lista de licenças está aberta e nenhum texto foi escolhido.</summary>
    public bool IsLicenseListOpen => LicensesOpen && SelectedLicense is null;

    public bool IsLicenseTextOpen => SelectedLicense is not null;

    /// <summary>Página principal (seções + Sobre): nem a lista nem o texto de licença estão abertos.</summary>
    public bool IsMainOpen => !LicensesOpen;

    public string? SelectedLicenseText => SelectedLicense is null ? null : ReadLicense(SelectedLicense);

    private static string DefaultVersion()
    {
        var info = typeof(SettingsPageViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }

    private static string ReadLicense(LicenseEntry entry)
    {
        try
        {
            return LicenseTexts.Read(entry.File);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao ler a licença {entry.File}: {ex}");
            return "Não foi possível abrir a licença.";
        }
    }

    [RelayCommand]
    private void OpenLicenses() => LicensesOpen = true;

    [RelayCommand]
    private void CloseLicenses()
    {
        SelectedLicense = null;
        LicensesOpen = false;
    }

    [RelayCommand]
    private void SelectLicense(LicenseEntry entry) => SelectedLicense = entry;

    [RelayCommand]
    private void CloseLicenseText() => SelectedLicense = null;

    public override bool TryGoBack()
    {
        if (SelectedLicense is not null)
            CloseLicenseText();
        else if (LicensesOpen)
            CloseLicenses();
        else
            return false;
        return true;
    }

    partial void OnLicensesOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLicenseListOpen));
        OnPropertyChanged(nameof(IsMainOpen));
    }

    partial void OnSelectedLicenseChanged(LicenseEntry? value) => OnPropertyChanged(nameof(IsLicenseListOpen));

    public override string IconKey => "NavSettings";

    /// <summary>Seção "Geral" (início com o sistema).</summary>
    public bool ShowGeneral => _capabilities.Autostart;

    /// <summary>Seção "Lembretes" (tempo de adiar).</summary>
    public bool ShowReminders => _capabilities.Reminders;

    /// <summary>Seção "Janela" (ao fechar).</summary>
    public bool ShowWindow => _capabilities.WindowClose;

    /// <summary>Nenhuma seção se aplica à plataforma: a tela mostra um aviso no lugar.</summary>
    public bool IsEmpty => !ShowGeneral && !ShowReminders && !ShowWindow;

    /// <summary>Linhas "Notificações" e "Alarmes exatos" da seção Lembretes (só com a porta de permissões).</summary>
    public bool ShowPermissions => permissions is not null && _capabilities.Reminders;

    public string EmptyMessage => "Nada para configurar por enquanto.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationsText), nameof(CanRequestNotifications))]
    public partial bool NotificationsAllowed { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExactAlarmsText), nameof(CanRequestExactAlarms))]
    public partial bool ExactAlarmsAllowed { get; set; } = true;

    public string NotificationsText => NotificationsAllowed ? "Permitidas" : "Bloqueadas";

    public string ExactAlarmsText => ExactAlarmsAllowed ? "Permitidos" : "Bloqueados";

    public bool CanRequestNotifications => !NotificationsAllowed;

    public bool CanRequestExactAlarms => !ExactAlarmsAllowed;

    [RelayCommand]
    private void RequestNotifications() => permissions?.RequestNotifications();

    [RelayCommand]
    private void RequestExactAlarms() => permissions?.RequestExactAlarms();

    private void RefreshPermissions()
    {
        if (permissions is null)
            return;
        var status = permissions.Get();
        (NotificationsAllowed, ExactAlarmsAllowed) = (status.Notifications, status.ExactAlarms);
    }

    public bool CanAutostart => autostart is not null;

    public string? AutostartUnavailableText => autostart is null ? "Indisponível neste sistema." : null;

    public IReadOnlyList<SnoozeOptionViewModel> SnoozeOptions { get; } =
        [.. AppSettings.SnoozeOptions.Select(m => new SnoozeOptionViewModel(m, m == 60 ? "1 h" : $"{m} min"))];

    [ObservableProperty] public partial bool StartWithSystem { get; set; }

    [ObservableProperty] public partial SnoozeOptionViewModel? SelectedSnooze { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloseQuits), nameof(CloseHint))]
    public partial bool CloseHides { get; set; } = true;

    /// <summary>Segmento "Encerrar o app" (RadioButton, dois sentidos); par de <see cref="CloseHides"/>.</summary>
    public bool CloseQuits
    {
        get => !CloseHides;
        set
        {
            if (value)
                CloseHides = false;
        }
    }

    public string CloseHint => CloseHides
        ? "Os lembretes continuam com a janela fechada."
        : "Com o app fechado, os lembretes param.";

    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    /// <summary>Última gravação disparada por uma mudança na tela (para quem precisa esperar o fim dela).</summary>
    public Task LastSave { get; private set; } = Task.CompletedTask;

    public override void Activate() => LoadCommand.Execute(null);

    [RelayCommand]
    private async Task LoadAsync()
    {
        _ui ??= SynchronizationContext.Current;
        if (!_subscribed && changes is not null)
        {
            // O aviso pode chegar de qualquer thread (ex.: o app volta das configurações do sistema).
            _subscribed = true;
            changes.Changed += (_, _) =>
            {
                if (_ui is null)
                    RefreshPermissions();
                else
                    _ui.Post(_ => RefreshPermissions(), null);
            };
        }
        RefreshPermissions();
        ErrorMessage = null;
        try
        {
            var snooze = await settings.GetSnoozeMinutesAsync();
            var close = await settings.GetCloseActionAsync();
            var enabled = autostart?.IsEnabled() ?? false;
            _loading = true;
            try
            {
                StartWithSystem = enabled;
                SelectedSnooze = SnoozeOptions.FirstOrDefault(o => o.Minutes == snooze);
                CloseHides = close == CloseAction.Hide;
            }
            finally
            {
                _loading = false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao carregar as configurações: {ex}");
            ErrorMessage = "Não foi possível carregar a configuração.";
        }
    }

    partial void OnStartWithSystemChanged(bool oldValue, bool newValue)
    {
        if (_loading || autostart is null)
            return;
        ErrorMessage = null;
        try
        {
            if (newValue)
                autostart.Enable();
            else
                autostart.Disable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao alterar o início com o sistema: {ex}");
            Revert(() => StartWithSystem = oldValue);
            ErrorMessage = AutostartError;
        }
    }

    partial void OnSelectedSnoozeChanged(SnoozeOptionViewModel? oldValue, SnoozeOptionViewModel? newValue)
    {
        if (_loading || newValue is null)
            return;
        LastSave = SaveAsync(() => settings.SetSnoozeMinutesAsync(newValue.Minutes), () => SelectedSnooze = oldValue);
    }

    partial void OnCloseHidesChanged(bool oldValue, bool newValue)
    {
        if (_loading)
            return;
        LastSave = SaveAsync(
            () => settings.SetCloseActionAsync(newValue ? CloseAction.Hide : CloseAction.Quit),
            () => CloseHides = oldValue);
    }

    private async Task SaveAsync(Func<Task> save, Action revert)
    {
        ErrorMessage = null;
        try
        {
            await save();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao salvar a configuração: {ex}");
            Revert(revert);
            ErrorMessage = SaveError;
        }
    }

    private void Revert(Action revert)
    {
        _loading = true;
        try
        {
            revert();
        }
        finally
        {
            _loading = false;
        }
    }
}
