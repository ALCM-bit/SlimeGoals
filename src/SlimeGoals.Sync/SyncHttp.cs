using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SlimeGoals.Sync;

/// <summary>O cliente HTTP de um servidor de sincronização, o mesmo nos dois heads.</summary>
public static class SyncHttp
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Não segue redirecionamento: um 3xx para outro host levaria o token junto. No Android, o texto puro só passa para
    /// 127.0.0.1 e localhost (network_security_config_dev). O certificado passa pelo sistema e por
    /// <see cref="CertificatePins"/>.
    /// </summary>
    public static HttpClient Create(Uri server) =>
        new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (request, leaf, chain, errors) =>
                Validate(request.RequestUri ?? server, leaf, chain, errors),
        })
        { BaseAddress = server, Timeout = RequestTimeout };

    private static bool Validate(Uri server, X509Certificate2? leaf, X509Chain? chain, SslPolicyErrors errors)
    {
        if (CertificatePins.IsTrusted(server, errors, CertificatePins.ValidatedPath(leaf, chain)))
            return true;
        // Sem o endereço nem o certificado: só o motivo.
        Trace.TraceWarning(errors == SslPolicyErrors.None
            ? "Certificado do servidor fora das raízes fixadas: conexão recusada."
            : $"Certificado do servidor recusado pelo sistema: {errors}.");
        return false;
    }
}
