using System.Diagnostics;
using Trace = System.Diagnostics.Trace;
using System.Security.Cryptography;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using SlimeGoals.Sync.Integrity;

namespace SlimeGoals.Android.Sync;

/// <summary>
/// Conferência de integridade do APK de distribuição (MASVS-RESILIENCE, decisão do autor: avisar e pausar a conta).
/// Assinatura diferente da oficial ou depurador conectado desligam a conta; root só gera aviso. Nada disso impede quem
/// controla o aparelho, só tira a sessão do caminho fácil de um APK refeito ou de um depurador. A assinatura e o root
/// não mudam com o app aberto e são lidos uma vez; o depurador, a cada conferência. Não vale no APK de
/// desenvolvimento, que sai com a chave de depuração.
/// </summary>
internal sealed class AndroidAppIntegrity(Context context) : IAppIntegrity
{
    /// <summary>SHA-256 do certificado da chave de assinatura oficial (<c>apksigner verify --print-certs</c>).</summary>
    private static readonly byte[] OfficialCertificate =
        Convert.FromHexString("e88d54af6d3ff404a7f0c72e37f58310ab15c3c2f8e5010afa9a5939b986bbd3");

    private static readonly string[] SuPaths =
    [
        "/system/bin/su", "/system/xbin/su", "/sbin/su", "/system/sbin/su", "/vendor/bin/su", "/su/bin/su",
        "/data/local/bin/su", "/data/local/xbin/su", "/system/app/Superuser.apk",
    ];

    private readonly Lazy<bool> _foreignSignature = new(() => IsForeignSignature(context));
    private readonly Lazy<bool> _rooted = new(IsRooted);

    public IntegrityReport Check()
    {
        var issues = new HashSet<IntegrityIssue>();
        if (_foreignSignature.Value)
            issues.Add(IntegrityIssue.ForeignSignature);
        if (IsDebugged(context))
            issues.Add(IntegrityIssue.Debugger);
        if (_rooted.Value)
            issues.Add(IntegrityIssue.Rooted);
        return issues.Count == 0 ? IntegrityReport.Clean : new IntegrityReport(issues);
    }

    /// <summary>Na falha de leitura, conta como estranha: a conta fica desligada em vez de confiar sem saber.</summary>
    private static bool IsForeignSignature(Context context)
    {
        try
        {
            var packages = context.PackageManager!;
            var name = context.PackageName!;
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
                return !packages.HasSigningCertificate(name, OfficialCertificate, CertificateType.Sha256);
#pragma warning disable CA1422 // Antes da API 28 não há HasSigningCertificate.
            var signatures = packages.GetPackageInfo(name, PackageInfoFlags.Signatures)?.Signatures;
#pragma warning restore CA1422
            return signatures is not { Count: 1 }
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(signatures[0].ToByteArray()!), OfficialCertificate);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Falha ao conferir a assinatura do app: {ex.GetType().FullName}");
            return true;
        }
    }

    private static bool IsDebugged(Context context) =>
        Debugger.IsAttached
        || global::Android.OS.Debug.IsDebuggerConnected
        || ((context.ApplicationInfo?.Flags ?? 0) & ApplicationInfoFlags.Debuggable) != 0;

    private static bool IsRooted()
    {
        try
        {
            return Build.Tags?.Contains("test-keys", StringComparison.Ordinal) == true || SuPaths.Any(File.Exists);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Falha ao procurar sinais de root: {ex.GetType().FullName}");
            return false;
        }
    }
}
