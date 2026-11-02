# Fase 4 (4a–4c) — Lembretes no Linux — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada meta gera lembretes periódicos como notificação nativa do GNOME, dentro do horário e dos dias ativos, até ser cumprida no período. As ações **ação principal** (Feito / +1 / Iniciar), **Adiar** e **Ignorar** funcionam direto da notificação, e a tela Hoje mostra os próximos lembretes do dia.

**Architecture:** O Core ganha um planejador puro (`ReminderPlanner`: o próximo disparo de uma meta a partir de um estado já levantado) e um orquestrador (`ReminderService.SyncAsync`). O orquestrador levanta esse estado, retira as notificações vencidas, dispara as devidas e devolve o próximo instante. As ações chegam ao `ReminderResponseHandler`, que só usa serviços e repositórios. No Linux, um agendador com um único timer do processo (`TimeProvider`) chama `SyncAsync` e se rearma no mais cedo entre o próximo lembrete e a meia-noite local. O adaptador D-Bus (`Tmds.DBus.Protocol`, como no spike) envia as notificações e traduz os sinais. Um `DataChangeNotifier` do Core avisa o agendador e a tela Hoje sempre que metas, registros ou o cronômetro mudam.

**Tech Stack:** as mesmas versões da Fase 3, mais um pacote novo: `Tmds.DBus.Protocol` **0.95.2**, a versão validada no spike `specs/spikes/gnome-notificacoes.md`, só no `SlimeGoals.Platform.Linux`.

**Spec:** `specs/Planejamento_v3.md` v1.4: **§9 (Lembretes)**, §5 (portas e componentes obrigatórios), §5.1 (quando os lembretes param), §7 (Hoje: próximos lembretes do dia), §8 (`reminder_log`), §12 (Fases 4a–4c), §14; `specs/spikes/gnome-notificacoes.md` (decisões 1–3).

**Fora deste plano:** a **4d** (fechar = ocultar, instância única, bandeja, pausar lembretes), que terá um plano próprio; a duração do "adiar" configurável e a suspensão do computador (Fase 6); o fallback em janela própria (ver Decisão 5).

## Decisões deste plano (o autor confirma na revisão)

1. **Adiar = 10 min fixos** (`ReminderService.SnoozeMinutes`). A spec não fixa o padrão; a configuração vem na Fase 6.
2. **Lembrete atrasado dispara na hora.** Se o próximo disparo calculado já passou (ex.: app aberto às 14:00 numa meta com janela desde 08:00), ele dispara uma vez, agora, e o intervalo volta a contar a partir desse disparo.
3. **Notificação sem resposta vira "ignorada"** (spec §9.6) e é retirada da central quando: dispara o próximo lembrete da mesma meta; a meta deixa de precisar de lembrete (cumprida, arquivada, cronômetro iniciado, fora da janela); o app encerra; ou o app abre e encontra registros sem resposta de uma execução anterior.
4. **Cronômetro parado aguardando registro também suspende os lembretes** da meta, como o cronômetro rodando (spec §9.11): o usuário está no meio da ação.
5. **Sem fallback em janela nesta fase.** O GNOME foi validado com ações (spike). Se o D-Bus falhar, o app segue funcionando sem notificações, registra o erro em `Trace` e não entra em laço de novas tentativas. O fallback fica para a Fase 6, se for preciso.
6. **Fechar a janela continua encerrando o app** até a 4d. Por isso, na verificação manual, a janela fica aberta, minimizada ou em segundo plano. O critério "com a janela principal fechada" da 4c passa para a 4d.
7. **Virada do dia:** o agendador também acorda à meia-noite local e avisa o `DataChangeNotifier`, e a tela Hoje recarrega. Isso resolve a decisão adiada da Fase 3 ("Hoje não recarrega na virada do dia").
8. **Desvio de assinatura da spec §5:** `IReminderScheduler`/`INotificationService` ficam assíncronos (`Task`); `Pause`/`Resume` só entram na 4d, junto com a bandeja.

## Global Constraints

- Tudo das Fases 1–3 continua valendo: `net10.0`, `Nullable`, `TreatWarningsAsErrors`, versões centralizadas em `Directory.Packages.props`, Core sem dependências (o `ArchitectureTests` já proíbe `Tmds`), relógio via `TimeProvider`, textos em pt-BR, identificadores em inglês e ViewModels sem tipos visuais do Avalonia.
- "Hoje" no Core = `DateOnly.FromDateTime(time.GetLocalNow().DateTime)`; fuso = `time.LocalTimeZone`; instantes gravados em UTC, texto ISO 8601 `"O"` (`SqlText.Instant`).
- `reminder_log.response` grava `'done' | 'snooze' | 'ignore'` (spec §8); a tabela e o índice já existem desde a `001`, então **não há migração nesta fase**.
- Rótulos exatos das ações (spec §9.11): ação principal `"Feito"` (conclusão), `"+1"` (quantidade), `"Iniciar"` (tempo); `"Adiar"`; `"Ignorar"`. O clique no corpo da notificação usa a chave D-Bus `default`, com o rótulo `"Abrir"`.
- Chaves de ação D-Bus: `primary`, `snooze`, `ignore`, `default`.
- Toda exceção vinda de um evento (D-Bus, timer) é capturada e registrada em `Trace`: um sinal ou um tique nunca derruba o app.
- Branch `feat/fase-4-lembretes`; Conventional Commits (`core`, `infra`, `linux`, `app`, `desktop`, `specs`); commits terminam com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; integração por PR.
- Comando de testes: `dotnet test --solution SlimeGoals.slnx`.

## Review Focus

1. **Horário de verão na janela do dia** (ex.: `America/New_York`, 08/03/2026, `active_from` 02:30 não existe): o disparo vai para o primeiro instante válido (03:00 local), sem exceção nem salto de um dia. Coberto na Task 1.
2. **Vários lembretes atrasados ao abrir o app** (cinco metas com janela desde 08:00, app aberto às 14:00): cada meta dispara uma vez, agora, e a próxima de cada uma fica para `agora + intervalo`, não para agora de novo. Coberto nas Tasks 1 e 2.
3. **Clique numa notificação antiga** (ação depois de a meta ter sido cumprida pela tela, ou de o registro já ter resposta): nada é registrado duas vezes; o clique é ignorado sem erro. Coberto na Task 3.
4. **Notificação que falha** (D-Bus fora do ar ou `Notify` lança exceção): o disparo fica no `reminder_log`, o planejador avança para o próximo intervalo e o agendador não fica disparando sem parar. Coberto nas Tasks 2 e 5.
5. **"Iniciar" com outro cronômetro rodando**: não para o cronômetro atual; o app vem para a frente, para o usuário decidir (spec §9.11). Coberto nas Tasks 3 e 7.

---

## Mapa de arquivos

```
src/SlimeGoals.Core/
  Domain/LocalTime.cs                      (novo: data + hora local → UTC, com horário de verão)
  Domain/DateRange.cs                      (alterado: LocalMidnightToUtc delega para LocalTime)
  Domain/ReminderLogEntry.cs               (novo: ReminderLogEntry, ReminderResponse)
  Domain/Reminder.cs                       (novo: Reminder, ReminderText, ReminderAction, ReminderOutcome)
  Abstractions/IReminderLogRepository.cs, Abstractions/INotificationService.cs,
  Abstractions/IReminderFormatter.cs, Abstractions/IReminderScheduler.cs (novos)
  Services/ReminderPlanner.cs              (novo, puro)
  Services/ReminderService.cs              (novo)
  Services/ReminderResponseHandler.cs      (novo)
  Services/DataChangeNotifier.cs           (novo)
  Services/GoalService.cs, CheckinService.cs, TimerService.cs (alterados: notificam mudanças)
src/SlimeGoals.Infrastructure/
  Data/ReminderLogRepository.cs            (novo)
  InfrastructureServiceCollectionExtensions.cs (alterado)
src/SlimeGoals.Platform.Linux/
  Scheduling/ProcessTimerReminderScheduler.cs (novo)
  Notifications/INotificationBus.cs, Notifications/TmdsNotificationBus.cs,
  Notifications/DbusNotificationService.cs  (novos)
  SlimeGoals.Platform.Linux.csproj         (alterado: Tmds.DBus.Protocol)
src/SlimeGoals.App/
  Goals/ReminderFormatter.cs               (novo: IReminderFormatter com GoalFormatting)
  Hosting/IAppHostedService.cs, IWindowActivator.cs, MainWindowActivator.cs, ReminderHost.cs (novos)
  App.axaml.cs                             (alterado: inicia/para serviços hospedados, ativa a janela)
  ViewModels/TodayPageViewModel.cs, Views/TodayView.axaml (alterados: recarga por mudança, próximos lembretes)
src/SlimeGoals.Desktop/
  CompositionRoot.cs                       (alterado)
tests/SlimeGoals.Testing/                  (InMemoryReminderLogRepository, FakeNotificationService)
tests/…                                    (testes novos por task)
Directory.Packages.props                   (Tmds.DBus.Protocol 0.95.2)
specs/Planejamento_v3.md                   (§9: decisões 1–4 e 7; §15: decisões da Fase 4)
```

---

## Task 1: Planejador de lembretes (4a)

**Files:**
- Create: `src/SlimeGoals.Core/Domain/LocalTime.cs`, `src/SlimeGoals.Core/Domain/ReminderLogEntry.cs`, `src/SlimeGoals.Core/Services/ReminderPlanner.cs`
- Modify: `src/SlimeGoals.Core/Domain/DateRange.cs` (`LocalMidnightToUtc` passa a chamar `LocalTime.ToUtc(date, TimeOnly.MinValue, zone)`; comportamento igual)
- Test: `tests/SlimeGoals.Core.Tests/Domain/LocalTimeTests.cs`, `tests/SlimeGoals.Core.Tests/Services/ReminderPlannerTests.cs`

**Interfaces:**

```csharp
public static class LocalTime
{
    /// Primeiro instante UTC cuja hora local é ≥ date + time. Hora inexistente (horário de verão)
    /// → primeiro instante válido depois; hora repetida → primeira ocorrência. Mesma busca pelo lado UTC de DateRange.
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone);
}

public enum ReminderResponse { Done, Snooze, Ignore }

public sealed record ReminderLogEntry(Guid Id, Guid GoalId, DateTimeOffset FiredAt,
    ReminderResponse? Response = null, DateTimeOffset? RespondedAt = null);

/// Fatos de hoje (dia local) já levantados pelo ReminderService.
public sealed record ReminderState(
    bool PeriodMet,                  // soma do período de avaliação atual ≥ alvo
    bool HasTimer,                   // há active_timer desta meta (rodando ou parado)
    DateTimeOffset? LastFiredAt,     // último disparo de hoje
    DateTimeOffset? LastCheckinAt,   // último registro de hoje
    DateTimeOffset? SnoozedAt);      // RespondedAt do último disparo de hoje, se a resposta foi Snooze

public static class ReminderPlanner
{
    /// Próximo disparo (UTC) ou null se a meta não terá lembrete nos próximos 14 dias.
    public static DateTimeOffset? Next(Goal goal, ReminderState state, int snoozeMinutes,
        DateTimeOffset nowUtc, TimeZoneInfo zone);
}
```

Regras (spec §9.1–9.2 e §5.1; decisões 2 e 4):
- `null` se a meta está arquivada/excluída ou `HasTimer`.
- **Hoje** só vale se `!PeriodMet` e `GoalPeriod.RemindsOn(goal, hoje)`. Com janela `[início, fim]` = `LocalTime.ToUtc(hoje, ActiveFrom/ActiveUntil)`:
  - se `SnoozedAt` existe e é posterior a `LastCheckinAt` (ou não há registro), o candidato é `SnoozedAt + snoozeMinutes`;
  - senão, a base é o mais recente entre `LastFiredAt` e `LastCheckinAt` que seja ≥ início da janela; o candidato é `base + ReminderEveryMinutes`, ou o início da janela se não houver base;
  - candidato = `max(candidato, nowUtc)` (decisão 2); vale se ≤ fim da janela (inclusivo).
- Senão, o primeiro dia `d` em `hoje+1 … hoje+14` com `RemindsOn(goal, d)`, exigindo, se `PeriodMet` e a meta é semanal, `WeekMath.StartOfWeek(d) > WeekMath.StartOfWeek(hoje)`. O resultado é `LocalTime.ToUtc(d, ActiveFrom, zone)`.

- [ ] **Step 1: Testes que falham.** Use o fuso `America/Sao_Paulo`, a meta diária com janela 08:00–20:00 e intervalo 60 min e o relógio em quarta, 04/11/2026. Testes:
  - `First_reminder_is_window_start_when_nothing_happened_today` (agora 07:00 → 08:00);
  - `Next_is_last_fire_plus_interval` (disparo às 09:00 → 10:00);
  - `Checkin_after_fire_restarts_interval` (disparo 09:00, registro 09:40 → 10:40);
  - `Overdue_reminder_fires_now` (agora 14:00, nada hoje → 14:00);
  - `After_window_end_goes_to_next_active_day` (disparo 19:30 → quinta 08:00; dias ativos sem quinta → sexta 08:00);
  - `Met_daily_goal_waits_for_next_day`;
  - `Met_weekly_goal_waits_for_next_week` (semanal cumprida na quarta → segunda 09/11 08:00);
  - `Snooze_replaces_interval` (adiou às 09:05 → 09:15) e `Checkin_after_snooze_wins` (adiou 09:05, registro 09:10 → 10:10);
  - `Timer_or_archived_goal_has_no_reminder`;
  - `End_date_stops_reminders` (fim hoje e janela já passada → null);
  - `Window_start_in_dst_gap_uses_first_valid_instant` (fuso `America/New_York`, domingo 08/03/2026, `ActiveFrom` 02:30 → 03:00 local = 07:00 UTC).

  No `LocalTimeTests`: hora comum, hora no buraco do horário de verão e hora repetida (`America/New_York`, 01/11/2026 01:30 → primeira ocorrência, 05:30 UTC). Os testes de `DateRangeUtcTests` existentes continuam passando.
- [ ] **Step 2:** Rodar `dotnet test --project tests/SlimeGoals.Core.Tests` e ver falhar (tipos inexistentes).
- [ ] **Step 3:** Implementar `LocalTime`, os records e o `ReminderPlanner`.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(core): planejador de lembretes com janela, intervalo, adiar e horário de verão`

---

## Task 2: Registro de disparos e orquestração (`ReminderService`)

**Files:**
- Create: `src/SlimeGoals.Core/Domain/Reminder.cs`, `src/SlimeGoals.Core/Abstractions/IReminderLogRepository.cs`, `src/SlimeGoals.Core/Abstractions/INotificationService.cs`, `src/SlimeGoals.Core/Abstractions/IReminderFormatter.cs`, `src/SlimeGoals.Core/Services/ReminderService.cs`, `tests/SlimeGoals.Testing/InMemoryReminderLogRepository.cs`, `tests/SlimeGoals.Testing/FakeNotificationService.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/ReminderServiceTests.cs`

**Interfaces:**

```csharp
public sealed record ReminderText(string Title, string Body, string PrimaryLabel);
public sealed record Reminder(Guid LogId, Guid GoalId, ReminderText Text);
public enum ReminderAction { Primary, Snooze, Ignore, Open }
public enum ReminderOutcome { Handled, OpenApp, Stale }

public interface IReminderFormatter { ReminderText Format(Goal goal, int periodTotal); }

public interface INotificationService
{
    Task ShowAsync(Reminder reminder, CancellationToken ct = default);
    /// Retira da central (no-op se o id não está aberto).
    Task WithdrawAsync(Guid logId, CancellationToken ct = default);
    event EventHandler<ReminderActionEventArgs>? ActionReceived;
}
public sealed class ReminderActionEventArgs(Guid logId, ReminderAction action) : EventArgs
{ public Guid LogId { get; } = logId; public ReminderAction Action { get; } = action; }

public interface IReminderLogRepository
{
    Task AddAsync(ReminderLogEntry entry, CancellationToken ct = default);
    Task<ReminderLogEntry?> GetAsync(Guid id, CancellationToken ct = default);
    /// Grava a resposta só se ainda não há resposta; devolve false se já havia.
    Task<bool> RespondAsync(Guid id, ReminderResponse response, DateTimeOffset respondedAt, CancellationToken ct = default);
    /// fired_at em [fromUtc, toUtc), ordenado por FiredAt.
    Task<IReadOnlyList<ReminderLogEntry>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
    /// Response nula, ordenado por FiredAt.
    Task<IReadOnlyList<ReminderLogEntry>> ListPendingAsync(CancellationToken ct = default);
}

public sealed record UpcomingReminder(Goal Goal, DateTimeOffset At);

public sealed class ReminderService(IGoalRepository goals, ICheckinRepository checkins, ITimerRepository timers,
    IReminderLogRepository log, INotificationService notifications, IReminderFormatter formatter, TimeProvider time)
{
    public const int SnoozeMinutes = 10;
    /// Retira pendentes que não valem mais, dispara os devidos (Next ≤ agora) e devolve o próximo disparo.
    public Task<DateTimeOffset?> SyncAsync(CancellationToken ct = default);
    /// Próximo disparo de cada meta que ainda cai hoje (dia local), ordenado por horário.
    public Task<IReadOnlyList<UpcomingReminder>> UpcomingTodayAsync(CancellationToken ct = default);
    /// Marca todos os pendentes como Ignore e os retira (ao abrir e ao encerrar o app).
    public Task ExpirePendingAsync(CancellationToken ct = default);
}
```

- Estado por meta (uma consulta de registros e uma de disparos, ambas desde a segunda local, como no `ProgressService`): `PeriodMet` = soma do `EvaluationPeriod(goal, hoje)` ≥ alvo; `HasTimer` = `timers.GetAsync()?.GoalId == goal.Id`; `LastFiredAt`, `LastCheckinAt` e `SnoozedAt` só de hoje.
- `SyncAsync`, nesta ordem:
  1. Cada pendente cuja meta tem `Next` nulo ou depois de hoje, ou já foi substituída por um disparo mais novo da mesma meta, vira `Ignore` e é retirado.
  2. Cada meta com `Next ≤ agora` tem o pendente anterior marcado como `Ignore` e retirado; depois ganha um `ReminderLogEntry` novo, gravado antes do `ShowAsync`, com `Id = Guid.CreateVersion7(agora)` e `FiredAt = agora`.
  3. Se `ShowAsync` lançar exceção: registrar em `Trace`, manter o disparo gravado e seguir para a próxima meta.
  4. Recalcular e devolver o menor `Next`.

- [ ] **Step 1: Testes que falham** (relógio, fuso e meta como na Task 1, com `FakeNotificationService` que guarda os `Reminder` mostrados e os ids retirados, e pode ser configurado para lançar no `ShowAsync`):
  - `Sync_fires_due_goal_logs_it_and_returns_next_interval` (agora 08:00 → 1 notificação, 1 entrada no log, retorno 09:00);
  - `Sync_twice_does_not_fire_twice`;
  - `Five_overdue_goals_fire_once_each` (agora 14:00 → 5 notificações; um segundo `SyncAsync` às 14:00 não mostra nenhuma);
  - `Next_fire_withdraws_previous_pending_as_ignore`;
  - `Met_goal_pending_is_withdrawn_as_ignore` (registro que cumpre a meta + `SyncAsync` → retirada; nenhuma notificação nova);
  - `Failing_notification_still_logs_and_advances` (Show lança → log tem a entrada; o retorno é 09:00, não agora);
  - `Upcoming_today_lists_only_today_sorted`;
  - `Expire_pending_marks_ignore_and_withdraws`;
  - `Formatter_text_is_used` (o `Reminder` mostrado traz o título, o corpo e o rótulo do formatter falso).
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar os tipos, os fakes (mesmo contrato das interfaces; `RespondAsync` só grava se a resposta for nula) e o serviço.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(core): registro de disparos e sincronização de lembretes`

---

## Task 3: Resposta às ações e aviso de mudança (`ReminderResponseHandler`, `DataChangeNotifier`)

**Files:**
- Create: `src/SlimeGoals.Core/Services/ReminderResponseHandler.cs`, `src/SlimeGoals.Core/Services/DataChangeNotifier.cs`
- Modify: `src/SlimeGoals.Core/Services/GoalService.cs`, `CheckinService.cs`, `TimerService.cs` (último parâmetro do construtor `DataChangeNotifier? changes = null`; chamam `changes?.NotifyChanged()` depois de cada escrita bem-sucedida)
- Test: `tests/SlimeGoals.Core.Tests/Services/ReminderResponseHandlerTests.cs`, `tests/SlimeGoals.Core.Tests/Services/DataChangeNotifierTests.cs`

**Interfaces:**

```csharp
public sealed class DataChangeNotifier
{
    public event EventHandler? Changed;      // pode disparar em qualquer thread
    public void NotifyChanged();
}

public sealed class ReminderResponseHandler(IReminderLogRepository log, IGoalRepository goals,
    CheckinService checkins, TimerService timers, TimeProvider time, DataChangeNotifier? changes = null)
{
    public Task<ReminderOutcome> HandleAsync(Guid logId, ReminderAction action, CancellationToken ct = default);
}
```

Regras:
- `Open` → `OpenApp`, sem tocar no log.
- Registro inexistente ou já respondido → `Stale` (nada é gravado).
- `Primary`:
  - conclusão ou quantidade → `checkins.AdjustAsync(goalId, 1)`;
  - tempo → `timers.StartAsync(goalId)`;
  - antes de `StartAsync`, se `timers.GetAsync()` devolve um cronômetro de **outra** meta → o log continua sem resposta e o retorno é `OpenApp` (o handler não tenta iniciar);
  - outra `GoalStateException` ou `GoalNotFoundException` (meta já concluída, arquivada ou inativa hoje) → responde `Ignore` e retorna `Handled`;
  - sucesso → responde `Done`.
- `Snooze` → responde `Snooze` com `RespondedAt = agora`. `Ignore` → responde `Ignore`.
- Toda resposta gravada termina em `changes?.NotifyChanged()`.

- [ ] **Step 1: Testes que falham:**
  - `Primary_on_completion_and_quantity_adds_1_and_logs_done`;
  - `Primary_on_duration_starts_timer_and_logs_done`;
  - `Primary_on_duration_with_other_timer_returns_open_app_and_keeps_pending` (o cronômetro da outra meta continua rodando);
  - `Primary_on_already_completed_goal_logs_ignore`;
  - `Second_click_is_stale_and_records_nothing` (dois `Primary` seguidos → 1 registro);
  - `Snooze_and_ignore_are_logged_with_time`;
  - `Open_returns_open_app_without_logging`;
  - `Handler_and_services_notify_changes` (um contador no `Changed` sobe após `AdjustAsync`, `StartAsync`, `CreateAsync` e após uma resposta).
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar. As construções existentes dos serviços nos testes continuam compilando, porque o parâmetro novo tem valor padrão.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(core): resposta às ações do lembrete e aviso de mudança de dados`

---

## Task 4: Repositório SQLite do `reminder_log`

**Files:**
- Create: `src/SlimeGoals.Infrastructure/Data/ReminderLogRepository.cs`
- Modify: `src/SlimeGoals.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (registra `IReminderLogRepository`)
- Test: `tests/SlimeGoals.Infrastructure.Tests/Data/ReminderLogRepositoryTests.cs` (mesmo `TestDatabase` dos outros repositórios)

**Interfaces:** consome `IReminderLogRepository` e `ReminderLogEntry` (Tasks 1–2). `response` ↔ `ReminderResponse`: `'done'`/`'snooze'`/`'ignore'`; valor desconhecido no banco → `FormatException`.

- [ ] **Step 1: Testes que falham:**
  - `Add_then_get_round_trips_utc_instants`;
  - `Respond_only_once` (a segunda chamada devolve `false` e não altera a primeira resposta);
  - `List_filters_by_fired_at_range_and_orders`;
  - `List_pending_returns_only_null_responses`;
  - `Response_text_matches_spec_values` (lê a coluna crua e confere `'snooze'`);
  - `Goal_foreign_key_is_enforced`.
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar no padrão do `TimerRepository` (Dapper, `SqlText.Id`/`Instant`/`ParseInstant`). `RespondAsync` usa `UPDATE … WHERE id = @Id AND response IS NULL` e devolve `linhas == 1`.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(infra): repositório do registro de lembretes`

---

## Task 5: Agendador Linux com timer único (4b)

**Files:**
- Create: `src/SlimeGoals.Core/Abstractions/IReminderScheduler.cs`, `src/SlimeGoals.Platform.Linux/Scheduling/ProcessTimerReminderScheduler.cs`
- Modify: `tests/SlimeGoals.Platform.Linux.Tests/SlimeGoals.Platform.Linux.Tests.csproj` (referências a `SlimeGoals.Testing` e `Microsoft.Extensions.TimeProvider.Testing`, se faltarem)
- Test: `tests/SlimeGoals.Platform.Linux.Tests/ProcessTimerReminderSchedulerTests.cs`

**Interfaces:**

```csharp
public interface IReminderScheduler : IDisposable
{
    Task StartAsync(CancellationToken ct = default);   // ExpirePending + Sync + arma o timer
    Task RescheduleAsync(CancellationToken ct = default);
}

public sealed class ProcessTimerReminderScheduler(ReminderService reminders, DataChangeNotifier changes, TimeProvider time)
    : IReminderScheduler
{
    /// Instante em que o timer está armado (para testes e diagnóstico); null antes do StartAsync.
    public DateTimeOffset? ArmedFor { get; }
}
```

Regras:
- `RescheduleAsync` roda serializado (`SemaphoreSlim(1)`): chama `SyncAsync` e arma **um** `ITimer` (`time.CreateTimer`, período infinito) para `min(próximo, próxima meia-noite local)`.
- Quando o timer dispara: se o instante armado for a meia-noite, chama `changes.NotifyChanged()`; em seguida, `RescheduleAsync`.
- `changes.Changed` → `RescheduleAsync`, disparado sem esperar o resultado (fire-and-forget), com as exceções capturadas.
- `Dispose` cancela o timer e se desinscreve do `Changed`. (Síncrono de propósito: o container do head é descartado com `using`, e um singleton só `IAsyncDisposable` lançaria exceção ao fechar o app.)
- Exceção no `SyncAsync` → `Trace`; o timer é rearmado para a próxima meia-noite, sem laço.

- [ ] **Step 1: Testes que falham** (`FakeTimeProvider` em São Paulo, `ReminderService` real com repositórios em memória e `FakeNotificationService`):
  - `Start_arms_for_next_reminder` (agora 07:00, janela 08:00 → `ArmedFor` = 08:00);
  - `Timer_fires_shows_notification_and_rearms` (`Advance` até 08:00 → 1 notificação, `ArmedFor` = 09:00);
  - `No_goals_arms_for_local_midnight_and_notifies_change_there`;
  - `Data_change_reschedules` (registro que cumpre a meta → `ArmedFor` passa para o dia seguinte);
  - `Failing_sync_rearms_for_midnight` (repositório que lança).
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(linux): agendador de lembretes com timer único e virada do dia`

---

## Task 6: Notificação nativa via D-Bus (4c)

**Files:**
- Create: `src/SlimeGoals.Platform.Linux/Notifications/INotificationBus.cs`, `Notifications/DbusNotificationService.cs`, `Notifications/TmdsNotificationBus.cs`
- Modify: `Directory.Packages.props` (`Tmds.DBus.Protocol` 0.95.2), `src/SlimeGoals.Platform.Linux/SlimeGoals.Platform.Linux.csproj`, `THIRD-PARTY-NOTICES.md` (Tmds.DBus.Protocol, MIT)
- Test: `tests/SlimeGoals.Platform.Linux.Tests/DbusNotificationServiceTests.cs`

**Interfaces:**

```csharp
/// Costura fina sobre org.freedesktop.Notifications; o resto da lógica é testável sem D-Bus.
public interface INotificationBus : IDisposable
{
    Task<uint> NotifyAsync(string summary, string body, IReadOnlyList<(string Key, string Label)> actions,
        CancellationToken ct = default);
    Task CloseAsync(uint id, CancellationToken ct = default);
    event Action<uint, string>? ActionInvoked;        // (id, chave)
    event Action<uint, uint>? NotificationClosed;     // (id, motivo)
}

public sealed class DbusNotificationService(INotificationBus bus) : INotificationService;
public sealed class TmdsNotificationBus : INotificationBus;   // conexão de sessão única, criada no 1º uso
```

Regras:
- `ShowAsync` envia as ações nesta ordem: `primary`/`PrimaryLabel`, `snooze`/`"Adiar"`, `ignore`/`"Ignorar"`, `default`/`"Abrir"`. Guarda o par id ↔ `LogId`.
- `ActionInvoked` → `ActionReceived` (`primary`→`Primary`, `snooze`→`Snooze`, `ignore`→`Ignore`, `default`→`Open`) e marca o id como respondido.
- `NotificationClosed`:
  - motivo 1 ou 2 **sem** ação antes → `ActionReceived(Ignore)` (spike, decisão 2);
  - motivo 3 (fechada pelo app) ou qualquer motivo depois de uma ação → nada;
  - em todos os casos, o id sai do mapa.
- `WithdrawAsync` → `CloseAsync(id)` e o id sai do mapa. Sem id → nada.
- Chave ou id desconhecidos → ignorados.
- `TmdsNotificationBus`: `Notify` com assinatura `susssasa{sv}i`, valores:
  - `app_name` = `"SlimeGoals"`, `replaces_id` = 0, `app_icon` = `""`, `expire_timeout` = −1;
  - dicas `desktop-entry` = `"slimegoals"` e `urgency` = byte 1.

  Os sinais `ActionInvoked` e `NotificationClosed` são ouvidos com `WatchSignalAsync`, como no spike (`git show spike/notificacao-gnome:spikes/gnome-notify/Program.cs`). As exceções dos callbacks vão para `Trace`.

- [ ] **Step 1: Testes que falham** (bus falso que grava as chamadas e expõe métodos para disparar os eventos):
  - `Show_sends_actions_in_order_with_labels`;
  - `Action_maps_to_reminder_action_with_log_id`;
  - `Closed_by_user_without_action_is_ignore`;
  - `Closed_after_action_raises_nothing`;
  - `Withdraw_closes_and_later_closed_signal_is_silent`;
  - `Unknown_ids_and_keys_are_ignored`.
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar o serviço e o `TmdsNotificationBus`. O bus real é verificado só na Task 8, à mão.
- [ ] **Step 4:** Suíte inteira passa e o build fica sem avisos.
- [ ] **Step 5: Commit** — `feat(linux): notificação de lembrete via D-Bus com ações`

---

## Task 7: Composição, ativação da janela e tela Hoje

**Files:**
- Create: `src/SlimeGoals.App/Goals/ReminderFormatter.cs`, `src/SlimeGoals.App/Hosting/IAppHostedService.cs`, `src/SlimeGoals.App/Hosting/IWindowActivator.cs`, `src/SlimeGoals.App/Hosting/MainWindowActivator.cs`, `src/SlimeGoals.App/Hosting/ReminderHost.cs` (só depende de portas e serviços do Core, então serve também ao Android)
- Modify: `src/SlimeGoals.App/App.axaml.cs`, `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`, `src/SlimeGoals.App/Views/TodayView.axaml`, `src/SlimeGoals.Desktop/CompositionRoot.cs`
- Test: `tests/SlimeGoals.App.Tests/Goals/ReminderFormatterTests.cs`, `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`, `tests/SlimeGoals.App.Tests/Hosting/ReminderHostTests.cs`

**Interfaces:**

```csharp
public sealed class ReminderFormatter : IReminderFormatter;
// Title = goal.Title; Body = GoalFormatting.Progress(new GoalProgress(goal, total, total >= goal.Target));
// PrimaryLabel: Completion "Feito", Quantity "+1", Duration "Iniciar".

public interface IAppHostedService { Task StartAsync(); Task StopAsync(); }
public interface IWindowActivator { void Activate(); }   // Show() + Activate() na thread da UI
public sealed class MainWindowActivator : IWindowActivator { public Window? Window { get; set; } }  // Dispatcher.UIThread.Post

public sealed class ReminderHost(IReminderScheduler scheduler, INotificationService notifications,
    ReminderResponseHandler handler, ReminderService reminders, IWindowActivator window) : IAppHostedService;

// TodayPageViewModel: construtor ganha ReminderService e DataChangeNotifier (no fim)
public ObservableCollection<UpcomingReminderViewModel> Upcoming { get; }
public bool HasUpcoming { get; }
public sealed record UpcomingReminderViewModel(string Time, string Title);   // "14:30", título da meta
```

Regras:
- `ReminderHost`:
  - `StartAsync` → assina `ActionReceived` e chama `scheduler.StartAsync()`;
  - cada ação → `handler.HandleAsync`; se o resultado for `OpenApp`, chama `window.Activate()`;
  - `StopAsync` → `reminders.ExpirePendingAsync()`. O agendador e o bus são descartados pelo container.
- `App`: depois de criar a `MainWindow`, entrega a janela ao `IWindowActivator` registrado e chama `StartAsync` de cada `IAppHostedService` sem bloquear a UI. No `desktop.Exit`, chama `Task.Run(StopAsync).Wait(TimeSpan.FromSeconds(2))` (sem depender da thread da UI).
- `CompositionRoot`: registra os tipos novos.
  - Singletons: `DataChangeNotifier`, `ReminderService`, `ReminderResponseHandler`, `IReminderFormatter`, `IReminderScheduler`, `INotificationBus`, `INotificationService`, `MainWindowActivator` (também como `IWindowActivator`) e `IAppHostedService`.
  - O `INotificationBus` é o `TmdsNotificationBus`, o `INotificationService` é o `DbusNotificationService`, o `IReminderScheduler` é o `ProcessTimerReminderScheduler` e o `IAppHostedService` é o `ReminderHost`.
  - A `App` recebe o `MainWindowActivator` pelo container e preenche `Window`.
  - `ValidateOnBuild` continua ligado.
- `TodayPageViewModel`:
  - `Changed` → recarrega na thread da UI, pelo `SynchronizationContext` já capturado;
  - `Upcoming` vem de `UpcomingTodayAsync` no mesmo `LoadAsync`, com a hora local em `"HH:mm"`.
- `TodayView`: abaixo da lista, a seção "Próximos lembretes", visível só com itens. Cada linha mostra hora e título, com texto `Muted` e a hora em `SemiBold`.

- [ ] **Step 1: Testes que falham:**
  - `Formatter_uses_progress_text_and_primary_label_per_measure`;
  - `Today_reloads_when_data_changes` (`NotifyChanged` → o cartão mostra o novo total);
  - `Today_lists_upcoming_reminders_of_today` (agora 07:00 → `"08:00"`, título);
  - `Host_open_app_outcome_activates_window` (ação `Primary` em meta de tempo com outro cronômetro → `Activate` chamado uma vez);
  - `Host_stop_expires_pending`.
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Suíte inteira passa; `dotnet build SlimeGoals.slnx` sem avisos; `dotnet run --project src/SlimeGoals.Desktop` sobe sem exceção (o `ValidateOnBuild` resolve tudo).
- [ ] **Step 5: Commit** — `feat(app,desktop): lembretes ligados ao app e próximos lembretes na tela Hoje`

---

## Task 8: Verificação manual no GNOME e spec

**Files:**
- Modify: `specs/Planejamento_v3.md` (§9: decisões 1–4 e 7 deste plano; §15: "Decisões da Fase 4"; §12: o critério "janela fechada" da 4c vai para a 4d)

- [ ] **Step 1:** Com `XDG_DATA_HOME=/tmp/slimegoals-lembretes`, criar três metas, uma de cada medida, com intervalo de 5 min e janela incluindo a hora atual. A primeira notificação de cada meta aparece em até 1 min.
- [ ] **Step 2:** Testar cada ação:
  - **Feito**, **+1** e **Iniciar**: o registro ou o cronômetro aparecem na tela Hoje sem recarregar à mão;
  - **Adiar**: a próxima notificação vem 10 min depois;
  - **Ignorar** e o **X** da notificação: a próxima vem pelo intervalo;
  - **Abrir**: a janela vem à frente;
  - **Iniciar** com outro cronômetro rodando: a janela vem à frente e o cronômetro atual continua.
- [ ] **Step 3:** Cumprir uma meta pela tela: a notificação pendente dela some da central. Encerrar o app: as pendentes somem. Com Não Perturbe, a notificação vai para a central, e as ações funcionam ao expandir.
- [ ] **Step 4:** Atualizar a spec. Suíte inteira passa.
- [ ] **Step 5: Commit** — `docs(specs): decisões da Fase 4 (lembretes no Linux)`
