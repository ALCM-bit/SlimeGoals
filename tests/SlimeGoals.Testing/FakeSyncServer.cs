using SlimeGoals.Core.Sync;

namespace SlimeGoals.Testing;

/// <summary>
/// Servidor de sincronização em memória, de uma conta, com as regras 1 a 5 da §5 da spec. Aparelhos da
/// mesma conta usam a mesma instância.
/// </summary>
public sealed class FakeSyncServer : ISyncTransport
{
    /// <summary>Um pedido de <c>pull</c> como chegou ao servidor.</summary>
    public sealed record PullRequest(long After, bool Full);

    private readonly object _lock = new();
    private readonly Dictionary<string, RemoteRecord> _records = [];
    private readonly List<PullRequest> _pullRequests = [];
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

    /// <summary>Todos os pedidos de <c>pull</c> recebidos, em ordem, mesmo os que falharam.</summary>
    public IReadOnlyList<PullRequest> PullRequests
    {
        get
        {
            lock (_lock)
                return _pullRequests.ToList();
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

    /// <summary>
    /// Limpa as marcas de exclusão anteriores a <paramref name="seq"/>, sem passar do maior <c>seq</c> da conta: o registro
    /// mais novo sempre fica, e quem lê tudo alcança a limpeza (regra 5). Quem parou antes dela precisa refazer tudo.
    /// </summary>
    public void PurgeBefore(long seq)
    {
        lock (_lock)
        {
            _purgedBefore = Math.Min(seq, _lastSeq);
            foreach (var record in _records.Values.Where(r => r.Blob is null && r.Seq < _purgedBefore).ToList())
                _records.Remove(record.RecordKey);
        }
    }

    public Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
        {
            _pullRequests.Add(new PullRequest(after, full));
            ThrowIfFailing();
            // Registros vivos ficam abaixo da limpeza: quem lê tudo passa por ela sem 410, senão nunca terminaria.
            if (!full && after < _purgedBefore)
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
