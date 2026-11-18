using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SlimeGoals.Server.Limits;

/// <summary>
/// Limites de taxa em janela fixa de um minuto: por IP e por e-mail nas rotas de <c>/auth</c>, e de falhas de <c>auth_key</c> por conta.
/// Os contadores usam o <see cref="TimeProvider"/> do servidor (o <c>AddRateLimiter</c> do ASP.NET não aceita um relógio próprio), o que
/// deixa o teste de reinício da janela determinístico. Os contadores ficam na memória do processo, como as janelas do ASP.NET.
/// </summary>
public sealed class RateLimits(TimeProvider time, IOptions<ServerOptions> options, ILogger<RateLimits> logger)
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly Counter _ips = new(time, Minute);
    private readonly Counter _requests = new(time, Minute);
    private readonly Counter _devices = new(time, Minute);
    private readonly Counter _emails = new(time, Minute);
    private readonly Counter _accountFailures = new(time, Minute);

    /// <summary>Conta um pedido do IP; devolve quanto esperar se passou do limite.</summary>
    public TimeSpan? HitIp(string ip) => _ips.Hit(ip, options.Value.Limits.AuthPerIpPerMinute);

    /// <summary>Conta um pedido do IP em qualquer rota (menos <c>/health</c>); devolve quanto esperar se passou do limite.</summary>
    public TimeSpan? HitRequest(string ip) => _requests.Hit(ip, options.Value.Limits.RequestsPerIpPerMinute);

    /// <summary>Conta um pedido de <c>/sync</c> do aparelho; devolve quanto esperar se passou do limite.</summary>
    public TimeSpan? HitDevice(Guid deviceId) => _devices.Hit(deviceId.ToString("N"), options.Value.Limits.SyncPerDevicePerMinute);

    /// <summary>Conta um pedido do e-mail (já normalizado), de qualquer IP; a chave é o SHA-256 do e-mail.</summary>
    public TimeSpan? HitEmail(string email)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
        var retry = _emails.Hit(key, options.Value.Limits.AuthPerEmailPerMinute);
        if (retry is not null)
        {
            logger.LogWarning("Limite de pedidos por e-mail atingido para {Email}.", MaskEmail(email));
        }

        return retry;
    }

    /// <summary>
    /// Reserva uma tentativa de <c>auth_key</c> da conta antes de comparar a chave; devolve quanto esperar se o limite já foi usado.
    /// A reserva é atômica, então pedidos paralelos não passam todos pela checagem. Quem acerta a chave devolve com
    /// <see cref="RefundKeyAttempt"/>; quem erra deixa a reserva gasta.
    /// </summary>
    public TimeSpan? ReserveKeyAttempt(Guid account)
    {
        var retry = _accountFailures.Hit(account.ToString("N"), options.Value.Limits.AuthKeyFailuresPerAccountPerMinute);
        if (retry is not null)
        {
            logger.LogWarning("Limite de falhas de chave atingido para a conta {Account}.", account);
        }

        return retry;
    }

    public void RefundKeyAttempt(Guid account) => _accountFailures.Remove(account.ToString("N"));

    /// <summary>Máscara para log: primeira letra, três asteriscos e o domínio (<c>a***@exemplo.com</c>).</summary>
    public static string MaskEmail(string email)
    {
        var at = email.LastIndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }

    private sealed class Counter(TimeProvider time, TimeSpan window)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, (DateTimeOffset Start, int Count)> _entries = [];
        private DateTimeOffset _lastPrune = time.GetUtcNow();

        public TimeSpan? Hit(string key, int limit)
        {
            lock (_gate)
            {
                var now = Current(key, out var entry);
                if (entry.Count >= limit)
                {
                    return Remaining(entry.Start, now);
                }

                _entries[key] = (entry.Start, entry.Count + 1);
                return null;
            }
        }

        /// <summary>Devolve uma contagem da janela atual (a chave certa não deve gastar a tentativa).</summary>
        public void Remove(string key)
        {
            lock (_gate)
            {
                Current(key, out var entry);
                if (entry.Count > 0)
                {
                    _entries[key] = (entry.Start, entry.Count - 1);
                }
            }
        }

        // Devolve a entrada da janela atual, ou uma nova e vazia se a anterior venceu.
        private DateTimeOffset Current(string key, out (DateTimeOffset Start, int Count) entry)
        {
            var now = time.GetUtcNow();
            Prune(now);
            if (!_entries.TryGetValue(key, out entry) || now - entry.Start >= window)
            {
                entry = (now, 0);
            }

            return now;
        }

        private void Prune(DateTimeOffset now)
        {
            if (now - _lastPrune < window)
            {
                return;
            }

            _lastPrune = now;
            foreach (var key in _entries.Where(e => now - e.Value.Start >= window).Select(e => e.Key).ToList())
            {
                _entries.Remove(key);
            }
        }

        private TimeSpan Remaining(DateTimeOffset start, DateTimeOffset now) => start + window - now;
    }
}
