using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimeGoals.Core.Services;
using SlimeGoals.Sync;
using SlimeGoals.Sync.Account;

namespace SlimeGoals.App.Account;

/// <summary>Subpágina da conta aberta dentro de Configurações; <see cref="None"/> é a seção na página principal.</summary>
public enum AccountPage
{
    None,
    Register,
    RecoveryCode,
    Confirm,
    Login,
    Recover,
    RecoveredCode,
    Reauthenticate,
    ChangePassword,
    Devices,
    SignOut,
    DeleteAccount,
}

/// <summary>Um aparelho da conta na lista; o atual não tem o botão de remover (decisão 12).</summary>
public sealed record DeviceItemViewModel(Guid Id, string Name, bool IsCurrent)
{
    public bool CanRevoke => !IsCurrent;

    public string? CurrentLabel => IsCurrent ? "Este aparelho" : null;
}

/// <summary>
/// Seção "Conta e sincronização" de Configurações e suas subpáginas (fase 9.5a). Toda chamada à conta passa por
/// <see cref="RunAsync"/>: liga <see cref="IsBusy"/>, troca a exceção pelo texto de <see cref="AccountMessages"/> e
/// devolve o controle. Senhas e o código de recuperação digitado saem dos campos a cada envio, ao sair da subpágina e ao
/// sair de Configurações.
/// </summary>
public sealed partial class AccountViewModel : ObservableObject
{
    // Textos fixos da decisão 16.
    public const string IntroText = "Com uma conta, suas metas e registros vão cifrados para o servidor e aparecem nos seus " +
        "outros aparelhos. Ninguém além de você consegue lê-los, nem quem cuida do servidor. Por isso, se você perder a senha " +
        "e o código de recuperação, a cópia da nuvem se perde. Os dados deste aparelho continuam.";
    public const string RecoveryCodeTitle = "Seu código de recuperação";
    public const string RecoveryCodeText = "Guarde este código num lugar seguro. Ele é a única forma de recuperar a conta se " +
        "você esquecer a senha, e não será mostrado de novo.";
    public const string RecoveryCodeSavedLabel = "Guardei o código";
    public const string ContinueLabel = "Continuar";
    public const string CopyLabel = "Copiar";
    public const string WaitLabel = "Esperar";
    public const string SignOutAnywayLabel = "Sair mesmo assim";
    public const string DeleteAccountText = "Isto apaga a conta e todos os dados dela no servidor, para todos os aparelhos. " +
        "As metas e registros deste aparelho continuam aqui.";
    public const string DeleteAccountLabel = "Apagar conta";
    public const string SessionExpiredText = "A sessão deste aparelho foi encerrada (a senha mudou ou o aparelho foi " +
        "removido). Entre de novo para continuar sincronizando. Nada do que está na fila se perde.";
    public const string ReauthenticateLabel = "Entrar de novo";
    public const string VaultLockedText = "O chaveiro do sistema está travado ou indisponível. Destrave-o para continuar " +
        "sincronizando.";
    public const string RetryVaultLabel = "Tentar de novo";
    public const string SessionUnreadableText = "Os dados da sessão neste aparelho estão ilegíveis. Saia da conta e entre de novo.";

    private readonly ISyncAccount _account;
    private readonly AppSettings _settings;
    private readonly Action<Action> _dispatch;
    private readonly TimeProvider _time;
    private PendingRegistration? _pending;
    private string _savedServer = ServerAddress.Display(AppSettings.DefaultSyncServer);
    private int _refreshVersion;

    /// <param name="dispatch">Leva o recarregar do status à thread da interface; padrão, o <see cref="Dispatcher"/> do Avalonia.</param>
    public AccountViewModel(ISyncAccount account, AppSettings settings, AccountDefaults defaults,
        Action<Action>? dispatch = null, TimeProvider? time = null)
    {
        _account = account;
        _settings = settings;
        _dispatch = dispatch ?? (a => Dispatcher.UIThread.Post(a));
        _time = time ?? TimeProvider.System;
        DeviceName = defaults.DeviceName;
        ServerText = _savedServer;
        // O aviso chega de qualquer thread; o recarregar roda na da interface e nunca lança.
        _account.StatusChanged += (_, _) => _dispatch(() => _ = RefreshAsync());
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMainOpen), nameof(ConfirmText), nameof(PageTitle))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial AccountPage Page { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenRegisterCommand), nameof(OpenLoginCommand), nameof(OpenRecoverCommand),
        nameof(OpenReauthenticateCommand), nameof(OpenChangePasswordCommand), nameof(OpenDevicesCommand),
        nameof(OpenSignOutCommand), nameof(OpenDeleteAccountCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitRegisterCommand), nameof(SubmitConfirmCommand), nameof(SubmitLoginCommand),
        nameof(SubmitRecoverCommand), nameof(SubmitReauthenticateCommand), nameof(SubmitChangePasswordCommand),
        nameof(SubmitSignOutCommand), nameof(SubmitDeleteAccountCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand), nameof(CloseCommand), nameof(RetryVaultCommand),
        nameof(RevokeDeviceCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; }

    /// <summary>Texto do último erro, sempre de <see cref="AccountMessages"/> ou das validações da tela.</summary>
    [ObservableProperty] public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut), nameof(IsSignedIn), nameof(IsSessionExpired), nameof(IsVaultLocked),
        nameof(IsSessionUnreadable), nameof(IsServerEditable), nameof(ConnectedText), nameof(ServerLineText),
        nameof(LastSyncText), nameof(PendingText), nameof(LastError))]
    public partial AccountStatus Status { get; private set; } = new(AccountState.SignedOut, null, null, null, 0, null);

    // Campos dos formulários.
    [ObservableProperty] public partial string ServerText { get; set; }
    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string PasswordConfirmation { get; set; } = "";
    [ObservableProperty] public partial string CurrentPassword { get; set; } = "";
    [ObservableProperty] public partial string ConfirmationCode { get; set; } = "";
    [ObservableProperty] public partial string RecoveryCodeInput { get; set; } = "";
    [ObservableProperty] public partial string DeviceName { get; set; }

    /// <summary>Código de recuperação para mostrar; só existe nas páginas dele.</summary>
    [ObservableProperty] public partial string? ShownRecoveryCode { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand), nameof(CloseCommand))]
    public partial bool RecoveryCodeSaved { get; set; }

    [ObservableProperty] public partial IReadOnlyList<DeviceItemViewModel> Devices { get; private set; } = [];

    /// <summary>Itens que ficaram na fila no último "Sair" (decisão 11).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignOutWarningText))]
    public partial int PendingToLose { get; private set; }

    public bool IsIdle => !IsBusy;

    public bool IsMainOpen => Page == AccountPage.None;

    /// <summary>Título da subpágina no cabeçalho de Configurações; nulo na seção principal.</summary>
    public string? PageTitle => Page switch
    {
        AccountPage.Register => "Criar conta",
        AccountPage.Login => "Entrar",
        AccountPage.Recover => "Recuperar conta",
        AccountPage.RecoveryCode or AccountPage.RecoveredCode => "Código de recuperação",
        AccountPage.Confirm => "Confirmar e-mail",
        AccountPage.Reauthenticate => "Entrar de novo",
        AccountPage.ChangePassword => "Trocar senha",
        AccountPage.Devices => "Aparelhos",
        AccountPage.SignOut => "Sair da conta",
        AccountPage.DeleteAccount => "Apagar conta",
        _ => null,
    };

    public bool IsSignedOut => Status.State == AccountState.SignedOut;
    public bool IsSignedIn => Status.State == AccountState.SignedIn;
    public bool IsSessionExpired => Status.State == AccountState.SessionExpired;
    public bool IsVaultLocked => Status.State == AccountState.VaultLocked;
    public bool IsSessionUnreadable => Status.State == AccountState.SessionUnreadable;

    /// <summary>Com a conta ligada vale o servidor da sessão: o campo não muda (decisão 5).</summary>
    public bool IsServerEditable => IsSignedOut;

    public bool IsRegisterOpen => Page == AccountPage.Register;
    public bool IsRecoveryCodeOpen => Page == AccountPage.RecoveryCode;
    public bool IsConfirmOpen => Page == AccountPage.Confirm;
    public bool IsLoginOpen => Page == AccountPage.Login;
    public bool IsRecoverOpen => Page == AccountPage.Recover;
    public bool IsRecoveredCodeOpen => Page == AccountPage.RecoveredCode;
    public bool IsReauthenticateOpen => Page == AccountPage.Reauthenticate;
    public bool IsChangePasswordOpen => Page == AccountPage.ChangePassword;
    public bool IsDevicesOpen => Page == AccountPage.Devices;
    public bool IsSignOutOpen => Page == AccountPage.SignOut;
    public bool IsDeleteAccountOpen => Page == AccountPage.DeleteAccount;

    public string ConfirmText => $"Enviamos um código para {_pending?.Email}. Neste servidor de desenvolvimento, o código " +
        "aparece no log do servidor (docker compose logs server).";

    public string SignOutWarningText => PendingToLose == 1
        ? "Há 1 alteração que ainda não foi enviada. Se sair agora, ela não chega aos outros aparelhos, e metas excluídas " +
          "depois disso podem voltar quando você entrar de novo."
        : $"Há {PendingToLose} alterações que ainda não foram enviadas. Se sair agora, elas não chegam aos outros " +
          "aparelhos, e metas excluídas depois disso podem voltar quando você entrar de novo.";

    public string ConnectedText => $"Conectado como {Status.Email}";

    public string ServerLineText => $"Servidor: {(Status.Server is { } server ? ServerAddress.Display(server) : "")}";

    public string LastSyncText => $"Última sincronização: {FormatMoment(Status.LastSyncAt)}";

    public string PendingText => $"Pendências: {Status.Pending}";

    public string? LastError => Status.LastError;

    /// <summary>Lê o status e, sem conta, o servidor gravado. Nunca lança.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            _savedServer = ServerAddress.Display(await _settings.GetSyncServerAsync());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao ler o servidor de sincronização: {ex.GetType().FullName}");
        }

        await RefreshAsync(reloadServer: true);
    }

    private async Task RefreshAsync(bool reloadServer = false)
    {
        var version = ++_refreshVersion;
        AccountStatus status;
        try
        {
            status = await _account.GetStatusAsync();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Falha ao ler o status da conta: {ex.GetType().FullName}");
            return;
        }

        if (version != _refreshVersion)
            return;
        var wasSignedOut = IsSignedOut;
        Status = status;
        if (status.Server is { } server && !IsSignedOut)
            ServerText = ServerAddress.Display(server);
        else if (IsSignedOut && (reloadServer || !wasSignedOut))
            ServerText = _savedServer;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenRegister() => Page = AccountPage.Register;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenLogin() => Page = AccountPage.Login;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenRecover() => Page = AccountPage.Recover;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenReauthenticate() => Page = AccountPage.Reauthenticate;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenChangePassword() => Page = AccountPage.ChangePassword;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void OpenDeleteAccount() => Page = AccountPage.DeleteAccount;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenDevicesAsync()
    {
        Page = AccountPage.Devices;
        await LoadDevicesAsync();
    }

    /// <summary>Sair em dois passos (decisão 11): envia a fila e só avisa se algo ficou.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task OpenSignOutAsync() => RunAsync(AccountAction.SignOut, async () =>
    {
        var left = await _account.FlushAsync();
        if (left == 0)
        {
            await _account.SignOutAsync();
            Page = AccountPage.None;
            return;
        }

        PendingToLose = left;
        Page = AccountPage.SignOut;
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SubmitRegisterAsync()
    {
        var (email, password, confirmation) = (Email, Password, PasswordConfirmation);
        ClearSecretInputs();
        if (await ServerForSubmitAsync() is not { } server || !PasswordsMatch(password, confirmation))
            return;
        await RunAsync(AccountAction.Register, async () =>
        {
            var pending = await _account.RegisterAsync(server, email, password);
            _pending?.Dispose();
            _pending = pending;
            ShowRecoveryCode(pending.RecoveryCode, AccountPage.RecoveryCode);
        });
    }

    /// <summary>Só com "Guardei o código" marcado: do cadastro vai à confirmação; da recuperação, à seção.</summary>
    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue() => Page = Page == AccountPage.RecoveryCode ? AccountPage.Confirm : AccountPage.None;

    private bool CanContinue() => IsIdle && RecoveryCodeSaved;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SubmitConfirmAsync()
    {
        if (_pending is not { } pending)
            return;
        var (code, deviceName) = (ConfirmationCode, DeviceName);
        await RunAsync(AccountAction.Confirm, async () =>
        {
            await _account.ConfirmAsync(pending, code, deviceName);
            Page = AccountPage.None;
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SubmitLoginAsync()
    {
        var (email, password, deviceName) = (Email, Password, DeviceName);
        ClearSecretInputs();
        if (await ServerForSubmitAsync() is not { } server)
            return;
        await RunAsync(AccountAction.Login, async () =>
        {
            await _account.LoginAsync(server, email, password, deviceName);
            Page = AccountPage.None;
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SubmitRecoverAsync()
    {
        var (email, code, password, confirmation, deviceName) =
            (Email, RecoveryCodeInput, Password, PasswordConfirmation, DeviceName);
        ClearSecretInputs();
        if (await ServerForSubmitAsync() is not { } server || !PasswordsMatch(password, confirmation))
            return;
        await RunAsync(AccountAction.Recover, async () =>
        {
            var newCode = await _account.RecoverAsync(server, email, code, password, deviceName);
            ShowRecoveryCode(newCode, AccountPage.RecoveredCode);
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SubmitReauthenticateAsync()
    {
        var (password, deviceName) = (Password, DeviceName);
        ClearSecretInputs();
        return RunAsync(AccountAction.Reauthenticate, async () =>
        {
            await _account.ReauthenticateAsync(password, deviceName);
            Page = AccountPage.None;
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SubmitChangePasswordAsync()
    {
        var (current, password, confirmation) = (CurrentPassword, Password, PasswordConfirmation);
        ClearSecretInputs();
        if (!PasswordsMatch(password, confirmation))
            return;
        await RunAsync(AccountAction.ChangePassword, async () =>
        {
            await _account.ChangePasswordAsync(current, password);
            Page = AccountPage.None;
        });
    }

    /// <summary>"Sair mesmo assim": a fila que sobrou se perde.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SubmitSignOutAsync() => RunAsync(AccountAction.SignOut, async () =>
    {
        await _account.SignOutAsync();
        Page = AccountPage.None;
    });

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task SubmitDeleteAccountAsync()
    {
        var password = Password;
        ClearSecretInputs();
        return RunAsync(AccountAction.DeleteAccount, async () =>
        {
            await _account.DeleteAccountAsync(password);
            Page = AccountPage.None;
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RevokeDeviceAsync(DeviceItemViewModel? device)
    {
        if (device is not { CanRevoke: true })
            return;
        await RunAsync(AccountAction.Devices, () => _account.RevokeDeviceAsync(device.Id));
        if (Error is null)
            await LoadDevicesAsync();
    }

    /// <summary>Em <see cref="AccountState.VaultLocked"/>: o coordenador só lança no cancelamento, então a ação pouco importa.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private Task RetryVaultAsync() => RunAsync(AccountAction.Reauthenticate, () => _account.RetryVaultAsync());

    /// <summary>
    /// Fecha a subpágina ("Esperar" no aviso de sair); parado enquanto ocupado (decisão 13) e, nas páginas do código de
    /// recuperação, até marcar "Guardei o código" (decisão 16): o código não volta a aparecer.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close() => Page = AccountPage.None;

    private bool CanClose() => IsIdle && !CodeNotYetSaved;

    private bool CodeNotYetSaved =>
        Page is AccountPage.RecoveryCode or AccountPage.RecoveredCode && !RecoveryCodeSaved;

    /// <summary>
    /// Voltar do sistema: falso na seção principal; ocupado, ou com o código de recuperação ainda não guardado, engole o
    /// voltar sem mudar nada.
    /// </summary>
    public bool TryGoBack()
    {
        if (Page == AccountPage.None)
            return false;
        if (CanClose())
            Page = AccountPage.None;
        return true;
    }

    private Task LoadDevicesAsync() => RunAsync(AccountAction.Devices, async () =>
    {
        var devices = await _account.ListDevicesAsync();
        Devices = [.. devices.Select(d => new DeviceItemViewModel(d.Id, d.Name, d.IsCurrent))];
    });

    /// <summary>
    /// Única porta para a conta: uma operação por vez, <see cref="IsBusy"/> ligado durante ela, e a exceção vira o texto
    /// de <see cref="AccountMessages"/>. No Trace, só o tipo da exceção: nada do que ela carrega.
    /// </summary>
    private async Task RunAsync(AccountAction action, Func<Task> op)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        Error = null;
        try
        {
            await op();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Falha na operação de conta {action}: {ex.GetType().FullName}");
            Error = AccountMessages.For(ex, action);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    /// <summary>O servidor do campo, conferido e gravado; inválido, mostra o erro e devolve nulo.</summary>
    private async Task<Uri?> ServerForSubmitAsync()
    {
        if (!ServerAddress.TryParse(ServerText, out var server, out var error))
        {
            Error = error;
            return null;
        }

        try
        {
            await _settings.SetSyncServerAsync(server!);
            _savedServer = ServerAddress.Display(server!);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Não gravar o endereço não impede a operação: ele só volta ao padrão no próximo início.
            Trace.TraceError($"Falha ao gravar o servidor de sincronização: {ex.GetType().FullName}");
        }

        return server;
    }

    private bool PasswordsMatch(string password, string confirmation)
    {
        if (password == confirmation)
            return true;
        Error = AccountMessages.PasswordsDiffer;
        return false;
    }

    private void ShowRecoveryCode(string code, AccountPage page)
    {
        Page = page;
        RecoveryCodeSaved = false;
        ShownRecoveryCode = code;
    }

    /// <summary>Esvazia os campos de senha e o código de recuperação digitado (a cada envio, ao trocar de subpágina e ao
    /// sair de Configurações).</summary>
    public void ClearSecretInputs() =>
        (CurrentPassword, Password, PasswordConfirmation, RecoveryCodeInput) = ("", "", "", "");

    partial void OnPageChanged(AccountPage oldValue, AccountPage newValue)
    {
        ClearSecretInputs();
        Error = null;
        if (oldValue is AccountPage.RecoveryCode or AccountPage.RecoveredCode)
        {
            ShownRecoveryCode = null;
            RecoveryCodeSaved = false;
        }

        if (oldValue == AccountPage.Confirm)
            ConfirmationCode = "";
        // O cadastro pendente só vive do código à confirmação: sair dali zera as chaves dele.
        if (newValue is not (AccountPage.RecoveryCode or AccountPage.Confirm))
        {
            _pending?.Dispose();
            _pending = null;
        }

        if (newValue != AccountPage.Devices)
            Devices = [];
        foreach (var name in PageFlags)
            OnPropertyChanged(name);
    }

    private static readonly string[] PageFlags =
    [
        nameof(IsRegisterOpen), nameof(IsRecoveryCodeOpen), nameof(IsConfirmOpen), nameof(IsLoginOpen),
        nameof(IsRecoverOpen), nameof(IsRecoveredCodeOpen), nameof(IsReauthenticateOpen), nameof(IsChangePasswordOpen),
        nameof(IsDevicesOpen), nameof(IsSignOutOpen), nameof(IsDeleteAccountOpen),
    ];

    /// <summary>"hoje às HH:mm", "dd/MM às HH:mm" ou "ainda não", no fuso local.</summary>
    private string FormatMoment(DateTimeOffset? at)
    {
        if (at is not { } value)
            return "ainda não";
        var local = TimeZoneInfo.ConvertTime(value, _time.LocalTimeZone);
        var today = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).Date;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return local.Date == today ? $"hoje às {time}" : $"{local.ToString("dd/MM", CultureInfo.InvariantCulture)} às {time}";
    }
}
