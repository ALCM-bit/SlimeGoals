using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Account;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Services;
using SlimeGoals.Sync;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Contracts;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.Account;

public class AccountViewModelTests
{
    private static readonly Uri Local = new("http://127.0.0.1:8080/");

    private readonly FakeSyncAccount _account = new();
    private readonly InMemorySettingsRepository _repo = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero));
    private readonly List<Action> _posted = [];

    private AccountViewModel Create(bool inlineDispatch = true) =>
        new(_account, new AppSettings(_repo), new AccountDefaults("notebook-da-ana"),
            inlineDispatch ? a => a() : _posted.Add, _time);

    private async Task<AccountViewModel> LoadedAsync()
    {
        var vm = Create();
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    private static AccountStatus SignedIn(int pending = 0, string? error = null, DateTimeOffset? lastSync = null) =>
        new(AccountState.SignedIn, "ana@exemplo.com", new Uri("https://sync.exemplo.com/"), lastSync, pending, error);

    private static AccountStatus WithState(AccountState state, int pending = 0) =>
        new(state, state is AccountState.SessionExpired ? "ana@exemplo.com" : null, null, null, pending, null);

    [Fact]
    public async Task Signed_out_loads_server_from_settings_and_device_name_from_defaults()
    {
        var vm = await LoadedAsync();

        vm.Page.Should().Be(AccountPage.None);
        vm.IsSignedOut.Should().BeTrue();
        vm.ServerText.Should().Be("http://127.0.0.1:8080");
        vm.IsServerEditable.Should().BeTrue();
        vm.DeviceName.Should().Be("notebook-da-ana");
        AccountViewModel.IntroText.Should().StartWith("Com uma conta, suas metas e registros vão cifrados");
    }

    [Fact]
    public async Task Register_flow_shows_code_then_requires_checkbox_then_confirm()
    {
        var vm = await LoadedAsync();
        vm.OpenRegisterCommand.Execute(null);
        vm.Page.Should().Be(AccountPage.Register);
        (vm.Email, vm.Password, vm.PasswordConfirmation) = ("ana@exemplo.com", "senha longa", "senha longa");

        await vm.SubmitRegisterCommand.ExecuteAsync(null);

        _account.Calls.Should().Equal("register");
        (_account.LastServer, _account.LastEmail, _account.LastPassword).Should().Be((Local, "ana@exemplo.com", "senha longa"));
        vm.Page.Should().Be(AccountPage.RecoveryCode);
        vm.ShownRecoveryCode.Should().Be(_account.Pending.RecoveryCode);
        vm.ContinueCommand.CanExecute(null).Should().BeFalse();

        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.CanExecute(null).Should().BeTrue();
        vm.ContinueCommand.Execute(null);

        vm.Page.Should().Be(AccountPage.Confirm);
        vm.ShownRecoveryCode.Should().BeNull();
        vm.ConfirmText.Should().Be("Enviamos um código para ana@exemplo.com. Neste servidor de desenvolvimento, o código " +
            "aparece no log do servidor (docker compose logs server).");
        vm.ConfirmationCode = "123456";

        await vm.SubmitConfirmCommand.ExecuteAsync(null);

        _account.Calls.Should().Equal("register", "confirm");
        _account.LastPending.Should().BeSameAs(_account.Pending);
        (_account.LastCode, _account.LastDeviceName).Should().Be(("123456", "notebook-da-ana"));
        vm.Page.Should().Be(AccountPage.None);
        vm.Error.Should().BeNull();
    }

    [Fact]
    public async Task Register_saves_the_server_typed()
    {
        var vm = await LoadedAsync();
        vm.OpenRegisterCommand.Execute(null);
        (vm.ServerText, vm.Email, vm.Password, vm.PasswordConfirmation) =
            ("https://sync.exemplo.com", "ana@exemplo.com", "senha longa", "senha longa");

        await vm.SubmitRegisterCommand.ExecuteAsync(null);

        _account.LastServer.Should().Be(new Uri("https://sync.exemplo.com/"));
        (await new AppSettings(_repo).GetSyncServerAsync()).Should().Be(new Uri("https://sync.exemplo.com/"));
    }

    [Fact]
    public async Task Leaving_confirm_disposes_the_pending_registration()
    {
        var vm = await LoadedAsync();
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation) = ("ana@exemplo.com", "senha longa", "senha longa");
        await vm.SubmitRegisterCommand.ExecuteAsync(null);
        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.Execute(null);

        vm.TryGoBack().Should().BeTrue();

        vm.Page.Should().Be(AccountPage.None);
        var keys = () => _account.Pending.AuthKey;
        keys.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(AccountPage.Register)]
    [InlineData(AccountPage.Recover)]
    [InlineData(AccountPage.ChangePassword)]
    public async Task Mismatched_passwords_are_caught_before_calling_the_service(AccountPage page)
    {
        var vm = await LoadedAsync();
        if (page == AccountPage.ChangePassword)
            _account.Status = SignedIn();
        await vm.LoadCommand.ExecuteAsync(null);
        Open(vm, page);
        (vm.Email, vm.CurrentPassword, vm.Password, vm.PasswordConfirmation, vm.RecoveryCodeInput) =
            ("ana@exemplo.com", "atual", "senha longa", "senha longs", "ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23");

        await Submit(vm, page);

        _account.Calls.Should().BeEmpty();
        vm.Error.Should().Be("As senhas não são iguais.");
        vm.Page.Should().Be(page);
        (vm.CurrentPassword, vm.Password, vm.PasswordConfirmation, vm.RecoveryCodeInput).Should().Be(("", "", "", ""));
    }

    [Fact]
    public async Task Server_field_is_read_only_while_signed_in()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();

        vm.IsServerEditable.Should().BeFalse();
        vm.ServerText.Should().Be("https://sync.exemplo.com");
        vm.ConnectedText.Should().Be("Conectado como ana@exemplo.com");
        vm.ServerLineText.Should().Be("Servidor: https://sync.exemplo.com");

        _account.Status = WithState(AccountState.SignedOut);
        _account.RaiseStatusChanged();

        vm.IsServerEditable.Should().BeTrue();
        vm.ServerText.Should().Be("http://127.0.0.1:8080");
    }

    [Theory]
    [InlineData(AccountPage.Login)]
    [InlineData(AccountPage.Register)]
    [InlineData(AccountPage.Recover)]
    public async Task Invalid_server_blocks_submit_with_error(AccountPage page)
    {
        var vm = await LoadedAsync();
        Open(vm, page);
        (vm.ServerText, vm.Email, vm.Password, vm.PasswordConfirmation, vm.RecoveryCodeInput) =
            ("ftp://x", "ana@exemplo.com", "senha longa", "senha longa", "ABCD");

        await Submit(vm, page);

        _account.Calls.Should().BeEmpty();
        vm.Error.Should().Be("Informe um endereço http:// ou https:// completo.");
        vm.Page.Should().Be(page);
        (await new AppSettings(_repo).GetSyncServerAsync()).Should().Be(Local);
    }

    [Theory]
    [InlineData(AccountPage.Login)]
    [InlineData(AccountPage.Register)]
    [InlineData(AccountPage.Recover)]
    public async Task Failure_shows_text_and_reenables_buttons(AccountPage page)
    {
        var vm = await LoadedAsync();
        Open(vm, page);
        (vm.Email, vm.Password, vm.PasswordConfirmation, vm.RecoveryCodeInput) =
            ("ana@exemplo.com", "senha longa", "senha longa", "ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23");
        _account.Failure = new AccountApiException(AccountErrorKind.Unavailable);

        await Submit(vm, page);

        vm.Error.Should().Be("Não foi possível falar com o servidor. Confira o endereço e a conexão.");
        vm.IsBusy.Should().BeFalse();
        vm.Page.Should().Be(page);
        CommandOf(vm, page).CanExecute(null).Should().BeTrue();
        (vm.Password, vm.PasswordConfirmation, vm.RecoveryCodeInput).Should().Be(("", "", ""));
    }

    [Fact]
    public async Task Confirm_failure_keeps_the_pending_for_another_try()
    {
        var vm = await LoadedAsync();
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation) = ("ana@exemplo.com", "senha longa", "senha longa");
        await vm.SubmitRegisterCommand.ExecuteAsync(null);
        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.Execute(null);
        _account.Failure = new AccountApiException(AccountErrorKind.Unauthorized);

        await vm.SubmitConfirmCommand.ExecuteAsync(null);

        vm.Error.Should().Be("Código inválido ou vencido.");
        vm.Page.Should().Be(AccountPage.Confirm);
        _account.Failure = null;
        await vm.SubmitConfirmCommand.ExecuteAsync(null);
        _account.LastPending.Should().BeSameAs(_account.Pending);
        vm.Page.Should().Be(AccountPage.None);
    }

    [Fact]
    public async Task Raw_exception_messages_never_reach_the_screen()
    {
        var vm = await LoadedAsync();
        vm.OpenLoginCommand.Execute(null);
        _account.Failure = new IOException("/home/ana/.local/share/token=abc");

        await vm.SubmitLoginCommand.ExecuteAsync(null);

        vm.Error.Should().Be("Algo deu errado. Tente de novo.");
    }

    [Fact]
    public async Task Back_is_ignored_while_busy()
    {
        var vm = await LoadedAsync();
        vm.OpenLoginCommand.Execute(null);
        (vm.Email, vm.Password) = ("ana@exemplo.com", "senha longa");
        _account.Gate = new TaskCompletionSource();

        var submit = vm.SubmitLoginCommand.ExecuteAsync(null);

        vm.IsBusy.Should().BeTrue();
        vm.TryGoBack().Should().BeTrue();
        vm.Page.Should().Be(AccountPage.Login);
        vm.CloseCommand.CanExecute(null).Should().BeFalse();
        vm.SubmitLoginCommand.CanExecute(null).Should().BeFalse();
        _account.Gate.SetResult();
        await submit;
        vm.IsBusy.Should().BeFalse();
        vm.Page.Should().Be(AccountPage.None);
        vm.TryGoBack().Should().BeFalse();
    }

    [Fact]
    public async Task Login_sends_fields_and_returns_to_main()
    {
        var vm = await LoadedAsync();
        vm.OpenLoginCommand.Execute(null);
        (vm.Email, vm.Password, vm.DeviceName) = ("ana@exemplo.com", "senha longa", "trabalho");

        await vm.SubmitLoginCommand.ExecuteAsync(null);

        _account.Calls.Should().Equal("login");
        (_account.LastServer, _account.LastEmail, _account.LastPassword, _account.LastDeviceName)
            .Should().Be((Local, "ana@exemplo.com", "senha longa", "trabalho"));
        vm.Page.Should().Be(AccountPage.None);
        vm.Password.Should().BeEmpty();
    }

    [Fact]
    public async Task Recover_shows_the_new_code_until_saved()
    {
        var vm = await LoadedAsync();
        vm.OpenRecoverCommand.Execute(null);
        (vm.Email, vm.RecoveryCodeInput, vm.Password, vm.PasswordConfirmation) =
            ("ana@exemplo.com", "ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23", "nova senha", "nova senha");

        await vm.SubmitRecoverCommand.ExecuteAsync(null);

        (_account.LastCode, _account.LastNewPassword, _account.LastDeviceName)
            .Should().Be(("ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23", "nova senha", "notebook-da-ana"));
        vm.Page.Should().Be(AccountPage.RecoveredCode);
        vm.ShownRecoveryCode.Should().Be(_account.RecoveredCode);
        vm.RecoveryCodeInput.Should().BeEmpty();
        vm.ContinueCommand.CanExecute(null).Should().BeFalse();
        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.Execute(null);
        vm.Page.Should().Be(AccountPage.None);
        vm.ShownRecoveryCode.Should().BeNull();
    }

    [Fact]
    public async Task Recovery_code_page_cannot_be_left_before_the_box_is_checked()
    {
        var vm = await LoadedAsync();
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation) = ("ana@exemplo.com", "senha longa", "senha longa");
        await vm.SubmitRegisterCommand.ExecuteAsync(null);

        vm.CloseCommand.CanExecute(null).Should().BeFalse();
        vm.TryGoBack().Should().BeTrue();
        vm.Page.Should().Be(AccountPage.RecoveryCode);
        vm.ShownRecoveryCode.Should().Be(_account.Pending.RecoveryCode);

        vm.RecoveryCodeSaved = true;
        vm.CloseCommand.CanExecute(null).Should().BeTrue();
        vm.TryGoBack().Should().BeTrue();
        vm.Page.Should().Be(AccountPage.None);
    }

    [Fact]
    public async Task Recovered_code_page_cannot_be_left_before_the_box_is_checked()
    {
        var vm = await LoadedAsync();
        vm.OpenRecoverCommand.Execute(null);
        (vm.Email, vm.RecoveryCodeInput, vm.Password, vm.PasswordConfirmation) =
            ("ana@exemplo.com", "ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23", "nova senha", "nova senha");
        await vm.SubmitRecoverCommand.ExecuteAsync(null);

        vm.CloseCommand.CanExecute(null).Should().BeFalse();
        vm.TryGoBack().Should().BeTrue();
        vm.Page.Should().Be(AccountPage.RecoveredCode);

        vm.RecoveryCodeSaved = true;
        vm.CloseCommand.CanExecute(null).Should().BeTrue();
        vm.CloseCommand.Execute(null);
        vm.Page.Should().Be(AccountPage.None);
    }

    [Fact]
    public async Task Change_password_and_delete_account_send_the_passwords()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        vm.OpenChangePasswordCommand.Execute(null);
        (vm.CurrentPassword, vm.Password, vm.PasswordConfirmation) = ("atual", "nova senha", "nova senha");
        await vm.SubmitChangePasswordCommand.ExecuteAsync(null);
        (_account.LastPassword, _account.LastNewPassword).Should().Be(("atual", "nova senha"));
        vm.Page.Should().Be(AccountPage.None);
        (vm.CurrentPassword, vm.Password, vm.PasswordConfirmation).Should().Be(("", "", ""));

        vm.OpenDeleteAccountCommand.Execute(null);
        vm.Page.Should().Be(AccountPage.DeleteAccount);
        AccountViewModel.DeleteAccountText.Should().StartWith("Isto apaga a conta e todos os dados dela no servidor");
        vm.Password = "atual";
        await vm.SubmitDeleteAccountCommand.ExecuteAsync(null);
        _account.Calls.Should().Equal("change-password", "delete-account");
        _account.LastPassword.Should().Be("atual");
        vm.Page.Should().Be(AccountPage.None);
    }

    [Fact]
    public async Task Sign_out_with_pending_shows_warning_and_wait_keeps_session()
    {
        _account.Status = SignedIn(pending: 3);
        _account.FlushResult = 3;
        var vm = await LoadedAsync();

        await vm.OpenSignOutCommand.ExecuteAsync(null);

        _account.Calls.Should().Equal("flush");
        vm.Page.Should().Be(AccountPage.SignOut);
        vm.SignOutWarningText.Should().Be("Há 3 alterações que ainda não foram enviadas. Se sair agora, elas não chegam aos " +
            "outros aparelhos, e metas excluídas depois disso podem voltar quando você entrar de novo.");

        vm.CloseCommand.Execute(null);
        vm.Page.Should().Be(AccountPage.None);
        _account.Calls.Should().Equal("flush");

        await vm.OpenSignOutCommand.ExecuteAsync(null);
        await vm.SubmitSignOutCommand.ExecuteAsync(null);
        _account.Calls.Should().Equal("flush", "flush", "sign-out");
        vm.Page.Should().Be(AccountPage.None);
    }

    [Fact]
    public async Task Sign_out_warning_with_one_change_is_singular()
    {
        _account.Status = SignedIn(pending: 1);
        _account.FlushResult = 1;
        var vm = await LoadedAsync();

        await vm.OpenSignOutCommand.ExecuteAsync(null);

        vm.SignOutWarningText.Should().StartWith("Há 1 alteração que ainda não foi enviada. Se sair agora, ela não chega");
    }

    [Fact]
    public async Task Sign_out_without_pending_leaves_directly()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();

        await vm.OpenSignOutCommand.ExecuteAsync(null);

        _account.Calls.Should().Equal("flush", "sign-out");
        vm.Page.Should().Be(AccountPage.None);
    }

    [Fact]
    public async Task Devices_list_hides_revoke_for_current_device()
    {
        _account.Status = SignedIn();
        var current = Guid.NewGuid();
        var other = Guid.NewGuid();
        _account.Devices =
        [
            new DeviceDto(current, "notebook-da-ana", DateTimeOffset.UnixEpoch, true),
            new DeviceDto(other, "celular", DateTimeOffset.UnixEpoch, false),
        ];
        var vm = await LoadedAsync();

        await vm.OpenDevicesCommand.ExecuteAsync(null);

        vm.Page.Should().Be(AccountPage.Devices);
        vm.Devices.Select(d => (d.Name, d.CanRevoke, d.CurrentLabel))
            .Should().Equal(("notebook-da-ana", false, "Este aparelho"), ("celular", true, null));

        await vm.RevokeDeviceCommand.ExecuteAsync(vm.Devices[1]);

        _account.LastRevoked.Should().Be(other);
        _account.Calls.Should().Equal("list-devices", "revoke-device", "list-devices");
    }

    [Fact]
    public async Task Session_expired_offers_reauth_and_sign_out()
    {
        _account.Status = WithState(AccountState.SessionExpired, pending: 2);
        _account.FlushResult = 2;
        var vm = await LoadedAsync();

        vm.IsSessionExpired.Should().BeTrue();
        AccountViewModel.SessionExpiredText.Should().EndWith("Nada do que está na fila se perde.");
        vm.OpenReauthenticateCommand.Execute(null);
        vm.Page.Should().Be(AccountPage.Reauthenticate);
        vm.DeviceName.Should().Be("notebook-da-ana");
        vm.Password = "senha longa";
        await vm.SubmitReauthenticateCommand.ExecuteAsync(null);
        (_account.LastPassword, _account.LastDeviceName).Should().Be(("senha longa", "notebook-da-ana"));
        vm.Page.Should().Be(AccountPage.None);

        await vm.OpenSignOutCommand.ExecuteAsync(null);
        vm.Page.Should().Be(AccountPage.SignOut);
        await vm.SubmitSignOutCommand.ExecuteAsync(null);
        _account.Calls.Should().Equal("reauthenticate", "flush", "sign-out");
    }

    [Fact]
    public async Task Vault_locked_retries_and_unreadable_only_signs_out()
    {
        _account.Status = WithState(AccountState.VaultLocked);
        var vm = await LoadedAsync();
        vm.IsVaultLocked.Should().BeTrue();
        await vm.RetryVaultCommand.ExecuteAsync(null);
        _account.Calls.Should().Equal("retry-vault");

        _account.Status = WithState(AccountState.SessionUnreadable);
        _account.RaiseStatusChanged();
        vm.IsSessionUnreadable.Should().BeTrue();
        AccountViewModel.SessionUnreadableText.Should().Be(
            "Os dados da sessão neste aparelho estão ilegíveis. Saia da conta e entre de novo.");
    }

    [Fact]
    public async Task Signed_in_texts_follow_the_status()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT"));
        _account.Status = SignedIn(pending: 4, error: "Sem conexão com o servidor.",
            lastSync: new DateTimeOffset(2026, 11, 7, 13, 5, 0, TimeSpan.Zero));
        var vm = await LoadedAsync();

        vm.LastSyncText.Should().Be("Última sincronização: hoje às 10:05");
        vm.PendingText.Should().Be("Pendências: 4");
        vm.LastError.Should().Be("Sem conexão com o servidor.");

        _account.Status = SignedIn(lastSync: new DateTimeOffset(2026, 11, 6, 2, 30, 0, TimeSpan.Zero));
        _account.RaiseStatusChanged();
        vm.LastSyncText.Should().Be("Última sincronização: 05/11 às 23:30");
        vm.LastError.Should().BeNull();

        _account.Status = SignedIn();
        _account.RaiseStatusChanged();
        vm.LastSyncText.Should().Be("Última sincronização: ainda não");
    }

    [Fact]
    public async Task Status_changes_reload_on_the_ui_dispatcher()
    {
        var vm = Create(inlineDispatch: false);
        await vm.LoadCommand.ExecuteAsync(null);
        _account.Status = SignedIn();

        _account.RaiseStatusChanged();

        vm.IsSignedIn.Should().BeFalse();
        _posted.Should().ContainSingle();
        _posted[0]();
        vm.IsSignedIn.Should().BeTrue();
    }

    [Fact]
    public void Settings_hides_account_without_capability()
    {
        var settings = new AppSettings(_repo);
        var account = Create();

        new SettingsPageViewModel(settings, account: account).ShowAccount.Should().BeFalse();
        new SettingsPageViewModel(settings, capabilities: PlatformCapabilities.Desktop with { Sync = true })
            .ShowAccount.Should().BeFalse();
        new SettingsPageViewModel(settings, capabilities: PlatformCapabilities.Desktop with { Sync = true }, account: account)
            .ShowAccount.Should().BeTrue();
    }

    [Fact]
    public async Task Settings_back_closes_account_page_first()
    {
        var account = await LoadedAsync();
        var vm = new SettingsPageViewModel(new AppSettings(_repo),
            capabilities: PlatformCapabilities.Desktop with { Sync = true }, account: account);
        (vm.IsMainOpen, vm.IsAccountPageOpen, account.PageTitle).Should().Be((true, false, (string?)null));

        account.OpenLoginCommand.Execute(null);
        (vm.IsMainOpen, vm.IsAccountPageOpen, account.PageTitle).Should().Be((false, true, "Entrar"));

        vm.TryGoBack().Should().BeTrue();
        account.Page.Should().Be(AccountPage.None);
        (vm.IsMainOpen, vm.IsAccountPageOpen).Should().Be((true, false));
        vm.TryGoBack().Should().BeFalse();
    }

    private static void Open(AccountViewModel vm, AccountPage page)
    {
        switch (page)
        {
            case AccountPage.Register: vm.OpenRegisterCommand.Execute(null); break;
            case AccountPage.Login: vm.OpenLoginCommand.Execute(null); break;
            case AccountPage.Recover: vm.OpenRecoverCommand.Execute(null); break;
            case AccountPage.ChangePassword: vm.OpenChangePasswordCommand.Execute(null); break;
            default: throw new ArgumentOutOfRangeException(nameof(page));
        }
    }

    private static CommunityToolkit.Mvvm.Input.IAsyncRelayCommand CommandOf(AccountViewModel vm, AccountPage page) => page switch
    {
        AccountPage.Register => vm.SubmitRegisterCommand,
        AccountPage.Login => vm.SubmitLoginCommand,
        AccountPage.Recover => vm.SubmitRecoverCommand,
        AccountPage.ChangePassword => vm.SubmitChangePasswordCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(page)),
    };

    private static Task Submit(AccountViewModel vm, AccountPage page) => CommandOf(vm, page).ExecuteAsync(null);
}
