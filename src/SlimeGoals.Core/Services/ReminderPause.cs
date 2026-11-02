using System.Diagnostics;
using System.Globalization;
using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Core.Services;

/// <summary>Estado da pausa dos lembretes.</summary>
/// <param name="IsPaused">Os lembretes estão pausados.</param>
/// <param name="ResumedAt">Instante (UTC) da última retomada, se houver.</param>
public sealed record PauseState(bool IsPaused, DateTimeOffset? ResumedAt);

/// <summary>Pausa e retoma os lembretes; o estado fica em <see cref="ISettingsRepository"/> e sobrevive ao reinício.</summary>
public sealed class ReminderPause(ISettingsRepository settings, TimeProvider time, DataChangeNotifier? changes = null)
{
    /// <summary>Chave: "1" enquanto pausado.</summary>
    public const string PausedKey = "reminders.paused";

    /// <summary>Chave: instante da última retomada (ISO 8601, UTC).</summary>
    public const string ResumedAtKey = "reminders.resumed_at";

    /// <summary>Lê o estado atual; uma retomada ilegível é tratada como ausente.</summary>
    public async Task<PauseState> GetAsync(CancellationToken ct = default)
    {
        var paused = await settings.GetAsync(PausedKey, ct) == "1";
        DateTimeOffset? resumedAt = null;
        if (await settings.GetAsync(ResumedAtKey, ct) is { } raw)
        {
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                resumedAt = parsed;
            else
                Trace.TraceWarning($"Valor ilegível em {ResumedAtKey}: '{raw}'.");
        }
        return new PauseState(paused, resumedAt);
    }

    /// <summary>Pausa os lembretes e avisa. Idempotente: já pausado, não grava nem avisa.</summary>
    public async Task PauseAsync(CancellationToken ct = default)
    {
        if ((await GetAsync(ct)).IsPaused)
            return;
        await settings.SetAsync(PausedKey, "1", ct);
        changes?.NotifyChanged();
    }

    /// <summary>Retoma os lembretes: o intervalo de hoje recomeça de agora. Sem pausa, não faz nada.</summary>
    public async Task ResumeAsync(CancellationToken ct = default)
    {
        if (!(await GetAsync(ct)).IsPaused)
            return;
        await settings.SetAsync(ResumedAtKey, time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture), ct);
        await settings.RemoveAsync(PausedKey, ct);
        changes?.NotifyChanged();
    }
}
