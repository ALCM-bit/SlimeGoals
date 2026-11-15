#if SYNC_DEV
using Android.Content;
using Android.OS;
using SlimeGoals.App.Account;

namespace SlimeGoals.Android.Sync;

/// <summary>
/// Área de transferência do Android para o código de recuperação: marca o clipe como sensível, e o Android 13+ não o
/// mostra na prévia. Chamada na thread principal, que é a da interface do Avalonia. No Android 10+, ler o clipe só
/// funciona com o app em foco: fora dele, a limpeza depois de 60 s não acontece.
/// </summary>
internal sealed class AndroidSensitiveClipboard(Context context) : ITextClipboard
{
    // ClipDescription.ExtraIsSensitive existe só a partir do API 33; o valor é o mesmo.
    private const string ExtraIsSensitive = "android.content.extra.IS_SENSITIVE";

    private ClipboardManager? Manager => context.GetSystemService(Context.ClipboardService) as ClipboardManager;

    public Task SetTextAsync(string text)
    {
        var clip = ClipData.NewPlainText("SlimeGoals", text)!;
        var extras = new PersistableBundle();
        extras.PutBoolean(ExtraIsSensitive, true);
        clip.Description!.Extras = extras;
        Manager?.PrimaryClip = clip;
        return Task.CompletedTask;
    }

    public Task<string?> TryGetTextAsync()
    {
        var clip = Manager?.PrimaryClip;
        return Task.FromResult(clip is { ItemCount: > 0 } ? clip.GetItemAt(0)?.Text : null);
    }

    public Task ClearAsync()
    {
        if (Manager is { } manager)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
                manager.ClearPrimaryClip();
            else
                manager.PrimaryClip = ClipData.NewPlainText("", "");
        }
        return Task.CompletedTask;
    }
}
#endif
