using System.Diagnostics;
using System.Globalization;
using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Core.Services;

/// <summary>O que acontece ao fechar a janela.</summary>
public enum CloseAction { Hide, Quit }

/// <summary>Configurações do app (spec RF08) na tabela settings; mudar avisa o <see cref="DataChangeNotifier"/>.</summary>
public sealed class AppSettings(ISettingsRepository settings, DataChangeNotifier? changes = null)
{
    /// <summary>Chave: minutos do adiar.</summary>
    public const string SnoozeMinutesKey = "reminders.snooze_minutes";

    /// <summary>Chave: "hide" ou "quit".</summary>
    public const string CloseActionKey = "window.close_action";

    /// <summary>Adiar padrão, em minutos.</summary>
    public const int DefaultSnoozeMinutes = 10;

    /// <summary>Opções de adiar aceitas, em minutos.</summary>
    public static IReadOnlyList<int> SnoozeOptions { get; } = [5, 10, 15, 30, 60];

    /// <summary>Adiar em minutos; ausente ou inválido vale <see cref="DefaultSnoozeMinutes"/>.</summary>
    public async Task<int> GetSnoozeMinutesAsync(CancellationToken ct = default)
    {
        if (await settings.GetAsync(SnoozeMinutesKey, ct) is not { } raw)
            return DefaultSnoozeMinutes;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && SnoozeOptions.Contains(minutes))
            return minutes;
        Trace.TraceWarning($"Valor inválido em {SnoozeMinutesKey}: '{raw}'.");
        return DefaultSnoozeMinutes;
    }

    /// <summary>Grava o adiar; valor fora de <see cref="SnoozeOptions"/> lança <see cref="ArgumentOutOfRangeException"/>.</summary>
    public async Task SetSnoozeMinutesAsync(int minutes, CancellationToken ct = default)
    {
        if (!SnoozeOptions.Contains(minutes))
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Adiar fora das opções permitidas.");
        await settings.SetAsync(SnoozeMinutesKey, minutes.ToString(CultureInfo.InvariantCulture), ct);
        changes?.NotifyChanged();
    }

    /// <summary>Ação ao fechar a janela; "quit" encerra, qualquer outro valor (ou ausente) mantém em segundo plano.</summary>
    public async Task<CloseAction> GetCloseActionAsync(CancellationToken ct = default) =>
        await settings.GetAsync(CloseActionKey, ct) == "quit" ? CloseAction.Quit : CloseAction.Hide;

    /// <summary>Grava a ação ao fechar a janela.</summary>
    public async Task SetCloseActionAsync(CloseAction action, CancellationToken ct = default)
    {
        await settings.SetAsync(CloseActionKey, action == CloseAction.Quit ? "quit" : "hide", ct);
        changes?.NotifyChanged();
    }
}
