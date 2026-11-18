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

    public EmailSection Email { get; set; } = new();

    public sealed class ServerSection
    {
        /// <summary>Segredo do sal falso do prelogin; obrigatório fora de Development.</summary>
        public string PreloginSecret { get; set; } = "";

        public string DefaultKdfParams { get; set; } = "{\"v\":1,\"m\":65536,\"t\":3,\"p\":1}";

        public bool MigrateOnStart { get; set; } = true;

        /// <summary>Redes (CIDR) do proxy reverso de confiança; só dele se aceita <c>X-Forwarded-For</c>. Vazio: nenhum proxy.</summary>
        public string[] TrustedProxyNetworks { get; set; } = [];

        /// <summary>Convite do cadastro. Em Development, vazio deixa o cadastro aberto; fora dele, vazio o fecha.</summary>
        public string InviteCode { get; set; } = "";
    }

    public sealed class SyncSection
    {
        public int DefaultLimit { get; set; } = 200;

        public int MaxLimit { get; set; } = 500;

        /// <summary>Cota de registros por conta. Um valor vazio desliga; um JSON nulo mantém o padrão.</summary>
        public int? MaxRecordsPerAccount { get; set; } = 100000;
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

        /// <summary>Dias sem uso (por <c>last_seen_at</c>) até o token do aparelho vencer.</summary>
        public int DeviceIdleDays { get; set; } = 30;
    }

    public sealed class MaintenanceSection
    {
        public bool Enabled { get; set; } = true;
    }

    /// <summary>Como sai o e-mail de confirmação: no log (só Development) ou por SMTP.</summary>
    public enum EmailMode
    {
        Log,
        Smtp,
    }

    /// <summary>Proteção da conexão SMTP. Nunca <c>Auto</c>, que cairia sem aviso para texto puro.</summary>
    public enum SmtpSecurity
    {
        /// <summary>Conecta em texto puro e exige o STARTTLS (porta 587).</summary>
        StartTls,

        /// <summary>TLS desde o primeiro byte (porta 465).</summary>
        SslOnConnect,

        /// <summary>Sem TLS; só Development, contra um servidor de teste.</summary>
        None,
    }

    public sealed class EmailSection
    {
        public EmailMode Mode { get; set; } = EmailMode.Log;

        /// <summary>
        /// Espera, em segundos, antes da 2ª e da 3ª tentativa de envio; o padrão <c>[2, 10]</c> fica no appsettings.json
        /// (lista na configuração soma à do código em vez de trocar). Sem valor para a tentativa, usa o último; lista vazia, nenhuma espera.
        /// </summary>
        public int[] RetryDelaySeconds { get; set; } = [];

        public SmtpSection Smtp { get; set; } = new();
    }

    public sealed class SmtpSection
    {
        public string Host { get; set; } = "";

        public int Port { get; set; } = 587;

        /// <summary>Vazio: envia sem autenticar.</summary>
        public string Username { get; set; } = "";

        /// <summary>Segredo: vem de <c>/run/secrets</c> e nunca vai para log ou mensagem de erro.</summary>
        public string Password { get; set; } = "";

        /// <summary>Remetente, com ou sem nome (<c>SlimeGoals &lt;nao-responda@exemplo.com&gt;</c>).</summary>
        public string From { get; set; } = "";

        public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
    }
}

/// <summary>
/// Recusa a partida fora de Development sem o segredo do prelogin e, em qualquer ambiente, com proxy em CIDR
/// inválido ou <c>Retention:DeviceIdleDays</c> menor que 1 (que revogaria todos os aparelhos).
/// </summary>
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

        if (options.Retention.DeviceIdleDays < 1)
        {
            return ValidateOptionsResult.Fail("Retention:DeviceIdleDays precisa ser pelo menos 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
