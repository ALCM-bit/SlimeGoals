using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>Cronômetro rodando, com o que é preciso para mostrá-lo fora do app.</summary>
public sealed record RunningTimer(Guid GoalId, string Title, string Icon, GoalColor Color, DateTimeOffset StartedAt, string? Task = null)
{
    /// <summary>O que a janelinha e a notificação mostram: a meta, e a tarefa depois de um ponto médio.</summary>
    public string Label => Task is null ? Title : $"{Title} · {Task}";
}

/// <summary>
/// Acompanha o cronômetro rodando para a janelinha do Linux e a notificação do Android. "Rodando" é um
/// <see cref="ActiveTimer"/> sem parada cuja meta existe e não está arquivada; qualquer outro caso é nulo.
/// Relê a cada aviso do <see cref="DataChangeNotifier"/>.
/// </summary>
public sealed class RunningTimerWatcher(TimerService timers, IGoalRepository goals, DataChangeNotifier changes) : IDisposable
{
    private readonly Lock _lock = new();
    private RunningTimer? _current;
    private long _started;
    private long _applied;
    private int _subscribed;

    /// <summary>O cronômetro rodando, ou nulo. Seguro de ler de qualquer thread.</summary>
    public RunningTimer? Current => Volatile.Read(ref _current);

    /// <summary>Disparado só quando <see cref="Current"/> muda; pode ocorrer em qualquer thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Assina os avisos de mudança (uma vez só) e faz a primeira leitura.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
            changes.Changed += OnDataChanged;
        return RefreshAsync(ct);
    }

    /// <summary>
    /// Relê o cronômetro. Nunca lança: a falha vai ao Trace e mantém o valor anterior. Entre leituras
    /// simultâneas, a que começou por último vence, mesmo se terminar antes.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var version = Interlocked.Increment(ref _started);
        RunningTimer? value;
        try
        {
            value = await ReadAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao ler o cronômetro rodando: {TraceText.Of(e)}");
            return;
        }

        lock (_lock)
        {
            if (version < _applied)
                return;
            _applied = version;
            if (value == _current)
                return;
            Volatile.Write(ref _current, value);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _subscribed, 2) == 1)
            changes.Changed -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e) => _ = RefreshAsync();

    private async Task<RunningTimer?> ReadAsync(CancellationToken ct)
    {
        if (await timers.GetAsync(ct) is not { StoppedAt: null } timer)
            return null;
        if (await goals.GetAsync(timer.GoalId, ct) is not { ArchivedAt: null } goal)
            return null;
        return new RunningTimer(goal.Id, goal.Title, goal.Icon, goal.Color, timer.StartedAt, timer.Task);
    }
}
