using SlimeGoals.Core.Abstractions;
using SlimeGoals.Platform.Linux.Secrets;
using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace SlimeGoals.Platform.Linux.Tests;

/// <summary>
/// Contra um <c>gnome-keyring-daemon</c> isolado num barramento privado (nunca o chaveiro nem o barramento reais).
/// O caminho de destravar pelo prompt usa um Secret Service falso, porque o chaveiro de teste não tem diálogo.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SecretServiceVaultTests
{
    private const string Login = "/org/freedesktop/secrets/collection/login";
    private const string Profile = "0123abcd";

    private static readonly byte[] Binary = [0x00, 0xff, 0x7b, 0x22, 0x80, 0x0a, 0x00];

    [Fact]
    public async Task Set_get_and_replace_round_trip()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        var vault = new SecretServiceVault(keyring.ConnectAsync, Profile);

        await vault.SetAsync("sync-session", Binary);
        (await vault.GetAsync("sync-session")).Should().Equal(Binary);

        await vault.SetAsync("sync-session", [1, 2, 3]);
        (await vault.GetAsync("sync-session")).Should().Equal(1, 2, 3);
        (await keyring.SearchItemsAsync(Tags("sync-session"))).Should().ContainSingle("substituir não duplica o item");
    }

    [Fact]
    public async Task Delete_removes_and_tolerates_missing()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        var vault = new SecretServiceVault(keyring.ConnectAsync, Profile);
        await vault.SetAsync("sync-session", Binary);
        await vault.SetAsync("other", [9]);

        await vault.DeleteAsync("sync-session");

        (await vault.GetAsync("sync-session")).Should().BeNull();
        (await keyring.SearchItemsAsync(Tags("sync-session"))).Should().BeEmpty();
        (await vault.GetAsync("other")).Should().Equal(9);
        await vault.DeleteAsync("sync-session");
        await vault.DeleteAsync("never-existed");
    }

    [Fact]
    public async Task Get_of_missing_name_returns_null()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        var vault = new SecretServiceVault(keyring.ConnectAsync, Profile);

        (await vault.IsAvailableAsync()).Should().BeTrue();
        (await vault.GetAsync("sync-session")).Should().BeNull();
    }

    [Fact]
    public async Task Items_are_tagged_with_application_and_name()
    {
        await using var keyring = await IsolatedKeyring.StartAsync();
        var vault = new SecretServiceVault(keyring.ConnectAsync, Profile);

        await vault.SetAsync("sync-session", Binary);

        var item = (await keyring.SearchItemsAsync(Tags("sync-session"))).Should().ContainSingle().Subject;
        item.Should().StartWith(Login + "/", "vai para a coleção default, que é o login");
        var (label, attributes) = await keyring.ReadItemAsync(item);
        label.Should().Be("SlimeGoals: sincronização");
        attributes.Should().Contain("application", "slimegoals").And.Contain("name", "sync-session")
            .And.Contain("profile", Profile);
        (await keyring.SearchItemsAsync(new Dictionary<string, string> { ["application"] = "slimegoals" }))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Profiles_do_not_share_the_secret()
    {
        // Dois apps de teste (bancos diferentes) no mesmo chaveiro: cada um lê só o seu sync-session.
        await using var keyring = await IsolatedKeyring.StartAsync();
        var first = new SecretServiceVault(keyring.ConnectAsync, "aaaa1111");
        var second = new SecretServiceVault(keyring.ConnectAsync, "bbbb2222");

        await first.SetAsync("sync-session", [1]);
        (await second.GetAsync("sync-session")).Should().BeNull("o segredo do outro perfil não aparece");
        await second.SetAsync("sync-session", [2]);

        (await first.GetAsync("sync-session")).Should().Equal(1);
        (await second.GetAsync("sync-session")).Should().Equal(2);
        (await keyring.SearchItemsAsync(Tags("sync-session"))).Should().HaveCount(2, "gravar num perfil não substitui o outro");

        await second.DeleteAsync("sync-session");
        (await first.GetAsync("sync-session")).Should().Equal(new byte[] { 1 }, "apagar num perfil não apaga o outro");
    }

    [Fact]
    public async Task Locked_collection_is_unlocked_through_the_prompt()
    {
        await using var bus = await IsolatedKeyring.StartAsync(keyring: false);
        using var service = await FakeSecretService.StartAsync(bus);
        var vault = new SecretServiceVault(bus.ConnectAsync, Profile);

        (await vault.GetAsync("sync-session")).Should().BeNull();

        service.Locked.Should().BeFalse();
        service.Calls.Should().ContainInOrder("OpenSession", "ReadAlias", "Unlock", "Prompt", "SearchItems", "Close");
    }

    [Fact]
    public async Task Locked_collection_dismissed_at_the_prompt_is_unavailable()
    {
        await using var bus = await IsolatedKeyring.StartAsync(keyring: false);
        using var service = await FakeSecretService.StartAsync(bus, dismissPrompt: true);
        var vault = new SecretServiceVault(bus.ConnectAsync, Profile);

        await FluentActions.Awaiting(() => vault.GetAsync("sync-session"))
            .Should().ThrowAsync<SecretVaultUnavailableException>();

        service.Locked.Should().BeTrue();
        service.Calls.Should().ContainInOrder("Unlock", "Prompt").And.NotContain("SearchItems");
    }

    [Fact]
    public async Task Locked_keyring_without_a_prompter_is_unavailable_and_nothing_is_written()
    {
        // O gnome-keyring de teste não acha o org.gnome.keyring.SystemPrompter e recusa o prompt.
        await using var keyring = await IsolatedKeyring.StartAsync();
        var vault = new SecretServiceVault(keyring.ConnectAsync, Profile);
        await keyring.LockAsync(Login);
        using var recorder = await keyring.RecordCallsAsync();

        await FluentActions.Awaiting(() => vault.SetAsync("sync-session", Binary))
            .Should().ThrowAsync<SecretVaultUnavailableException>();
        await FluentActions.Awaiting(() => vault.GetAsync("sync-session"))
            .Should().ThrowAsync<SecretVaultUnavailableException>();

        (await recorder.CallsAsync()).Should().ContainInOrder("Unlock", "Prompt").And.NotContain("CreateItem");
        (await keyring.SearchItemsAsync(Tags("sync-session"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_default_collection_reads_are_empty_and_refused_creation_is_unavailable()
    {
        await using var keyring = await IsolatedKeyring.StartAsync(loginPassword: null);
        var vault = new SecretServiceVault(keyring.ConnectAsync, Profile);
        using var recorder = await keyring.RecordCallsAsync();

        (await vault.IsAvailableAsync()).Should().BeTrue();
        (await vault.GetAsync("sync-session")).Should().BeNull();
        await vault.DeleteAsync("sync-session");
        (await recorder.CallsAsync()).Should().NotContain("CreateCollection", "ler e apagar não criam coleção");

        await FluentActions.Awaiting(() => vault.SetAsync("sync-session", Binary))
            .Should().ThrowAsync<SecretVaultUnavailableException>();
        (await recorder.CallsAsync()).Should().ContainInOrder("CreateCollection", "Prompt").And.NotContain("CreateItem");
    }

    [Fact]
    public async Task Missing_service_is_unavailable()
    {
        await using var bus = await IsolatedKeyring.StartAsync(keyring: false);
        var vault = new SecretServiceVault(bus.ConnectAsync, Profile);

        (await vault.IsAvailableAsync()).Should().BeFalse();
        await FluentActions.Awaiting(() => vault.SetAsync("sync-session", Binary))
            .Should().ThrowAsync<SecretVaultUnavailableException>();
        await FluentActions.Awaiting(() => vault.GetAsync("sync-session"))
            .Should().ThrowAsync<SecretVaultUnavailableException>();
        await FluentActions.Awaiting(() => vault.DeleteAsync("sync-session"))
            .Should().ThrowAsync<SecretVaultUnavailableException>();
    }

    [Fact]
    public async Task Unreachable_bus_is_unavailable()
    {
        var socket = Path.Combine(Path.GetTempPath(), $"sgk-none-{Guid.NewGuid():N}");
        var vault = new SecretServiceVault(async () =>
        {
            var connection = new DBusConnection($"unix:path={socket}");
            try
            {
                await connection.ConnectAsync();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }, Profile);

        (await vault.IsAvailableAsync()).Should().BeFalse();
        await FluentActions.Awaiting(() => vault.SetAsync("sync-session", Binary))
            .Should().ThrowAsync<SecretVaultUnavailableException>();
    }

    private static Dictionary<string, string> Tags(string name) =>
        new() { ["application"] = "slimegoals", ["name"] = name };
}
