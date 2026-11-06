using Dapper;
using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Sync;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Testing;

namespace SlimeGoals.Infrastructure.Tests.Sync;

/// <summary>Meta como a tabela guarda. <see cref="Row"/> traz todas as colunas, para a comparação não depender de uma lista.</summary>
internal sealed record SnapshotGoal(Guid Id, string Title, string UpdatedAt, string Row);

/// <summary>Registro como a tabela guarda, com <c>superseded</c>. <see cref="Row"/> traz todas as colunas.</summary>
internal sealed record SnapshotCheckin(Guid Id, Guid GoalId, int Amount, bool Superseded, string DoneAt, string? Period, string? Seen, string Row);

/// <summary>O que tem de ser igual em todos os aparelhos. O que é só local (lembretes, cronômetro, tabelas da sincronização) fica de fora.</summary>
internal sealed record DeviceSnapshot(IReadOnlyList<SnapshotGoal> Goals, IReadOnlyList<SnapshotCheckin> Checkins)
{
    /// <summary>Uma linha por registro: a diferença entre dois aparelhos aparece legível na falha.</summary>
    public IReadOnlyList<string> Lines => [.. Goals.Select(g => g.Row), .. Checkins.Select(c => c.Row)];
}

/// <summary>Um aparelho completo: banco e relógio próprios, serviços reais e o motor ligado ao servidor da conta.</summary>
internal sealed class SimulatedDevice : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly DeviceTransport _transport;
    private readonly SyncEngine _engine;
    private readonly SyncStateRepository _state;
    private readonly GoalService _goals;
    private readonly CheckinService _checkins;

    public SimulatedDevice(string name, Guid id, FakeSyncServer server, DateTimeOffset now)
    {
        Name = name;
        Id = id;
        Time = new FakeTimeProvider(now);
        _transport = new DeviceTransport(server);
        _state = new SyncStateRepository(_db.Factory);

        var goals = new GoalRepository(_db.Factory);
        var checkins = new CheckinRepository(_db.Factory);
        var timers = new TimerRepository(_db.Factory);
        var changes = new DataChangeNotifier();
        var reminders = new ReminderService(goals, checkins, timers, new ReminderLogRepository(_db.Factory), Notifications,
            new PlainFormatter(), Time);
        _goals = new GoalService(goals, checkins, timers, Time, changes, reminders);
        _checkins = new CheckinService(goals, checkins, Time, changes);
        _engine = new SyncEngine(_state, new SyncLocalStore(_db.Factory), _transport, new PlainSyncCrypto(), reminders, changes, Time);
        // EnableAsync mantém o id que já existe: assim cada aparelho tem o seu, escolhido pelo teste.
        _db.Anchor.Execute("UPDATE sync_state SET device_id = @Id", new { Id = id.ToString("D") });
    }

    public string Name { get; }
    public Guid Id { get; }
    public FakeTimeProvider Time { get; }
    public FakeNotificationService Notifications { get; } = new();

    /// <summary>Tamanho da página pedida ao servidor. Menor que o padrão, faz registros chegarem antes da meta.</summary>
    public int? PageSize
    {
        get => _transport.PageSize;
        set => _transport.PageSize = value;
    }

    /// <summary>Roda uma vez, com o envio já lido da fila e ainda sem resposta do servidor.</summary>
    public Func<Task>? DuringNextPush
    {
        get => _transport.DuringNextPush;
        set => _transport.DuringNextPush = value;
    }

    public int Pending => _db.Anchor.ExecuteScalar<int>("SELECT COUNT(*) FROM sync_outbox");

    public long LastSeq => _db.Anchor.ExecuteScalar<long>("SELECT last_seq FROM sync_state");

    public bool FullResync => _db.Anchor.ExecuteScalar<long>("SELECT full_resync FROM sync_state") == 1;

    public string? LastError => _db.Anchor.ExecuteScalar<string?>("SELECT last_error FROM sync_state");

    /// <summary>A fila em texto, para a mensagem de quem não terminou.</summary>
    public string Queue => string.Join(", ", _db.Anchor.Query<string>(
        "SELECT kind || ' ' || op || ' ' || record_id || ' base ' || base_seq FROM sync_outbox ORDER BY position"));

    public void Dispose() => _db.Dispose();

    public Task EnableAsync() => _state.EnableAsync();

    public Task DisableAsync() => _state.DisableAsync();

    public Task<Goal> CreateGoal(string title, Measure measure = Measure.Quantity, int target = 10) =>
        _goals.CreateAsync(Draft(title, measure, target));

    public async Task<Goal> EditGoal(Guid id, string title)
    {
        var goal = await _goals.GetAsync(id) ?? throw new GoalNotFoundException(id);
        return await _goals.UpdateAsync(id, Draft(title, goal.Measure, goal.Target));
    }

    public Task<int> Adjust(Guid goalId, int amount) => _checkins.AdjustAsync(goalId, amount);

    public Task DeleteGoal(Guid id) => _goals.DeleteAsync(id);

    public Task<SyncCycleResult> SyncAsync() => _engine.RunCycleAsync();

    /// <summary>Lembrete já mostrado e sem resposta, como o log guarda. Devolve o id da linha.</summary>
    public async Task<Guid> AddPendingReminder(Guid goalId)
    {
        var entry = new ReminderLogEntry(Guid.NewGuid(), goalId, Time.GetUtcNow());
        await new ReminderLogRepository(_db.Factory).AddAsync(entry);
        return entry.Id;
    }

    /// <summary>Total do período como a tela mostra.</summary>
    public Task<int> ShownTotal(Guid goalId) => _checkins.PeriodTotalAsync(goalId);

    /// <summary>Soma real dos registros que contam, sem o limite da tela.</summary>
    public int RealTotal(Guid goalId) => Snapshot().Checkins.Where(c => c.GoalId == goalId && !c.Superseded).Sum(c => c.Amount);

    public DeviceSnapshot Snapshot()
    {
        var goals = Rows("SELECT * FROM goals ORDER BY id").Select(r => new SnapshotGoal(
            Guid.ParseExact((string)r["id"]!, "D"), (string)r["title"]!, (string)r["updated_at"]!, Describe(r))).ToList();
        var checkins = Rows("SELECT * FROM checkins ORDER BY id").Select(r => new SnapshotCheckin(
            Guid.ParseExact((string)r["id"]!, "D"), Guid.ParseExact((string)r["goal_id"]!, "D"), (int)(long)r["amount"]!,
            (long)r["superseded"]! == 1, (string)r["done_at"]!, (string?)r["compensation_period"], (string?)r["compensation_seen"],
            Describe(r))).ToList();
        return new DeviceSnapshot(goals, checkins);
    }

    private IEnumerable<IDictionary<string, object?>> Rows(string sql) =>
        _db.Anchor.Query(sql).Cast<IDictionary<string, object?>>();

    private static string Describe(IDictionary<string, object?> row) =>
        string.Join(" | ", row.Select(c => $"{c.Key}={c.Value ?? "null"}"));

    private static GoalDraft Draft(string title, Measure measure, int target) => new(title, Recurrence.Weekly, target, "book",
        GoalColor.Purple, 60, new TimeOnly(8, 0), new TimeOnly(22, 0), WeekDays.All, null, null, measure,
        measure == Measure.Quantity ? "vezes" : null);

    private sealed class PlainFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new(goal.Title, "", "Feito!");
    }

    /// <summary>Passa tudo ao servidor da conta, com o tamanho de página e a ação durante o envio pedidos pelo teste.</summary>
    private sealed class DeviceTransport(ISyncTransport server) : ISyncTransport
    {
        public int? PageSize { get; set; }
        public Func<Task>? DuringNextPush { get; set; }

        public Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default) =>
            server.PullAsync(after, PageSize ?? limit, full, ct);

        public async Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default)
        {
            if (DuringNextPush is { } action)
            {
                DuringNextPush = null;
                await action();
            }
            return await server.PushAsync(items, ct);
        }
    }
}
