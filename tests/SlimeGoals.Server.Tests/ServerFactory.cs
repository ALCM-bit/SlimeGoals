using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Server.Accounts;

namespace SlimeGoals.Server.Tests;

/// <summary>O servidor em memória, com um banco novo, relógio falso e sem a manutenção diária.</summary>
public sealed class ServerFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;
    private readonly string _environment;
    private readonly bool _fakeEmail;

    public ServerFactory(PostgresFixture postgres, IDictionary<string, string?>? overrides = null, string environment = "Development")
        : this(postgres, overrides, environment, fakeEmail: environment == "Development")
    {
    }

    private ServerFactory(PostgresFixture postgres, IDictionary<string, string?>? overrides, string environment, bool fakeEmail)
    {
        _environment = environment;
        _fakeEmail = fakeEmail;
        _settings = new Dictionary<string, string?>
        {
            ["Maintenance:Enabled"] = "false",
            ["Limits:AuthPerIpPerMinute"] = "100000",
            ["Limits:AuthPerEmailPerIpPerMinute"] = "100000",
            ["Limits:LoginFailuresPerEmailPerHour"] = "100000",
            ["Limits:ConfirmationEmailsPerAddressPerHour"] = "100000",
            ["Limits:MaxBatchItems"] = "100000",
            ["Limits:MaxBlobBytes"] = "10000000",
            ["Limits:AuthKeyFailuresPerAccountPerMinute"] = "100000",
            ["Limits:RequestsPerIpPerMinute"] = "100000",
            ["Limits:SyncPerDevicePerMinute"] = "100000",
            ["Email:RetryDelaySeconds:0"] = "0",
            ["Email:RetryDelaySeconds:1"] = "0",
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
            var database = postgres.NewDatabaseAsync().GetAwaiter().GetResult();
            _settings["ConnectionStrings:Postgres"] = database.App;
            _settings["ConnectionStrings:PostgresMigrations"] = database.Owner;
        }
    }

    /// <summary>
    /// Um servidor em Production com configuração válida (segredo do prelogin, papéis distintos e SMTP configurado);
    /// o envio continua sendo o falso. Os testes de recusa partem dele e estragam uma chave só.
    /// </summary>
    public static ServerFactory Production(PostgresFixture postgres, IDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Server:PreloginSecret"] = "segredo-de-teste",
            ["Email:Mode"] = "smtp",
            ["Email:Smtp:Host"] = "smtp.exemplo.com",
            ["Email:Smtp:From"] = "SlimeGoals <nao-responda@exemplo.com>",
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        return new ServerFactory(postgres, settings, "Production", fakeEmail: true);
    }

    /// <summary>Um servidor em Development com o envio escolhido pela configuração (<c>Email:Mode</c>), sem o falso.</summary>
    public static ServerFactory WithConfiguredEmail(PostgresFixture postgres, IDictionary<string, string?> overrides) =>
        new(postgres, overrides, "Development", fakeEmail: false);

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Rota protegida só dos testes: 200 com token válido, 401 sem ele.</summary>
    public const string ProtectedTestRoute = "/test/protected";

    public FakeEmailSender Email { get; } = new();

    /// <summary>Tudo que o servidor registrou; o nível vem da configuração <c>Logging</c>.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>A cadeia do papel do app (só linhas).</summary>
    public string ConnectionString => _settings["ConnectionStrings:Postgres"]!;

    /// <summary>A cadeia do papel dono (DDL); cai na do app quando o teste não configurou outra.</summary>
    public string OwnerConnectionString => _settings.GetValueOrDefault("ConnectionStrings:PostgresMigrations") ?? ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));
            services.AddSingleton<ILoggerProvider>(Logs);
            if (_fakeEmail)
            {
                services.Replace(ServiceDescriptor.Singleton<IEmailSender>(Email));
            }

            services.AddTransient<IStartupFilter, ProtectedRouteFilter>();
        });
    }
}

/// <summary>Espera a fila de e-mails de confirmação do servidor.</summary>
public static class EmailQueueExtensions
{
    /// <summary>
    /// Termina quando a fila do servidor não tem e-mail à espera nem sendo enviado. Depois dele, a contagem de
    /// envios é final: serve para conferir que um e-mail não saiu.
    /// </summary>
    public static Task WaitForEmailQueueAsync(this WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<ConfirmationEmailQueue>().WhenIdleAsync().WaitAsync(FakeEmailSender.DefaultTimeout);
}

/// <summary>Registra a rota protegida de teste antes do pipeline do servidor.</summary>
internal sealed class ProtectedRouteFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, continuation) =>
        {
            if (context.Request.Path != ServerFactory.ProtectedTestRoute)
            {
                await continuation(context);
                return;
            }

            var result = await context.AuthenticateAsync();
            if (!result.Succeeded)
            {
                await context.ChallengeAsync();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
        });
        next(app);
    };
}
