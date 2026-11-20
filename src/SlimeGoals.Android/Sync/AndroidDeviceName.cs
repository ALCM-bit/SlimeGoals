using Android.Content;
using Android.OS;
using Android.Provider;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Android.Sync;

/// <summary>O nome que o celular mostra (por exemplo, "Galaxy S23 Ultra"), para sugerir ao entrar na conta.</summary>
internal static class AndroidDeviceName
{
    public static string Get(Context context)
    {
        var name = Settings.Global.GetString(context.ContentResolver, Settings.Global.DeviceName);
        if (string.IsNullOrWhiteSpace(name))
            name = Build.Model;
        name = string.IsNullOrWhiteSpace(name) ? "Android" : name.Trim();
        return name.Length > AccountRules.MaxDeviceNameLength ? name[..AccountRules.MaxDeviceNameLength] : name;
    }
}
