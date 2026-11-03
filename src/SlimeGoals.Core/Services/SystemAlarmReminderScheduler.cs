using System.Diagnostics;
using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Agendador que delega ao alarme do sistema: mantém UM alarme armado para o próximo lembrete (ou nenhum).
/// Não há temporizador no processo, então o alarme sobrevive a ele; ao tocar, quem hospeda chama <see cref="FiredAsync"/>.
/// </summary>
public sealed class SystemAlarmReminderScheduler(ReminderService reminders, ISystemAlarm alarm,
    DataChangeNotifier changes, TimeProvider time) : IReminderScheduler
{
    /// <summary>Espera antes de tentar de novo quando a sincronização falha.</summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _subscriptionLock = new();
    private bool _subscribed;
    private bool _disposed;

    /// <summary>Assina as mudanças (uma vez) e reagenda; não expira lembretes pendentes.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        lock (_subscriptionLock)
        {
            if (!_subscribed && !_disposed)
            {
                changes.Changed += OnChanged;
                _subscribed = true;
            }
        }

        await RescheduleAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RescheduleAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            DateTimeOffset? next;
            try
            {
                next = await reminders.SyncAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                Trace.TraceError($"Falha ao sincronizar lembretes: {e}");
                next = time.GetUtcNow() + RetryAfterFailure;
            }

            if (next is { } at)
                alarm.Set(at);
            else
                alarm.Cancel();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>O alarme tocou: reagenda (o que dispara os devidos) e avisa, para as telas recarregarem.</summary>
    public async Task FiredAsync(CancellationToken ct = default)
    {
        await RescheduleAsync(ct).ConfigureAwait(false);
        changes.NotifyChanged();
    }

    /// <summary>Cancela a assinatura; o alarme do sistema continua armado.</summary>
    public void Dispose()
    {
        lock (_subscriptionLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_subscribed)
                changes.Changed -= OnChanged;
            _subscribed = false;
        }
    }

    // Só reagenda, nunca avisa: sem laço.
    private void OnChanged(object? sender, EventArgs e) => _ = RescheduleSafelyAsync();

    private async Task RescheduleSafelyAsync()
    {
        try
        {
            await RescheduleAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao reagendar lembretes: {e}");
        }
    }
}
