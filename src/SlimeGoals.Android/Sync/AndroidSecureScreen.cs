using Android.App;
using Android.Views;
using SlimeGoals.App.Account;

// Para Window.SetHideOverlayWindows (API 31). Permissão normal: vem com a instalação, sem pedido.
[assembly: UsesPermission("android.permission.HIDE_OVERLAY_WINDOWS")]

namespace SlimeGoals.Android.Sync;

/// <summary>
/// Enquanto uma página de conta está à vista, a janela da <see cref="MainActivity"/> liga o <c>FLAG_SECURE</c> (a tela
/// sai preta em capturas, gravações e na miniatura dos recentes) e se defende de tapjacking: descarta o toque que chega
/// com outra janela por cima e, da API 31 em diante, esconde as sobreposições de outros apps. A Activity recriada
/// recebe o estado de novo em <see cref="CurrentActivity"/>.
/// </summary>
internal sealed class AndroidSecureScreen : ISecureScreen
{
    private readonly Lock _lock = new();
    private Activity? _activity;
    private bool _protected;

    public Activity? CurrentActivity
    {
        set
        {
            bool protect;
            lock (_lock)
            {
                _activity = value;
                protect = _protected;
            }
            Apply(value, protect);
        }
    }

    public void SetProtected(bool value)
    {
        Activity? activity;
        lock (_lock)
        {
            _protected = value;
            activity = _activity;
        }
        Apply(activity, value);
    }

    private static void Apply(Activity? activity, bool protect) => activity?.RunOnUiThread(() =>
    {
        if (activity.Window is not { } window)
            return;
        if (protect)
            window.AddFlags(WindowManagerFlags.Secure);
        else
            window.ClearFlags(WindowManagerFlags.Secure);
        window.DecorView.FilterTouchesWhenObscured = protect;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            window.SetHideOverlayWindows(protect);
    });
}
