using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Account;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Sync;
using SlimeGoals.Sync;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Contracts;
using SlimeGoals.Sync.Integrity;
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
        new(_account, new AppSettings(_repo), new AccountDefaults("notebook-da-ana", Local),
            inlineDispatch ? a => a() : _posted.Add, _time);

    [Fact]
    public void The_platform_clipboard_is_kept_for_the_view()
    {
        var clipboard = new NullClipboard();

        new AccountViewModel(_account, new AppSettings(_repo), new AccountDefaults("x"), a => a(), _time, clipboard)
            .PlatformClipboard.Should().BeSameAs(clipboard);
        Create().PlatformClipboard.Should().BeNull();
    }

    [Fact]
    public async Task Untrusted_app_shows_why_and_offers_retry()
    {
        _account.Status = WithState(AccountState.UntrustedApp);

        var vm = await LoadedAsync();

        (vm.IsUntrustedApp, vm.IsSignedOut, vm.IsSignedIn).Should().Be((true, false, false));
        vm.RetryVaultCommand.CanExecute(null).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rooted_device_shows_a_warning(bool rooted)
    {
        var integrity = new FixedIntegrity(rooted ? [IntegrityIssue.Rooted] : []);
        var vm = new AccountViewModel(_account, new AppSettings(_repo), new AccountDefaults("x"), a => a(), _time,
            integrity: integrity);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.IsDeviceRooted.Should().Be(rooted);
    }

    private sealed class FixedIntegrity(HashSet<IntegrityIssue> issues) : IAppIntegrity
    {
        public IntegrityReport Check() => new(issues);
    }

    private sealed class NullClipboard : ITextClipboard
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;

        public Task<string?> TryGetTextAsync() => Task.FromResult<string?>(null);

        public Task ClearAsync() => Task.CompletedTask;
    }

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
        vm.ConfirmText.Should().Be(
            "Enviamos um código para ana@exemplo.com. Se não chegar em alguns minutos, confira o spam.");
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
    public async Task Register_sends_the_invite_typed_and_clears_it_on_success()
    {
        var vm = await LoadedAsync();
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation, vm.InviteCode) = ("ana@exemplo.com", "senha longa", "senha longa", "abc");

        await vm.SubmitRegisterCommand.ExecuteAsync(null);

        _account.LastInviteCode.Should().Be("abc");
        vm.InviteCode.Should().BeEmpty("depois do cadastro a página muda e o convite some");
    }

    [Fact]
    public async Task Register_keeps_the_invite_when_the_server_refuses()
    {
        var vm = await LoadedAsync();
        _account.Failure = new SlimeGoals.Sync.Account.AccountApiException(SlimeGoals.Sync.Account.AccountErrorKind.RegistrationClosed);
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation, vm.InviteCode) = ("ana@exemplo.com", "senha longa", "senha longa", "abc");

        await vm.SubmitRegisterCommand.ExecuteAsync(null);

        vm.Page.Should().Be(AccountPage.Register);
        vm.InviteCode.Should().Be("abc");
        vm.Password.Should().BeEmpty();
        vm.Error.Should().Be(AccountMessages.For(_account.Failure, AccountAction.Register));
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
        (await new AppSettings(_repo).GetSyncServerAsync()).Should().BeNull();
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
        (vm.Password, vm.PasswordConfirmation, vm.RecoveryCodeInput).Should().Be(("", "", ""));

        // As senhas foram limpas: o botão volta assim que a pessoa as digita de novo.
        (vm.Password, vm.PasswordConfirmation, vm.RecoveryCodeInput) =
            ("senha longa", "senha longa", "ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23");
        CommandOf(vm, page).CanExecute(null).Should().BeTrue();
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
        vm.ConfirmationCode = "codigo-do-email";
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
    public async Task Confirm_without_a_code_asks_for_it_and_calls_nothing()
    {
        var vm = await LoadedAsync();
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation) = ("ana@exemplo.com", "uma senha boa", "uma senha boa");
        await vm.SubmitRegisterCommand.ExecuteAsync(null);
        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.Execute(null);
        vm.Page.Should().Be(AccountPage.Confirm);
        vm.ConfirmationCode = "   ";

        await vm.SubmitConfirmCommand.ExecuteAsync(null);

        vm.Error.Should().Be("Informe o código de confirmação.");
        _account.Calls.Should().NotContain("confirm");
        vm.Page.Should().Be(AccountPage.Confirm);
    }

    [Fact]
    public async Task Submit_buttons_wait_for_the_required_fields()
    {
        var vm = await LoadedAsync();

        vm.OpenLoginCommand.Execute(null);
        vm.SubmitLoginCommand.CanExecute(null).Should().BeFalse("faltam e-mail e senha");
        vm.Email = "ana@exemplo.com";
        vm.SubmitLoginCommand.CanExecute(null).Should().BeFalse("falta a senha");
        vm.Password = "uma senha boa";
        vm.SubmitLoginCommand.CanExecute(null).Should().BeTrue();
        vm.DeviceName = " ";
        vm.SubmitLoginCommand.CanExecute(null).Should().BeFalse("falta o nome do aparelho");
        vm.DeviceName = "notebook";

        vm.CloseCommand.Execute(null);
        vm.OpenRegisterCommand.Execute(null);
        vm.SubmitRegisterCommand.CanExecute(null).Should().BeFalse("trocar de página limpa as senhas");
        (vm.Password, vm.PasswordConfirmation) = ("uma senha boa", "uma senha boa");
        vm.SubmitRegisterCommand.CanExecute(null).Should().BeTrue();

        vm.CloseCommand.Execute(null);
        vm.OpenRecoverCommand.Execute(null);
        (vm.Password, vm.PasswordConfirmation) = ("uma senha boa", "uma senha boa");
        vm.SubmitRecoverCommand.CanExecute(null).Should().BeFalse("falta o código de recuperação");
        vm.RecoveryCodeInput = "ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23";
        vm.SubmitRecoverCommand.CanExecute(null).Should().BeTrue();

        vm.CloseCommand.Execute(null);
        vm.OpenChangePasswordCommand.Execute(null);
        (vm.Password, vm.PasswordConfirmation) = ("outra senha boa", "outra senha boa");
        vm.SubmitChangePasswordCommand.CanExecute(null).Should().BeFalse("falta a senha atual");
        vm.CurrentPassword = "uma senha boa";
        vm.SubmitChangePasswordCommand.CanExecute(null).Should().BeTrue();

        vm.CloseCommand.Execute(null);
        vm.OpenDeleteAccountCommand.Execute(null);
        vm.SubmitDeleteAccountCommand.CanExecute(null).Should().BeFalse();
        vm.Password = "uma senha boa";
        vm.SubmitDeleteAccountCommand.CanExecute(null).Should().BeTrue();

        vm.CloseCommand.Execute(null);
        vm.OpenReauthenticateCommand.Execute(null);
        vm.SubmitReauthenticateCommand.CanExecute(null).Should().BeFalse();
        vm.Password = "uma senha boa";
        vm.SubmitReauthenticateCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Screen_limits_come_from_the_account_rules()
    {
        AccountViewModel.MaxEmailLength.Should().Be(AccountRules.MaxEmailLength);
        AccountViewModel.MaxDeviceNameLength.Should().Be(AccountRules.MaxDeviceNameLength);
    }

    [Fact]
    public async Task Register_and_recover_explain_their_code_differently()
    {
        var vm = await LoadedAsync();
        vm.OpenRegisterCommand.Execute(null);
        (vm.Email, vm.Password, vm.PasswordConfirmation) = ("ana@exemplo.com", "uma senha", "uma senha");
        await vm.SubmitRegisterCommand.ExecuteAsync(null);
        vm.RecoveryCodePageText.Should().Be(AccountViewModel.RecoveryCodeText);

        vm.RecoveryCodeSaved = true;
        vm.CloseCommand.Execute(null);
        await RecoverAsync(vm);

        vm.RecoveryCodePageText.Should().Be(AccountViewModel.RecoveredCodeText).And.Contain("anterior deixou de valer");
    }

    [Fact]
    public async Task Recover_that_signed_in_leaves_no_message()
    {
        var vm = await LoadedAsync();
        _account.Status = new AccountStatus(AccountState.SignedIn, "ana@exemplo.com", new Uri("http://127.0.0.1:8080/"), null, 0, null);

        await RecoverAsync(vm);
        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.Execute(null);

        vm.Page.Should().Be(AccountPage.None);
        vm.Error.Should().BeNull();
    }

    [Fact]
    public async Task Recover_that_left_the_device_signed_out_asks_to_sign_in()
    {
        var vm = await LoadedAsync();

        await RecoverAsync(vm);
        vm.Error.Should().BeNull();
        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.Execute(null);

        vm.Page.Should().Be(AccountPage.None);
        vm.Error.Should().Be(AccountViewModel.SignInWithNewPasswordText);
    }

    [Fact]
    public async Task Recover_without_the_server_confirmation_still_shows_the_new_code()
    {
        var vm = await LoadedAsync();
        _account.Failure = new RecoveryUnconfirmedException("NOVO-CODI-GONA-OCON-FIRM-ADO0-12", new IOException("sem rede"));

        await RecoverAsync(vm);

        vm.Page.Should().Be(AccountPage.RecoveredCode);
        vm.ShownRecoveryCode.Should().Be("NOVO-CODI-GONA-OCON-FIRM-ADO0-12");
        vm.RecoveryCodePageText.Should().Be(AccountViewModel.RecoveryUnconfirmedText);
        vm.Error.Should().BeNull();
        vm.CloseCommand.CanExecute(null).Should().BeFalse("o código não volta a aparecer");
        vm.RecoveryCodeSaved = true;
        vm.ContinueCommand.Execute(null);
        vm.Error.Should().Be(AccountViewModel.SignInWithNewPasswordText);
    }

    private static async Task RecoverAsync(AccountViewModel vm)
    {
        vm.OpenRecoverCommand.Execute(null);
        (vm.Email, vm.RecoveryCodeInput, vm.Password, vm.PasswordConfirmation) =
            ("ana@exemplo.com", "ABCD-EFGH-JKMN-PQRS-TVWX-YZ01-23", "nova senha", "nova senha");
        await vm.SubmitRecoverCommand.ExecuteAsync(null);
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
        AccountViewModel.SessionExpiredText.Should().Be(
            "A sessão deste aparelho foi encerrada (a senha mudou, o aparelho foi removido ou ficou 30 dias sem sincronizar). " +
            "Entre de novo para continuar sincronizando. Nada do que está na fila se perde.");
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
    public async Task Server_field_starts_empty_without_a_default_and_without_a_saved_server()
    {
        var vm = new AccountViewModel(_account, new AppSettings(_repo), new AccountDefaults("pc"), a => a(), _time);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.ServerText.Should().BeEmpty();
        AccountViewModel.ServerHint.Should().Be("https://servidor.exemplo.ts.net");
    }

    [Fact]
    public async Task Server_field_starts_with_the_head_default()
    {
        var vm = new AccountViewModel(_account, new AppSettings(_repo),
            new AccountDefaults("pc", new Uri("http://127.0.0.1:8080")), a => a(), _time);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.ServerText.Should().Be("http://127.0.0.1:8080");
    }

    [Fact]
    public async Task Saved_server_wins_over_the_default()
    {
        await new AppSettings(_repo).SetSyncServerAsync(new Uri("https://sync.exemplo.com"));
        var vm = new AccountViewModel(_account, new AppSettings(_repo),
            new AccountDefaults("pc", new Uri("http://127.0.0.1:8080")), a => a(), _time);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.ServerText.Should().Be("https://sync.exemplo.com");
    }

    [Fact]
    public async Task Insecure_session_offers_only_sign_out()
    {
        _account.Status = WithState(AccountState.InsecureSession);

        var vm = await LoadedAsync();

        vm.IsInsecureSession.Should().BeTrue();
        vm.IsSessionExpired.Should().BeFalse();
        vm.SummaryText.Should().Be("Sessão sem https");
        vm.InsecureSessionText.Should().Be("Esta sessão usa um endereço http://, que não é mais aceito. Saia da conta e " +
            "entre de novo com https://. O que está na fila deste aparelho não será enviado.");
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
    public async Task Sync_now_runs_a_cycle_and_says_it_finished()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();

        await vm.SyncNowCommand.ExecuteAsync(null);

        _account.Calls.Should().Contain("sync-cycle");
        vm.SyncMessage.Should().Be("Sincronização concluída.");
        vm.Error.Should().BeNull();
    }

    [Theory]
    [InlineData(SyncCycleResult.Incomplete, "A sincronização não terminou. Tente de novo em instantes.")]
    [InlineData(SyncCycleResult.Skipped, "A sincronização não terminou. Tente de novo em instantes.")]
    [InlineData(SyncCycleResult.Failed, "Não foi possível sincronizar.")]
    [InlineData(SyncCycleResult.Paused, "Não foi possível sincronizar.")]
    [InlineData(SyncCycleResult.Disabled, "Não foi possível sincronizar.")]
    public async Task Sync_now_says_when_it_did_not_finish(SyncCycleResult result, string message)
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        _account.SyncResult = result;

        await vm.SyncNowCommand.ExecuteAsync(null);

        vm.SyncMessage.Should().Be(message);
    }

    [Fact]
    public async Task Sync_now_that_ends_the_session_shows_no_message()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        _account.SyncResult = SyncCycleResult.Unauthorized;
        _account.Status = WithState(AccountState.SessionExpired); // o coordenador já encerrou a sessão ao devolver

        await vm.SyncNowCommand.ExecuteAsync(null);

        vm.IsSessionExpired.Should().BeTrue();
        vm.SyncMessage.Should().BeNull();
    }

    [Fact]
    public async Task Sync_now_shows_progress_and_blocks_the_other_actions()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        _account.Gate = new TaskCompletionSource();

        var sync = vm.SyncNowCommand.ExecuteAsync(null);

        vm.IsBusy.Should().BeTrue();
        vm.SyncMessage.Should().Be("Sincronizando…");
        vm.SyncNowCommand.CanExecute(null).Should().BeFalse();
        vm.OpenSignOutCommand.CanExecute(null).Should().BeFalse();
        _account.RaiseStatusChanged(); // o ciclo do agendador termina enquanto o do botão espera
        vm.SyncMessage.Should().Be("Sincronizando…");

        _account.Gate.SetResult();
        await sync;

        vm.IsBusy.Should().BeFalse();
        vm.SyncMessage.Should().Be("Sincronização concluída.");
        vm.SyncNowCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Sync_message_stays_while_the_status_is_the_same()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        await vm.SyncNowCommand.ExecuteAsync(null);

        _account.RaiseStatusChanged(); // o aviso do coordenador pode chegar depois do recarregar do comando
        vm.SyncMessage.Should().Be("Sincronização concluída.");

        _account.Status = SignedIn(pending: 1);
        _account.RaiseStatusChanged();
        vm.SyncMessage.Should().BeNull("uma mudança depois do ciclo deixa a linha desatualizada");
    }

    [Fact]
    public async Task Sync_message_goes_away_on_reload_and_on_other_pages()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        await vm.SyncNowCommand.ExecuteAsync(null);
        vm.SyncMessage.Should().NotBeNull();

        await vm.LoadCommand.ExecuteAsync(null);
        vm.SyncMessage.Should().BeNull("voltar a Configurações começa sem a linha");

        await vm.SyncNowCommand.ExecuteAsync(null);
        vm.OpenChangePasswordCommand.Execute(null);
        vm.SyncMessage.Should().BeNull();
    }

    [Fact]
    public async Task Reloading_while_syncing_keeps_the_progress_line()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        _account.Gate = new TaskCompletionSource();
        var sync = vm.SyncNowCommand.ExecuteAsync(null);

        await vm.LoadCommand.ExecuteAsync(null); // a pessoa sai de Configurações e volta no meio do ciclo
        vm.SyncMessage.Should().Be("Sincronizando…");

        _account.Gate.SetResult();
        await sync;
        vm.SyncMessage.Should().Be("Sincronização concluída.");
    }

    [Fact]
    public async Task Sync_result_shows_even_when_the_status_cannot_be_read()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        _account.StatusFailure = new InvalidOperationException();

        await vm.SyncNowCommand.ExecuteAsync(null);

        vm.SyncMessage.Should().Be("Sincronização concluída.", "\"Sincronizando…\" com os botões livres diria que o ciclo não acabou");
    }

    [Fact]
    public async Task Sync_now_exception_shows_the_generic_error_and_no_message()
    {
        _account.Status = SignedIn();
        var vm = await LoadedAsync();
        _account.Failure = new HttpRequestException();

        await vm.SyncNowCommand.ExecuteAsync(null);

        vm.Error.Should().Be("Algo deu errado. Tente de novo.");
        vm.SyncMessage.Should().BeNull();
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
    public async Task Account_pages_open_inside_the_sync_group()
    {
        var account = await LoadedAsync();
        var vm = new SettingsPageViewModel(new AppSettings(_repo),
            capabilities: PlatformCapabilities.Desktop with { Sync = true }, account: account);
        vm.OpenSectionCommand.Execute(SettingsSection.Sync);
        (vm.IsSyncOpen, vm.HeaderTitle).Should().Be((true, "Sincronização"));

        account.OpenLoginCommand.Execute(null);
        (vm.IsSyncOpen, vm.IsAccountPageOpen).Should().Be((false, true));

        vm.TryGoBack().Should().BeTrue();
        (vm.IsSyncOpen, vm.IsAccountPageOpen).Should().Be((true, false));
        vm.TryGoBack().Should().BeTrue();
        vm.IsMainOpen.Should().BeTrue();
    }

    [Theory]
    [InlineData(AccountState.SignedOut, "Sem conta")]
    [InlineData(AccountState.SignedIn, "Conectado como ana@exemplo.com")]
    [InlineData(AccountState.SessionExpired, "Entre de novo para continuar")]
    [InlineData(AccountState.VaultLocked, "Chaveiro travado")]
    [InlineData(AccountState.SessionUnreadable, "Sessão ilegível")]
    [InlineData(AccountState.UntrustedApp, "Conferência de segurança falhou")]
    public async Task Summary_for_the_settings_list_follows_the_state(AccountState state, string summary)
    {
        _account.Status = new AccountStatus(state, "ana@exemplo.com", Local, null, 0, null);

        var vm = await LoadedAsync();

        vm.SummaryText.Should().Be(summary);
    }

    [Fact]
    public async Task Navigator_request_opens_the_sync_group()
    {
        var account = await LoadedAsync();
        var navigator = new PageNavigator();
        var vm = new SettingsPageViewModel(new AppSettings(_repo),
            capabilities: PlatformCapabilities.Desktop with { Sync = true }, account: account, navigator: navigator);

        navigator.ShowSettings(SettingsSection.Sync);

        vm.IsSyncOpen.Should().BeTrue();
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

    [Theory]
    [InlineData(AccountPage.Recover)]
    [InlineData(AccountPage.ChangePassword)]
    public async Task Leaving_settings_clears_typed_secrets(AccountPage page)
    {
        var account = await LoadedAsync();
        var settings = new SettingsPageViewModel(new AppSettings(_repo),
            capabilities: PlatformCapabilities.Desktop with { Sync = true }, account: account);
        var today = ShellViewModelTests.Today();
        var shell = new ShellViewModel(new PageViewModel[] { today, settings });
        shell.CurrentPage = settings;
        Open(account, page);
        (account.CurrentPassword, account.Password, account.PasswordConfirmation, account.RecoveryCodeInput) =
            ("senha atual", "senha nova", "senha nova", "CODIGO-DIGITADO");
        account.Email = "ana@exemplo.com";

        shell.CurrentPage = today;

        (account.CurrentPassword, account.Password, account.PasswordConfirmation, account.RecoveryCodeInput)
            .Should().Be(("", "", "", ""));
        account.Email.Should().Be("ana@exemplo.com");
        _account.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AccountPage.Register)]
    [InlineData(AccountPage.Login)]
    [InlineData(AccountPage.Recover)]
    [InlineData(AccountPage.ChangePassword)]
    public async Task Account_page_protects_the_screen_while_open(AccountPage page)
    {
        var account = await LoadedAsync();
        var screen = new RecordingSecureScreen();
        var settings = new SettingsPageViewModel(new AppSettings(_repo),
            capabilities: PlatformCapabilities.Desktop with { Sync = true }, account: account, secureScreen: screen);
        var shell = new ShellViewModel(new PageViewModel[] { ShellViewModelTests.Today(), settings });
        shell.CurrentPage = settings;
        screen.Calls.Should().BeEmpty();

        Open(account, page);
        screen.Calls.Should().Equal(true);

        settings.TryGoBack().Should().BeTrue();
        screen.Calls.Should().Equal(true, false);
    }

    [Fact]
    public async Task Leaving_settings_lifts_the_protection_and_coming_back_restores_it()
    {
        var account = await LoadedAsync();
        var screen = new RecordingSecureScreen();
        var settings = new SettingsPageViewModel(new AppSettings(_repo),
            capabilities: PlatformCapabilities.Desktop with { Sync = true }, account: account, secureScreen: screen);
        var today = ShellViewModelTests.Today();
        var shell = new ShellViewModel(new PageViewModel[] { today, settings });
        shell.CurrentPage = settings;
        account.OpenLoginCommand.Execute(null);

        shell.CurrentPage = today;
        screen.Calls.Should().Equal(true, false);

        // A página de conta continua aberta dentro de Configurações: voltar a ela protege de novo.
        shell.CurrentPage = settings;
        screen.Calls.Should().Equal(true, false, true);
    }

    [Fact]
    public async Task Account_page_open_outside_settings_does_not_protect_the_screen()
    {
        var account = await LoadedAsync();
        var screen = new RecordingSecureScreen();
        _ = new SettingsPageViewModel(new AppSettings(_repo),
            capabilities: PlatformCapabilities.Desktop with { Sync = true }, account: account, secureScreen: screen);

        account.OpenLoginCommand.Execute(null);

        screen.Calls.Should().BeEmpty();
    }

    private sealed class RecordingSecureScreen : ISecureScreen
    {
        public List<bool> Calls { get; } = [];

        public void SetProtected(bool value) => Calls.Add(value);
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
