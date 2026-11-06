using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Core.Sync;

public enum SyncCycleResult { Done, Disabled, Skipped, Paused, Failed, Incomplete }

/// <summary>
/// Roda o ciclo de sincronização (spec de sincronização §4): baixa, aplica e envia a fila, um ciclo por vez.
/// Uma falha fica em <c>last_error</c> e nunca chega a quem chamou (RS08): só o cancelamento lança.
/// </summary>
public sealed class SyncEngine(ISyncStateStore state, ISyncLocalStore store, ISyncTransport transport, ISyncCrypto crypto,
    IRemoteDeletionGate gate, DataChangeNotifier changes, TimeProvider time)
{
    public const int PullLimit = 200;
    public const int PushBatch = 100;
    public const int MaxAttempts = 3;

    private const string UpdateAppMessage = "Atualize o app para continuar sincronizando.";

    private readonly SemaphoreSlim _running = new(1, 1);

    public async Task<SyncCycleResult> RunCycleAsync(CancellationToken ct = default)
    {
        if (!_running.Wait(0))
            return SyncCycleResult.Skipped;
        try
        {
            return await RunExclusiveAsync(ct).ConfigureAwait(false);
        }
        // Um token que não é o de quem chamou é tempo esgotado na rede: vale como falha.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SyncFormatException e)
        {
            Trace.TraceWarning($"Sincronização pausada: {e.Message}");
            await RecordErrorAsync(UpdateAppMessage).ConfigureAwait(false);
            return SyncCycleResult.Paused;
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha na sincronização: {e}");
            await RecordErrorAsync(e.Message).ConfigureAwait(false);
            return SyncCycleResult.Failed;
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task<SyncCycleResult> RunExclusiveAsync(CancellationToken ct)
    {
        var current = await state.GetAsync(ct).ConfigureAwait(false);
        if (!current.Enabled || current.DeviceId is not { } deviceId)
            return SyncCycleResult.Disabled;

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            await PullAsync(deviceId, ct).ConfigureAwait(false);
            if (await PushAsync(deviceId, ct).ConfigureAwait(false))
            {
                await store.RecordCycleAsync(time.GetUtcNow(), null, ct).ConfigureAwait(false);
                return SyncCycleResult.Done;
            }
        }
        return SyncCycleResult.Incomplete;
    }

    private async Task PullAsync(Guid deviceId, CancellationToken ct)
    {
        var after = (await state.GetAsync(ct).ConfigureAwait(false)).LastSeq;
        while (true)
        {
            SyncPullPage page;
            try
            {
                page = await transport.PullAsync(after, PullLimit, ct).ConfigureAwait(false);
            }
            // Vindo do zero não há o que refazer: tentar de novo não terminaria nunca.
            catch (SyncResetRequiredException) when (after > 0)
            {
                await store.BeginFullResyncAsync(ct).ConfigureAwait(false);
                after = 0;
                continue;
            }

            if (page.Records.Count > 0)
            {
                after = page.Records.Max(r => r.Seq);
                await ApplyAsync(page, after, deviceId, ct).ConfigureAwait(false);
            }
            else if (page.HasMore)
            {
                throw new InvalidOperationException("O servidor anunciou mais registros numa página vazia.");
            }

            if (!page.HasMore)
                break;
        }
        await FinishFullResyncAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Abre tudo antes de aplicar: uma versão desconhecida pausa sem deixar a página pela metade.</summary>
    private async Task ApplyAsync(SyncPullPage page, long lastSeq, Guid deviceId, CancellationToken ct)
    {
        var records = page.Records.Select(Open).ToList();
        using (changes.Defer())
        {
            await gate.RunAsync(c => store.ApplyPageAsync(records, lastSeq, deviceId, c), ct).ConfigureAwait(false);
            changes.NotifyChanged();
        }
    }

    private IncomingRecord Open(RemoteRecord record)
    {
        if (record.Blob is null)
            return new IncomingRecord(record.RecordKey, record.Seq, null);
        try
        {
            return new IncomingRecord(record.RecordKey, record.Seq, crypto.Open(record.RecordKey, record.Blob));
        }
        catch (Exception e) when (e is not (SyncFormatException or OperationCanceledException))
        {
            Trace.TraceError($"Registro ilegível ignorado na sincronização ({record.RecordKey}, seq {record.Seq}): {e.Message}");
            return new IncomingRecord(record.RecordKey, record.Seq, null, Unreadable: true);
        }
    }

    private async Task FinishFullResyncAsync(CancellationToken ct)
    {
        var removed = false;
        using (changes.Defer())
        {
            await gate.RunAsync(async c =>
            {
                var goals = await store.FinishFullResyncAsync(c).ConfigureAwait(false);
                removed = goals.Count > 0;
                return goals;
            }, ct).ConfigureAwait(false);
            if (removed)
                changes.NotifyChanged();
        }
    }

    /// <summary>Envia a fila em lotes. Falso quando é preciso baixar de novo antes de continuar.</summary>
    private async Task<bool> PushAsync(Guid deviceId, CancellationToken ct)
    {
        while (true)
        {
            var items = await store.ReadOutboxAsync(PushBatch, ct).ConfigureAwait(false);
            if (items.Count == 0)
                return true;

            var keyed = items.Select(i => (Key: crypto.RecordKey(i.RecordId), Item: i)).ToList();
            await store.RememberKeysAsync(keyed.Select(k => new OutboxKey(k.Item.RecordId, k.Key)).ToList(), ct).ConfigureAwait(false);
            var batch = keyed.Select(k => new PushItem(k.Key, k.Item.BaseSeq, Seal(k.Item, deviceId))).ToList();

            var result = await transport.PushAsync(batch, ct).ConfigureAwait(false);

            var sent = keyed.ToDictionary(k => k.Key, k => k.Item);
            await store.ConfirmPushAsync(result.Accepted.Select(a =>
            {
                var item = sent[a.RecordKey];
                return new PushConfirmation(item.RecordId, item.Kind, item.IsDelete, a.RecordKey, a.Seq, item.Revision);
            }).ToList(), ct).ConfigureAwait(false);

            // Reler a fila só devolveria o item em conflito. Lote sem resposta também não anda: não insiste.
            if (result.Conflicts.Count > 0 || result.Accepted.Count == 0)
                return false;
        }
    }

    private byte[]? Seal(OutboxItem item, Guid deviceId) => item.IsDelete
        ? null
        : crypto.Seal(new SyncEnvelope(SyncCodec.FormatVersion, item.Kind, item.RecordId, deviceId, item.Payload!));

    /// <summary>Gravar o erro não pode virar outra falha para quem chamou, nem depender do token que já pode ter caído.</summary>
    private async Task RecordErrorAsync(string message)
    {
        try
        {
            await store.RecordCycleAsync(null, message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao gravar o erro da sincronização: {e}");
        }
    }
}
