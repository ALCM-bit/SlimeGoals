#if SYNC_DEV
using Android.App;
using Android.Content;
using System.Security.Cryptography;
using Android.Util;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Android.Sync;

/// <summary>
/// Confere os vetores fixos da criptografia e uma ida e volta no cofre do Keystore, só no APK de desenvolvimento. Exige a permissão <c>DUMP</c>,
/// que só o shell do <c>adb</c> e o sistema têm: nenhum outro app dispara. O registro diz só o que passou; da falha do
/// cofre, só o tipo da exceção.
/// <c>adb shell am broadcast -a dev.alcm.slimegoals.VERIFICAR_CRIPTO -n dev.alcm.slimegoals/dev.alcm.slimegoals.CryptoSelfCheckReceiver</c>
/// </summary>
[BroadcastReceiver(Name = "dev.alcm.slimegoals.CryptoSelfCheckReceiver", Enabled = true, Exported = true,
    Permission = "android.permission.DUMP")]
[IntentFilter(["dev.alcm.slimegoals.VERIFICAR_CRIPTO"])]
public sealed class CryptoSelfCheckReceiver : BroadcastReceiver
{
    private const string Tag = "SlimeGoals";

    public override void OnReceive(Context? context, Intent? intent)
    {
        var pending = GoAsync();
        var vault = AndroidApplication.Services?.GetService<ISecretVault>()
            ?? (context is null ? null : new AndroidKeystoreVault(context));
        _ = Task.Run(async () =>
        {
            try
            {
                var results = CryptoSelfCheck.Run();
                Log.Info(Tag, $"cripto: {results.Count(r => r.Passed)}/{results.Count} vetores conferem");
                foreach (var failed in results.Where(r => !r.Passed))
                    Log.Warn(Tag, $"cripto: falhou {failed.Name}");
                if (vault is not null)
                    await CheckVaultAsync(vault).ConfigureAwait(false);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    /// <summary>Grava, lê e apaga um segredo de teste, com outro nome que o da sessão.</summary>
    private static async Task CheckVaultAsync(ISecretVault vault)
    {
        const string name = "autoteste";
        try
        {
            var probe = RandomNumberGenerator.GetBytes(32);
            await vault.SetAsync(name, probe).ConfigureAwait(false);
            var back = await vault.GetAsync(name).ConfigureAwait(false);
            await vault.DeleteAsync(name).ConfigureAwait(false);
            var deleted = await vault.GetAsync(name).ConfigureAwait(false) is null;
            if (back is not null && back.AsSpan().SequenceEqual(probe) && deleted)
                Log.Info(Tag, "cofre: ida e volta confere");
            else
                Log.Warn(Tag, "cofre: falhou (o que voltou não confere)");
        }
        catch (Exception e)
        {
            Log.Warn(Tag, $"cofre: falhou ({TraceText.Types(e)})");
        }
    }
}
#endif
