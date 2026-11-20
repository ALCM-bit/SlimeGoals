namespace SlimeGoals.Sync;

/// <summary>O cliente HTTP de um servidor de sincronização, o mesmo nos dois heads.</summary>
public static class SyncHttp
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Não segue redirecionamento: um 3xx para outro host levaria o token junto. No Android, o texto puro só passa para
    /// 127.0.0.1 e localhost (network_security_config_dev).
    /// </summary>
    public static HttpClient Create(Uri server) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = server, Timeout = RequestTimeout };
}
