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

        public int AuthPerEmailPerMinute { get; set; } = 10;
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

        return ValidateOptionsResult.Success;
    }
}
