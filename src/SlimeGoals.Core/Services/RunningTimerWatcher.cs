using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Cronômetro rodando, com o que é preciso para mostrá-lo fora do app. Com <see cref="Focus"/>, é a sessão de Pomodoro:
/// <see cref="StartedAt"/> é o início do bloco, <see cref="GoalId"/> é <see cref="Guid.Empty"/> sem meta e
/// <see cref="Task"/> só vem num bloco de foco.
/// </summary>
public sealed record RunningTimer(Guid GoalId, string Title, string Icon, GoalColor Color, DateTimeOffset StartedAt,
    string? Task = null, FocusDisplay? Focus = null)
{
    /// <summary>
    /// O que a janelinha e a notificação mostram: a meta, e a tarefa depois de um ponto médio. Na sessão, a fase, a meta
    /// (se há) e a tarefa (se há), por exemplo "Foco · Estudar · Revisar capítulo 3".
    /// </summary>
    public string Label => Focus is null
        ? Task is null ? Title : $"{Title} · {Task}"
        : string.Join(" · ", new[] { Focus.Phase, GoalId == Guid.Empty ? null : Title, Task }.OfType<string>());

    /// <summary>
    /// O que a notificação mostra na tela de bloqueio que esconde o conteúdo: <see cref="LockScreenText.Timer"/>, ou só a
    /// fase na sessão de Pomodoro. Nem meta nem tarefa.
    /// </summary>
    public string PublicLabel => Focus?.Phase ?? LockScreenText.Timer;
}

/// <summary>
/// O bloco da sessão de Pomodoro fora do app: a fase ("Foco", "Pausa", "Pausado", "Foco concluído", "Pausa concluída"),
/// o fim do bloco para a contagem regressiva, o tempo que falta congelado quando pausado, e o início da sessão.
/// </summary>
public sealed record FocusDisplay(string Phase, DateTimeOffset EndsAt, TimeSpan? PausedRemaining, DateTimeOffset SessionStartedAt);

/// <summary>
/// Acompanha o cronômetro rodando para a janelinha do Linux e a notificação do Android. "Rodando" é um
/// <see cref="ActiveTimer"/> sem parada cuja meta existe e não está arquivada; sem ele, com <paramref name="focus"/>, é a
/// sessão de Pomodoro que ainda não terminou. Qualquer outro caso é nulo. Relê a cada aviso do
/// <see cref="DataChangeNotifier"/>.
/// </summary>
public sealed class RunningTimerWatcher(TimerService timers, IGoalRepository goals, DataChangeNotifier changes,
    FocusService? focus = null) : IDisposable
{
    /// <summary>Como a sessão sem meta aparece.</summary>
    public const string SessionTitle = "Pomodoro";
    public const string SessionIcon = "NavTime";
    public const GoalColor SessionColor = GoalColor.Purple;

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
        if (await ReadTimerAsync(ct) is { } running)
            return running;
        return focus is null ? null : await ReadSessionAsync(focus, ct);
    }

    private async Task<RunningTimer?> ReadTimerAsync(CancellationToken ct)
    {
        if (await timers.GetAsync(ct) is not { StoppedAt: null } timer)
            return null;
        if (await goals.GetAsync(timer.GoalId, ct) is not { ArchivedAt: null } goal)
            return null;
        return new RunningTimer(goal.Id, goal.Title, goal.Icon, goal.Color, timer.StartedAt, timer.Task);
    }

    private async Task<RunningTimer?> ReadSessionAsync(FocusService service, CancellationToken ct)
    {
        if (await service.GetAsync(ct) is not { FinishedAt: null } session)
            return null;
        // Meta que sumiu ou foi arquivada: a sessão segue como sem meta (spec Pomodoro §4.4).
        var goal = session.GoalId is { } id && await goals.GetAsync(id, ct) is { ArchivedAt: null } found ? found : null;
        var isFocus = session.Block.Kind == FocusBlockKind.Focus;
        var display = new FocusDisplay(PhaseOf(session), FocusClock.EndsAt(session),
            session.PausedAt is { } pausedAt ? FocusClock.Remaining(session, pausedAt) : null, session.StartedAt);
        return goal is null
            ? new RunningTimer(Guid.Empty, SessionTitle, SessionIcon, SessionColor, session.BlockStartedAt,
                isFocus ? session.Task : null, display)
            : new RunningTimer(goal.Id, goal.Title, goal.Icon, goal.Color, session.BlockStartedAt,
                isFocus ? session.Task : null, display);
    }

    /// <summary>
    /// A fase do bloco. O bloco espera o toque desde que o aviso do fim dele foi dado: o <see cref="FocusAlerts"/> marca o
    /// passo e avisa a mudança, e assim a janelinha troca de fase sem relógio próprio.
    /// </summary>
    private static string PhaseOf(FocusSession session)
    {
        var isFocus = session.Block.Kind == FocusBlockKind.Focus;
        if (session.PausedAt is not null)
            return "Pausado";
        if (session.AlertedStep == session.Step)
            return isFocus ? "Foco concluído" : "Pausa concluída";
        return isFocus ? "Foco" : "Pausa";
    }
}
