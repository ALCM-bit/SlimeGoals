#if SYNC_DEV
using Android.App;
using Android.Content;
using Android.Util;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.Android.Sync;

/// <summary>
/// Confere os vetores fixos da criptografia no aparelho, só no APK de desenvolvimento. Exige a permissão <c>DUMP</c>,
/// que só o shell do <c>adb</c> e o sistema têm: nenhum outro app dispara. O registro diz só o que passou.
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
        _ = Task.Run(() =>
        {
            try
            {
                var results = CryptoSelfCheck.Run();
                Log.Info(Tag, $"cripto: {results.Count(r => r.Passed)}/{results.Count} vetores conferem");
                foreach (var failed in results.Where(r => !r.Passed))
                    Log.Warn(Tag, $"cripto: falhou {failed.Name}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
#endif
