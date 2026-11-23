using Android.App;
using Android.Views;
using SlimeGoals.App.Account;

namespace SlimeGoals.Android.Sync;

/// <summary>
/// <c>FLAG_SECURE</c> na janela da <see cref="MainActivity"/> enquanto uma página de conta está à vista: a tela sai preta
/// em capturas, gravações e na miniatura dos recentes. A Activity recriada recebe o estado de novo em
/// <see cref="CurrentActivity"/>.
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
        if (protect)
            activity.Window?.AddFlags(WindowManagerFlags.Secure);
        else
            activity.Window?.ClearFlags(WindowManagerFlags.Secure);
    });
}
