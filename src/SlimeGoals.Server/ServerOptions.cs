using System.Net;
using Microsoft.Extensions.Options;

namespace SlimeGoals.Server;

/// <summary>Configuração do servidor; os valores padrão estão em appsettings.json.</summary>
public sealed class ServerOptions
{
    public ServerSection Server { get; set; } = new();

    public SyncSection Sync { get; set; } = new();

    public LimitsSection Limits { get; set; } = new();

    public RetentionSection Retention { get; set; } = new();

    public MaintenanceSection Maintenance { get; set; } = new();

    public sealed class ServerSection
    {
        /// <summary>Segredo do sal falso do prelogin; obrigatório fora de Development.</summary>
        public string PreloginSecret { get; set; } = "";

        public string DefaultKdfParams { get; set; } = "{\"v\":1,\"m\":65536,\"t\":3,\"p\":1}";

        public bool MigrateOnStart { get; set; } = true;

        /// <summary>Redes (CIDR) do proxy reverso de confiança; só dele se aceita <c>X-Forwarded-For</c>. Vazio: nenhum proxy.</summary>
        public string[] TrustedProxyNetworks { get; set; } = [];
    }

    public sealed class SyncSection
    {
        public int DefaultLimit { get; set; } = 200;

        public int MaxLimit { get; set; } = 500;

        /// <summary>Cota de registros por conta; nula desliga.</summary>
        public int? MaxRecordsPerAccount { get; set; }
    }

    public sealed class LimitsSection
    {
        public int MaxBatchItems { get; set; } = 100;

        public int MaxBlobBytes { get; set; } = 64 * 1024;

        public int MaxBodyBytes { get; set; } = 10 * 1024 * 1024;

        public int AuthPerIpPerMinute { get; set; } = 20;

        /// <summary>Pedidos por par e-mail e IP e por minuto nas rotas de <c>/auth</c> que citam um e-mail.</summary>
        public int AuthPerEmailPerIpPerMinute { get; set; } = 10;

        /// <summary>Falhas de <c>login</c> e <c>recover</c> por e-mail (de qualquer IP) e por hora; e-mail desconhecido também gasta.</summary>
        public int LoginFailuresPerEmailPerHour { get; set; } = 50;

        /// <summary>E-mails de confirmação por endereço e por hora; passou do limite, o cadastro responde 202 sem mandar nada.</summary>
        public int ConfirmationEmailsPerAddressPerHour { get; set; } = 3;

        /// <summary>Pedidos por IP e por minuto em todas as rotas, menos <c>/health</c>.</summary>
        public int RequestsPerIpPerMinute { get; set; } = 600;

        /// <summary>Pedidos por aparelho e por minuto em <c>/sync</c>.</summary>
        public int SyncPerDevicePerMinute { get; set; } = 300;

        /// <summary>Falhas de <c>auth_key</c> por conta e por minuto em <c>DELETE /account</c> e <c>POST /auth/password</c>.</summary>
        public int AuthKeyFailuresPerAccountPerMinute { get; set; } = 10;
    }

    public sealed class RetentionSection
    {
        public int TombstoneDays { get; set; } = 180;

        public int UnconfirmedAccountHours { get; set; } = 24;
    }

    public sealed class MaintenanceSection
    {
        public bool Enabled { get; set; } = true;
    }
}

/// <summary>Recusa a partida fora de Development sem o segredo do prelogin.</summary>
public sealed class ServerOptionsValidator(IHostEnvironment environment) : IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        if (!environment.IsDevelopment() && string.IsNullOrWhiteSpace(options.Server.PreloginSecret))
        {
            return ValidateOptionsResult.Fail("Server:PreloginSecret é obrigatório fora de Development.");
        }

        if (options.Server.TrustedProxyNetworks.Any(n => !IPNetwork.TryParse(n, out _)))
        {
            return ValidateOptionsResult.Fail("Server:TrustedProxyNetworks tem uma rede em CIDR inválida.");
        }

        return ValidateOptionsResult.Success;
    }
}
