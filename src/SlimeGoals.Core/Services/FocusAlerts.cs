using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>
/// O aviso de fim de bloco do Pomodoro (spec Pomodoro §4.3 e §4.5). Mantém o <see cref="IFocusAlarm"/> armado para o fim
/// do bloco enquanto a sessão roda, e no disparo mostra o <see cref="IFocusAlert"/> uma vez só por passo: a marca
/// <see cref="FocusSession.AlertedStep"/> fica gravada, então nem um processo novo avisa de novo. Retira o aviso quando o
/// passo dele deixa de ser o atual, quando a sessão termina e quando ela some. Nunca lança: as falhas vão ao Trace.
/// </summary>
public sealed class FocusAlerts(FocusService focus, IFocusSessionRepository sessions, IFocusAlarm alarm, IFocusAlert alert,
    TimeProvider time, DataChangeNotifier changes) : IDisposable
{
    /// <summary>Quanto antes do fim um disparo ainda vale: o alarme do sistema pode acordar um pouco cedo.</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(1);

    // Disparo e sincronização chegam de threads diferentes (alarme e avisos de mudança): um por vez.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _subscribed;

    // O passo (com o início da sessão) cujo aviso pode estar à mostra. Até a primeira leitura não se sabe: um processo
    // anterior pode ter deixado um aviso na central.
    private (DateTimeOffset Session, int Step)? _shown;
    private bool _known;

    /// <summary>Assina os avisos de mudança (uma vez só) e sincroniza.</summary>
    public Task StartAsync()
    {
        if (Interlocked.CompareExchange(ref _subscribed, 1, 0) == 0)
            changes.Changed += OnDataChanged;
        return SyncAsync();
    }

    /// <summary>
    /// Arma o alarme para o fim do bloco quando a sessão roda, sem pausa e sem aviso dado neste passo; nos outros casos,
    /// cancela. Retira o aviso que ficou de um passo anterior ou de uma sessão que acabou.
    /// </summary>
    public async Task SyncAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Apply(await focus.GetAsync());
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao sincronizar o aviso do Pomodoro: {TraceText.Of(e)}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// O alarme tocou: se o bloco venceu (com 1 s de tolerância) e o passo ainda não foi avisado, marca o passo, mostra o
    /// aviso e sincroniza. Um disparo cedo ou repetido só rearma o alarme.
    /// </summary>
    public async Task FireAsync()
    {
        var fired = false;
        await _gate.WaitAsync();
        try
        {
            var session = await focus.GetAsync();
            if (session is { FinishedAt: null, PausedAt: null } && session.AlertedStep != session.Step
                && time.GetUtcNow() >= FocusClock.EndsAt(session) - Tolerance
                && await sessions.MarkAlertedAsync(session.Step))
            {
                fired = true;
                _shown = (session.StartedAt, session.Step);
                Try(() => alert.Show(KindOf(session)), "mostrar o aviso do Pomodoro");
                session = await focus.GetAsync();
            }
            Apply(session);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao disparar o aviso do Pomodoro: {TraceText.Of(e)}");
        }
        finally
        {
            _gate.Release();
        }
        // A marca não muda a revisão nem avisa ninguém: a janelinha e a notificação releem para mostrar o fim do bloco.
        // Fora da trava, para a sincronização que o aviso causa aqui mesmo não ficar esperando.
        if (fired)
            changes.NotifyChanged();
    }

    /// <summary>Para o som do aviso, que fica na central.</summary>
    public void Silence() => Try(alert.Silence, "silenciar o aviso do Pomodoro");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _subscribed, 2) == 1)
            changes.Changed -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e) => _ = SyncAsync();

    private void Apply(FocusSession? session)
    {
        var open = session is { FinishedAt: null } ? session : null;
        if (!_known)
        {
            _known = true;
            if (open is null)
                Withdraw();
            else if (open.AlertedStep is { } alerted)
                _shown = (open.StartedAt, alerted);
        }
        if (_shown is { } shown && (open is null || shown != (open.StartedAt, open.Step)))
            Withdraw();

        if (open is { PausedAt: null } && open.AlertedStep != open.Step)
            Try(() => alarm.Set(FocusClock.EndsAt(open)), "armar o alarme do Pomodoro");
        else
            Try(alarm.Cancel, "cancelar o alarme do Pomodoro");
    }

    private void Withdraw()
    {
        _shown = null;
        Try(alert.Withdraw, "retirar o aviso do Pomodoro");
    }

    /// <summary>O aviso é decidido pelo bloco seguinte: pausa, foco ou o fim da sessão.</summary>
    private static FocusAlertKind KindOf(FocusSession session) =>
        session.IsLastStep ? FocusAlertKind.SessionDone
        : session.Plan.BlockAt(session.Step + 1).Kind == FocusBlockKind.Break ? FocusAlertKind.BreakTime
        : FocusAlertKind.FocusTime;

    private static void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao {what}: {TraceText.Of(e)}");
        }
    }
}
