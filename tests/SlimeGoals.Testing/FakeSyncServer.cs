using SlimeGoals.Core.Sync;

namespace SlimeGoals.Testing;

/// <summary>
/// Servidor de sincronização em memória, de uma conta, com as regras 1 a 5 da §5 da spec. Aparelhos da
/// mesma conta usam a mesma instância.
/// </summary>
public sealed class FakeSyncServer : ISyncTransport
{
    private readonly object _lock = new();
    private readonly Dictionary<string, RemoteRecord> _records = [];
    private long _lastSeq;
    private long _purgedBefore;
    private bool _loseNextAck;
    private Exception? _nextFailure;

    /// <summary>O que o servidor guarda agora, em ordem de <c>seq</c>. Blob nulo = marca de exclusão.</summary>
    public IReadOnlyList<RemoteRecord> Records
    {
        get
        {
            lock (_lock)
                return _records.Values.OrderBy(r => r.Seq).ToList();
        }
    }

    /// <summary>O próximo envio é aplicado, mas a chamada lança, como numa conexão que caiu antes da resposta.</summary>
    public void LoseNextAck()
    {
        lock (_lock)
            _loseNextAck = true;
    }

    /// <summary>A próxima chamada lança <paramref name="failure"/> sem aplicar nada.</summary>
    public void FailNext(Exception failure)
    {
        lock (_lock)
            _nextFailure = failure;
    }

    /// <summary>Limpa as marcas de exclusão anteriores a <paramref name="seq"/>. Quem parou antes disso precisa refazer tudo.</summary>
    public void PurgeBefore(long seq)
    {
        lock (_lock)
        {
            _purgedBefore = seq;
            foreach (var record in _records.Values.Where(r => r.Blob is null && r.Seq < seq).ToList())
                _records.Remove(record.RecordKey);
        }
    }

    public Task<SyncPullPage> PullAsync(long after, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            ThrowIfFailing();
            // after 0 é a sincronização completa: pedir 410 aqui faria ela nunca terminar.
            if (after > 0 && after < _purgedBefore)
                throw new SyncResetRequiredException();

            var newer = _records.Values.Where(r => r.Seq > after).OrderBy(r => r.Seq).ToList();
            return Task.FromResult(new SyncPullPage(newer.Take(limit).ToList(), newer.Count > limit));
        }
    }

    public Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            ThrowIfFailing();
            var accepted = new List<PushAccepted>();
            var conflicts = new List<string>();
            foreach (var item in items)
            {
                var current = _records.TryGetValue(item.RecordKey, out var record) ? record.Seq : 0;
                if (item.BaseSeq != current)
                {
                    conflicts.Add(item.RecordKey);
                    continue;
                }
                _records[item.RecordKey] = new RemoteRecord(item.RecordKey, ++_lastSeq, item.Blob);
                accepted.Add(new PushAccepted(item.RecordKey, _lastSeq));
            }

            if (_loseNextAck)
            {
                _loseNextAck = false;
                throw new IOException("Conexão perdida antes da resposta.");
            }
            return Task.FromResult(new PushResult(accepted, conflicts));
        }
    }

    private void ThrowIfFailing()
    {
        if (_nextFailure is not { } failure)
            return;
        _nextFailure = null;
        throw failure;
    }
}
