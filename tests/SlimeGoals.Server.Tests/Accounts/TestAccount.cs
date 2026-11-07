using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

/// <summary>Um aparelho de teste: cliente já autenticado, token e id.</summary>
public sealed record TestDevice(HttpClient Client, string Token, Guid DeviceId);

/// <summary>Uma conta cadastrada, confirmada e com um aparelho já conectado.</summary>
public sealed class TestAccount
{
    private readonly ServerFactory _factory;

    private TestAccount(ServerFactory factory, string email, byte[] authKey, byte[] recoveryKey)
    {
        _factory = factory;
        Email = email;
        AuthKey = authKey;
        RecoveryKey = recoveryKey;
    }

    public static byte[] WrappedPassword { get; } = [1, 2, 3, 4, 5];

    public string Email { get; }

    public byte[] AuthKey { get; }

    public byte[] RecoveryKey { get; }

    public TestDevice First { get; private set; } = null!;

    public HttpClient Client => First.Client;

    public static RegisterRequest NewRegisterRequest(string email, byte[]? authKey = null) => new(
        email,
        RandomNumberGenerator.GetBytes(16),
        "{\"v\":1,\"m\":65536,\"t\":3,\"p\":1}",
        authKey ?? RandomNumberGenerator.GetBytes(32),
        RandomNumberGenerator.GetBytes(32),
        WrappedPassword,
        [9, 8, 7]);

    public static async Task<TestAccount> CreateAsync(ServerFactory factory)
    {
        var email = $"user{Guid.NewGuid():N}@exemplo.com";
        var request = NewRegisterRequest(email);
        using var anonymous = factory.CreateClient();
        var register = await anonymous.PostAsJsonAsync("/auth/register", request, SyncJsonContext.Default.RegisterRequest);
        register.EnsureSuccessStatusCode();
        var confirm = await anonymous.PostAsJsonAsync(
            "/auth/confirm", new ConfirmRequest(factory.Email.LastToken, request.AuthKey), SyncJsonContext.Default.ConfirmRequest);
        confirm.EnsureSuccessStatusCode();

        var account = new TestAccount(factory, email, request.AuthKey!, request.RecoveryKey!);
        account.First = await account.LoginAsync("primeiro");
        return account;
    }

    public async Task<TestDevice> LoginAsync(string device)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest(Email, AuthKey, device), SyncJsonContext.Default.LoginRequest);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.LoginResponse))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
        return new TestDevice(client, body.Token, body.DeviceId);
    }
}
