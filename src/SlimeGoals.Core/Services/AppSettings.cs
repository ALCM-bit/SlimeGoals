using System.Diagnostics;
using System.Globalization;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

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

    /// <summary>Chave: endereço do servidor de sincronização, usado ao criar conta, entrar ou recuperar.</summary>
    public const string SyncServerKey = "sync_server";

    /// <summary>Chave: "on" ou "off" para a janelinha do cronômetro (desktop).</summary>
    public const string MiniTimerKey = "timer.mini_window";

    /// <summary>Chave: posição da janelinha do cronômetro, "x,y" em pixels da tela.</summary>
    public const string MiniTimerPositionKey = "timer.mini_window_position";

    /// <summary>Chave: sequência da última sessão de Pomodoro começada, em JSON (sem a meta).</summary>
    public const string FocusPlanKey = "focus.plan";

    /// <summary>Servidor padrão: o do <c>compose.yaml</c> de desenvolvimento.</summary>
    public static Uri DefaultSyncServer { get; } = new("http://127.0.0.1:8080");

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

    /// <summary>Servidor de sincronização; ausente, ou sem endereço absoluto http/https, vale <see cref="DefaultSyncServer"/>.</summary>
    public async Task<Uri> GetSyncServerAsync(CancellationToken ct = default)
    {
        if (await settings.GetAsync(SyncServerKey, ct) is not { } raw)
            return DefaultSyncServer;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var server)
            && (server.Scheme == Uri.UriSchemeHttp || server.Scheme == Uri.UriSchemeHttps))
            return server;
        Trace.TraceWarning($"Valor inválido em {SyncServerKey}.");
        return DefaultSyncServer;
    }

    /// <summary>Grava o servidor de sincronização; endereço relativo lança <see cref="ArgumentException"/>.</summary>
    public async Task SetSyncServerAsync(Uri server, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (!server.IsAbsoluteUri)
            throw new ArgumentException("O servidor precisa de um endereço absoluto.", nameof(server));
        await settings.SetAsync(SyncServerKey, server.AbsoluteUri, ct);
        changes?.NotifyChanged();
    }

    /// <summary>Janelinha do cronômetro ligada; ausente, ou qualquer valor que não "off", vale ligada.</summary>
    public async Task<bool> GetMiniTimerEnabledAsync(CancellationToken ct = default) =>
        await settings.GetAsync(MiniTimerKey, ct) != "off";

    /// <summary>Liga ou desliga a janelinha do cronômetro.</summary>
    public async Task SetMiniTimerEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        await settings.SetAsync(MiniTimerKey, enabled ? "on" : "off", ct);
        changes?.NotifyChanged();
    }

    /// <summary>Posição gravada da janelinha; nula se ausente ou ilegível.</summary>
    public async Task<(int X, int Y)?> GetMiniTimerPositionAsync(CancellationToken ct = default)
    {
        if (await settings.GetAsync(MiniTimerPositionKey, ct) is not { } raw)
            return null;
        var parts = raw.Split(',');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            return (x, y);
        Trace.TraceWarning($"Valor inválido em {MiniTimerPositionKey}.");
        return null;
    }

    /// <summary>
    /// Grava a posição da janelinha. Não avisa o <see cref="DataChangeNotifier"/>: nenhuma tela mostra a
    /// posição, e cada arrasto faria todas recarregarem à toa.
    /// </summary>
    public Task SetMiniTimerPositionAsync(int x, int y, CancellationToken ct = default) =>
        settings.SetAsync(MiniTimerPositionKey, string.Create(CultureInfo.InvariantCulture, $"{x},{y}"), ct);

    /// <summary>Sequência sugerida para a próxima sessão; ausente, ilegível ou fora dos limites vale <see cref="FocusPlan.Default"/>.</summary>
    public async Task<FocusPlan> GetFocusPlanAsync(CancellationToken ct = default)
    {
        if (await settings.GetAsync(FocusPlanKey, ct) is not { } raw)
            return FocusPlan.Default;
        // Um plano sem blocos ou com Repeat 0 se lê, mas quebraria as contas de passo: só vale o que passa na validação.
        if (FocusPlan.FromJson(raw) is { } plan && plan.Validate().Count == 0)
            return plan;
        Trace.TraceWarning($"Valor inválido em {FocusPlanKey}.");
        return FocusPlan.Default;
    }

    /// <summary>Grava a sequência sugerida para a próxima sessão.</summary>
    public async Task SetFocusPlanAsync(FocusPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await settings.SetAsync(FocusPlanKey, plan.ToJson(), ct);
        changes?.NotifyChanged();
    }
}
