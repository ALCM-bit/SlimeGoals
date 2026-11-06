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
    private bool _resetInCycle; // só muda com _running na mão

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

        _resetInCycle = false;
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
        var resuming = (await store.GetStatusAsync(ct).ConfigureAwait(false)).FullResync;
        // Registros vivos ficam abaixo da limpeza: quem lê tudo precisa passar por ela sem receber outro reset.
        var full = after == 0 || resuming || _resetInCycle;
        if (after == 0)
        {
            await RememberQueuedKeysAsync(ct).ConfigureAwait(false);
            // Sem 410 em after = 0, quem só enviou até aqui nunca saberia das marcas limpas: o que o servidor não devolver sai no fim.
            if (!resuming)
                await store.BeginFullResyncAsync(ct).ConfigureAwait(false);
        }
        while (true)
        {
            SyncPullPage page;
            try
            {
                page = await transport.PullAsync(after, PullLimit, full, ct).ConfigureAwait(false);
            }
            // Um segundo reset segue adiante e vira Failed: repetir poderia não terminar nunca.
            catch (SyncResetRequiredException) when (!_resetInCycle)
            {
                _resetInCycle = true;
                await store.BeginFullResyncAsync(ct).ConfigureAwait(false);
                after = 0;
                full = true;
                continue;
            }

            if (page.Records.Count > 0)
            {
                var lastSeq = page.Records.Max(r => r.Seq);
                if (lastSeq <= after)
                    throw new InvalidOperationException($"O servidor devolveu uma página que não avança: seq {lastSeq}, depois de {after}.");
                await ApplyAsync(page, lastSeq, deviceId, ct).ConfigureAwait(false);
                after = lastSeq;
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

    /// <summary>
    /// Ao ligar, a fila leva tudo com base 0, e o servidor pode já ter marca de exclusão de algum item. A marca só traz a
    /// chave e passa uma vez: sem a chave guardada antes de baixar, o item ficaria em conflito para sempre.
    /// </summary>
    private async Task RememberQueuedKeysAsync(CancellationToken ct)
    {
        while (true)
        {
            var ids = await store.ReadUnkeyedAsync(PushBatch, ct).ConfigureAwait(false);
            if (ids.Count == 0)
                return;
            await store.RememberKeysAsync(ids.Select(id => new OutboxKey(id, crypto.RecordKey(id))).ToList(), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Abre tudo antes de aplicar: uma versão desconhecida pausa sem deixar a página pela metade.</summary>
    private async Task ApplyAsync(SyncPullPage page, long lastSeq, Guid deviceId, CancellationToken ct)
    {
        var records = page.Records.Select(Open).ToList();
        using (changes.Defer())
        {
            // O portão pode falhar depois do COMMIT, e aí as telas ainda precisam recarregar.
            try
            {
                await gate.RunAsync(c => store.ApplyPageAsync(records, lastSeq, deviceId, c), ct).ConfigureAwait(false);
            }
            finally
            {
                changes.NotifyChanged();
            }
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
            try
            {
                await gate.RunAsync(async c =>
                {
                    var goals = await store.FinishFullResyncAsync(c).ConfigureAwait(false);
                    removed = goals.Count > 0;
                    return goals;
                }, ct).ConfigureAwait(false);
            }
            finally
            {
                if (removed)
                    changes.NotifyChanged();
            }
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
            var sent = keyed.ToDictionary(k => k.Key, k => k.Item);

            var result = await transport.PushAsync(batch, ct).ConfigureAwait(false);

            var confirmations = new List<PushConfirmation>();
            foreach (var accepted in result.Accepted)
            {
                if (sent.TryGetValue(accepted.RecordKey, out var item))
                    confirmations.Add(new PushConfirmation(item.RecordId, item.Kind, item.IsDelete, accepted.RecordKey, accepted.Seq, item.Revision));
                else
                    Trace.TraceWarning($"O servidor aceitou uma chave que não foi enviada: {accepted.RecordKey}.");
            }
            await store.ConfirmPushAsync(confirmations, ct).ConfigureAwait(false);

            // Reler a fila só devolveria o item em conflito. Lote sem nada confirmado também não anda: não insiste.
            if (result.Conflicts.Count > 0 || confirmations.Count == 0)
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
