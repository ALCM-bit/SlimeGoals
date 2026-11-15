using System.Diagnostics;
using Android.Content;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Diagnostics;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// Guarda o <see cref="StopRequestSecret"/> nas preferências privadas do app (outros apps não leem), fora do banco
/// que sincroniza. Criado na primeira leitura.
/// </summary>
internal sealed class StopTimerSecretStore(Context context)
{
    private const string FileName = "cronometro";
    private const string Key = "codigo_parar";

    private readonly Lock _lock = new();
    private string? _secret;

    /// <summary>O código desta instalação; nulo se as preferências falharem (aí nenhum "Parar" de fora vale).</summary>
    public string? Get()
    {
        lock (_lock)
        {
            if (_secret is not null)
                return _secret;
            try
            {
                var preferences = context.GetSharedPreferences(FileName, FileCreationMode.Private)!;
                var secret = preferences.GetString(Key, null);
                if (string.IsNullOrEmpty(secret))
                {
                    secret = StopRequestSecret.Create();
                    if (!preferences.Edit()!.PutString(Key, secret)!.Commit())
                    {
                        Trace.TraceError("Falha ao gravar o código do Parar do cronômetro.");
                        return null;
                    }
                }
                return _secret = secret;
            }
            catch (Exception e)
            {
                Trace.TraceError($"Falha ao ler o código do Parar do cronômetro: {TraceText.Of(e)}");
                return null;
            }
        }
    }
}
