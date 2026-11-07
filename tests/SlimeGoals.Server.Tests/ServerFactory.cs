using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace SlimeGoals.Server.Tests;

/// <summary>O servidor em memória, com um banco novo, relógio falso e sem a manutenção diária.</summary>
public sealed class ServerFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;
    private readonly string _environment;

    public ServerFactory(PostgresFixture postgres, IDictionary<string, string?>? overrides = null, string environment = "Development")
    {
        _environment = environment;
        _settings = new Dictionary<string, string?>
        {
            ["Maintenance:Enabled"] = "false",
            ["Limits:AuthPerIpPerMinute"] = "100000",
            ["Limits:AuthPerEmailPerMinute"] = "100000",
            ["Limits:MaxBatchItems"] = "100000",
            ["Limits:MaxBlobBytes"] = "10000000",
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                _settings[key] = value;
            }
        }

        if (!_settings.ContainsKey("ConnectionStrings:Postgres"))
        {
            _settings["ConnectionStrings:Postgres"] = postgres.NewDatabaseAsync().GetAwaiter().GetResult();
        }
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    public string ConnectionString => _settings["ConnectionStrings:Postgres"]!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time)));
    }
}
