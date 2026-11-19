using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests;

public sealed class SecretsDirectoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Secret_file_overrides_configuration()
    {
        var directory = Directory.CreateTempSubdirectory("slimegoals-secrets-");
        try
        {
            const string fromFile = "segredo-vindo-do-arquivo";
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, "Server__PreloginSecret"), fromFile);
            await using var factory = new ServerFactory(postgres, new Dictionary<string, string?>
            {
                ["SecretsDirectory"] = directory.FullName,
                ["Server:PreloginSecret"] = "segredo-do-ambiente",
            });
            using var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync("/auth/prelogin", new PreloginRequest("x@exemplo.com"), SyncJsonContext.Default.PreloginRequest);

            var salt = (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse))!.KdfSalt;
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(fromFile), Encoding.UTF8.GetBytes("x@exemplo.com"))[..16];
            salt.Should().Equal(expected);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Missing_secrets_directory_is_ignored()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?>
        {
            ["SecretsDirectory"] = Path.Combine(Path.GetTempPath(), "slimegoals-nao-existe-" + Guid.NewGuid().ToString("N")),
        });

        var act = () => factory.CreateClient();

        act.Should().NotThrow();
    }
}
