using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Platform.Linux.Scheduling;

/// <summary>
/// Agendador que vive no processo: mantém UM temporizador armado para o menor entre o próximo lembrete
/// e a próxima meia-noite local (na virada, avisa que os dados mudaram para as telas recarregarem).
/// </summary>
public sealed class ProcessTimerReminderScheduler(ReminderService reminders, DataChangeNotifier changes, TimeProvider time)
    : IReminderScheduler
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _subscriptionLock = new();
    private ITimer? _timer;
    private DateTimeOffset _midnight;
    private bool _subscribed;
    private bool _disposed;

    /// <summary>Instante em que o timer está armado (para testes e diagnóstico); null antes do StartAsync.</summary>
    public DateTimeOffset? ArmedFor { get; private set; }

    /// <inheritdoc />
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

        await reminders.ExpirePendingAsync(ct).ConfigureAwait(false);
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

            var zone = time.LocalTimeZone;
            var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
            var midnight = DateRange.LocalMidnightToUtc(today.AddDays(1), zone);

            DateTimeOffset? next = null;
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
            }

            Arm(next is { } n && n < midnight ? n : midnight, midnight);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
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

        _timer?.Dispose();
        _timer = null;
    }

    private void Arm(DateTimeOffset armedFor, DateTimeOffset midnight)
    {
        _timer?.Dispose();
        _midnight = midnight;
        ArmedFor = armedFor;
        var due = armedFor - time.GetUtcNow();
        _timer = time.CreateTimer(_ => _ = OnTimerAsync(), null, due < TimeSpan.Zero ? TimeSpan.Zero : due,
            Timeout.InfiniteTimeSpan);
    }

    private async Task OnTimerAsync()
    {
        try
        {
            if (_disposed)
                return;
            if (ArmedFor == _midnight)
                changes.NotifyChanged();
            await RescheduleAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha no disparo do agendador de lembretes: {e}");
        }
    }

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
