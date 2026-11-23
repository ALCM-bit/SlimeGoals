using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SlimeGoals.Sync;

/// <summary>
/// Pinning (MASVS-NETWORK-2): além da validação do sistema, a cadeia do servidor precisa passar por uma raiz do Let's
/// Encrypt, a CA do certificado <c>.ts.net</c> e do Caddy. Fixa a chave pública (SPKI) das raízes X1 (RSA, até 2035) e
/// X2 (ECDSA, até 2040) e das raízes da geração Y, YR (RSA) e YE (ECDSA), até 2045. A geração Y já emite com assinatura
/// cruzada pela X1 e pela X2; quando o sistema passar a confiar nela direto, a cadeia termina nela, sem passar pelas
/// antigas. Uma raiz nova do Let's Encrypt entra aqui antes de valer. O loopback fica só com a validação do sistema:
/// não há rede no caminho.
/// </summary>
public static class CertificatePins
{
    /// <summary><c>base64(SHA-256(SubjectPublicKeyInfo))</c> de ISRG Root X1, X2, YR e YE.</summary>
    public static IReadOnlySet<string> Roots { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "C5+lpZ7tcVwmwQIMcRtPbsQtWLABXhQzejna0wHFr8M=", // ISRG Root X1
        "diGVwiVYbubAI3RW4hB9xU8e/CH2GnkuvVFZE8zmgzI=", // ISRG Root X2
        "fk6IOKit1ild5647BH06ujSIq5XbCgqlbYl6ANhhi88=", // Root YR
        "sCkq5UWXjg+7mKu9lMhhYF5bGLsy7VI/UNW3tccdR7w=", // Root YE
    };

    public static string SpkiHash(X509Certificate2 certificate) =>
        Convert.ToBase64String(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));

    /// <summary>
    /// O caminho que a validação aceitou. No desktop, a cadeia já vem montada. No Android, a plataforma valida, mas a
    /// cadeia vem sem montar, só com o que o servidor apresentou em <see cref="X509ChainPolicy.ExtraStore"/>: aí ela é
    /// montada de novo com a mesma política, menos a revogação, que a plataforma já conferiu. Conferir o pin no que o
    /// servidor apresentou não basta, porque qualquer um pode anexar uma cópia de uma raiz fixada. Sem caminho (cadeia
    /// que não monta, sem folha), sai vazio.
    /// </summary>
    public static IEnumerable<X509Certificate2> ValidatedPath(X509Certificate2? leaf, X509Chain? chain)
    {
        if (chain is null)
            yield break;
        if (chain.ChainElements.Count > 0)
        {
            foreach (var element in chain.ChainElements)
                yield return element.Certificate;
            yield break;
        }
        if (leaf is null)
            yield break;
        using var rebuilt = new X509Chain { ChainPolicy = chain.ChainPolicy.Clone() };
        // A revogação já passou pela plataforma; aqui ela só derrubaria o caminho (o Let's Encrypt não tem mais OCSP).
        rebuilt.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (!rebuilt.Build(leaf))
            yield break;
        foreach (var element in rebuilt.ChainElements)
            yield return element.Certificate;
    }

    /// <summary>
    /// Sem erro na validação do sistema e, fora do loopback, com uma raiz fixada no caminho validado
    /// (<see cref="ValidatedPath"/>), que só é percorrido depois das duas primeiras conferências.
    /// </summary>
    public static bool IsTrusted(Uri server, SslPolicyErrors errors, IEnumerable<X509Certificate2> chain)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(chain);
        if (errors != SslPolicyErrors.None)
            return false;
        return server.IsLoopback || chain.Any(certificate => Roots.Contains(SpkiHash(certificate)));
    }
}
