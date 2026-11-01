# Fase 3 — Medidas, registros e tela Hoje — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada meta ganha uma medida (conclusão, quantidade ou tempo); a tela Hoje mostra o progresso do período atual com controles por medida (Feito/Desmarcar, −/+ e painel com valor e nota, cronômetro que sobrevive ao fechamento do app) e um resumo da semana.

**Architecture:** Core ganha `Measure`, `Checkin`, `ActiveTimer`, duas portas (`ICheckinRepository`, `ITimerRepository`) e três serviços: `CheckinService` (ajustes com sinal, total do período ≥ 0), `TimerService` (um cronômetro por vez, persistido) e `ProgressService` (instantâneo da tela Hoje e % da semana). A Infrastructure recebe a migração `003_measures.sql` e dois repositórios Dapper. Na App, o formulário de meta ganha a medida e a `TodayView` deixa de ser placeholder; os comandos ficam no `TodayPageViewModel` e recebem o cartão (`TodayGoalViewModel`) como parâmetro, no mesmo padrão da tela Metas.

**Tech Stack:** as mesmas versões das Fases 1–2 (Avalonia 12.1.4, CommunityToolkit.Mvvm 8.4.2, Dapper, Microsoft.Data.Sqlite, xunit.v3 MTP, AwesomeAssertions, Microsoft.Extensions.TimeProvider.Testing). Nenhum pacote novo.

**Spec:** `specs/Planejamento_v3.md` v1.4 — §5.1 e **§5.2 (medida)**, §7 (Hoje, Editar meta, % da semana), §8 (esquema), §15 "Decisões da versão 1.4".

## Global Constraints

- Tudo que vale nas Fases 1–2 continua: `net10.0`, `Nullable`, `TreatWarningsAsErrors`, versões centralizadas, Core sem dependências, relógio via `TimeProvider`, textos em pt-BR e identificadores em inglês, bindings compilados, cores e raios só por `DynamicResource`, alvos de toque ≥ 44 px, ViewModels sem tipos visuais do Avalonia.
- "Hoje" no Core = `DateOnly.FromDateTime(time.GetLocalNow().DateTime)`; períodos locais convertidos com `DateRange.ToUtc(time.LocalTimeZone)` antes de consultar `checkins.done_at`.
- Instantes gravados em UTC, texto ISO 8601 `"O"` (mesmo formato do `GoalRow`).
- Registros (`checkins.amount`) são inteiros com sinal na unidade base da medida: 1/−1 (conclusão), unidades (quantidade), minutos (tempo). O total do período nunca fica negativo (spec §5.2).
- Limites (decisão deste plano, ver Task 1): alvo da quantidade 1–9999; alvo do tempo 1–1440 min na diária e 1–10080 min na semanal; unidade 1–20 caracteres (após `Trim`), obrigatória só na quantidade; nota até 200 caracteres (vazia → `null`); valor de um registro: conclusão ±1, quantidade 1–9999 em módulo, tempo 1–1440 min em módulo.
- Branch `feat/fase-3-medidas-e-hoje`; Conventional Commits (`core`, `infra`, `app`, `specs`); commits terminam com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; integração por PR.
- Comando de testes: `dotnet test --solution SlimeGoals.slnx`.

## Review Focus

1. **Remover mais do que existe** (− com total 0; painel "Remover 40 min" com 30 registrados; "Desmarcar" de conclusão já pendente): o serviço recusa com a mensagem e o total não muda; a tela mostra a mensagem sem quebrar. Coberto nas Tasks 2 e 7.
2. **Cronômetro e reabertura do app** (rodando ou parado sem registrar quando o app fecha): um ViewModel novo, montado do zero, mostra o cronômetro ainda rodando com o tempo contado desde `started_at`, ou o painel de confirmação com os minutos congelados em `stopped_at`. Coberto nas Tasks 3 e 7.
3. **Fronteira de dia/semana no fuso local** (registro às 23:30 em São Paulo = 02:30 UTC do dia seguinte; domingo à noite): conta no dia/semana **local**; segunda-feira começa semana nova. Coberto nas Tasks 2 e 4.
4. **Trocar a medida depois de registrar** (formulário burlado, edição concorrente): o serviço recusa com erro no campo `Measure`; o formulário de meta com registros mostra a medida travada. Coberto nas Tasks 1, 2 e 6.
5. **Cronômetro de meta que deixou de existir para o Hoje** (meta arquivada, excluída ou vencida com cronômetro rodando): arquivar/excluir limpa o cronômetro; o Hoje não quebra e não mostra cronômetro órfão. Coberto nas Tasks 2, 3 e 7.

---

## Mapa de arquivos

```
src/SlimeGoals.Core/
  Domain/Measure.cs                       (novo)
  Domain/Goal.cs, Domain/GoalDraft.cs     (alterados: Measure, Unit)
  Domain/Checkin.cs, Domain/ActiveTimer.cs (novos)
  Abstractions/ICheckinRepository.cs, Abstractions/ITimerRepository.cs (novos)
  Validation/GoalValidator.cs             (alterado: regras por medida)
  Services/GoalService.cs                 (alterado: normalização, medida travada, limpa cronômetro)
  Services/CheckinService.cs, Services/TimerService.cs, Services/ProgressService.cs (novos)
src/SlimeGoals.Infrastructure/
  Data/Migrations/003_measures.sql        (novo)
  Data/GoalRow.cs, Data/GoalRepository.cs (alterados: measure, unit)
  Data/CheckinRepository.cs, Data/TimerRepository.cs (novos)
  InfrastructureServiceCollectionExtensions.cs (alterado)
src/SlimeGoals.App/
  Goals/GoalFormatting.cs                 (alterado: textos por medida)
  ViewModels/GoalEditViewModel.cs, ViewModels/GoalsPageViewModel.cs (alterados)
  ViewModels/TodayPageViewModel.cs        (reescrito), ViewModels/TodayGoalViewModel.cs (novo)
  Views/GoalEditView.axaml, Views/TodayView.axaml (alterado / reescrito)
  Styles/Controls.axaml                   (alterado: barra de progresso, cartão do Hoje)
src/SlimeGoals.Desktop/CompositionRoot.cs (alterado: novos serviços)
tests/SlimeGoals.Testing/                 (InMemoryCheckinRepository, InMemoryTimerRepository)
tests/…                                   (testes novos por task; construções de GoalService atualizadas)
specs/Planejamento_v3.md                  (§8: coluna stopped_at; limites)
```

---

## Task 1: Medida no domínio e na validação

**Files:**
- Create: `src/SlimeGoals.Core/Domain/Measure.cs`
- Modify: `src/SlimeGoals.Core/Domain/Goal.cs`, `src/SlimeGoals.Core/Domain/GoalDraft.cs`, `src/SlimeGoals.Core/Validation/GoalValidator.cs`, `src/SlimeGoals.Core/Services/GoalService.cs` (só `Normalize` e `Apply`)
- Test: `tests/SlimeGoals.Core.Tests/Validation/GoalValidatorTests.cs`, `tests/SlimeGoals.Core.Tests/Services/GoalServiceTests.cs`

**Interfaces:**

```csharp
public enum Measure { Completion, Quantity, Duration }

// Goal (acréscimos) — padrões = metas anteriores à v1.4 (mesmo DEFAULT da migração 003)
public Measure Measure { get; init; } = Measure.Quantity;
public string? Unit { get; init; } = "vezes";        // só Quantity; null nas outras

// GoalDraft: dois parâmetros novos NO FIM, com padrão (as chamadas existentes continuam compilando)
public sealed record GoalDraft(..., DateOnly? EndDate, Measure Measure = Measure.Quantity, string? Unit = "vezes");

// GoalValidator (acréscimos)
public const int MaxUnitLength = 20;
public const int MaxQuantityTarget = 9999;
public const int MaxDailyMinutes = 1440, MaxWeeklyMinutes = 10080;
```

- `MaxTarget = 99` deixa de existir; mensagens exatas por medida (campo `Target`):
  - Quantity: `"O alvo deve ser entre 1 e 9999."`
  - Duration diária: `"O tempo deve ser entre 1 min e 24 h."`; semanal: `"O tempo deve ser entre 1 min e 168 h."`
  - Completion com `Target != 1`: `"Meta de conclusão tem alvo 1."`
- Campo `Unit`: Quantity sem unidade (nula ou só espaços) → `"Informe a unidade."`; mais de 20 → `"A unidade deve ter até 20 caracteres."`.
- `GoalService.Normalize`: `Unit` com `Trim`; Completion → `Target = 1`, `Unit = null`; Duration → `Unit = null`. `Apply` copia `Measure` e `Unit`.

- [ ] **Step 1: Testes que falham:** `Quantity_target_accepts_9999_and_rejects_10000`; `Duration_target_limit_depends_on_recurrence` (diária 1440 ok/1441 erro; semanal 10080 ok/10081 erro); `Completion_requires_target_1`; `Quantity_requires_unit`; `Unit_longer_than_20_is_rejected`; no serviço, `Create_normalizes_completion_and_duration` (Completion com alvo 5 e unidade "x" → salva alvo 1, unidade null; Duration com unidade "x" → null; Quantity " copos " → "copos").
- [ ] **Step 2:** Rodar e ver falhar (tipo `Measure` inexistente).
- [ ] **Step 3:** Implementar. Ajustar testes antigos que citam `MaxTarget`/`"entre 1 e 99"` para a regra nova da quantidade.
- [ ] **Step 4:** `dotnet test --solution SlimeGoals.slnx` → tudo passa.
- [ ] **Step 5: Commit** — `feat(core): medida da meta (conclusão, quantidade, tempo) e validação por medida`

---

## Task 2: Registros (`CheckinService`) e regras de meta que dependem deles

**Files:**
- Create: `src/SlimeGoals.Core/Domain/Checkin.cs`, `src/SlimeGoals.Core/Domain/ActiveTimer.cs`, `src/SlimeGoals.Core/Abstractions/ICheckinRepository.cs`, `src/SlimeGoals.Core/Abstractions/ITimerRepository.cs`, `src/SlimeGoals.Core/Services/CheckinService.cs`, `tests/SlimeGoals.Testing/InMemoryCheckinRepository.cs`, `tests/SlimeGoals.Testing/InMemoryTimerRepository.cs`
- Modify: `src/SlimeGoals.Core/Services/GoalService.cs` (construtor, medida travada, limpar cronômetro)
- Test: `tests/SlimeGoals.Core.Tests/Services/CheckinServiceTests.cs`, `tests/SlimeGoals.Core.Tests/Services/GoalServiceMeasureTests.cs`; atualizar as construções de `GoalService` nos testes existentes (Core e App)

**Interfaces:**

```csharp
public sealed record Checkin
{
    public required Guid Id { get; init; }            // Guid.CreateVersion7(now)
    public required Guid GoalId { get; init; }
    public required DateTimeOffset DoneAt { get; init; }   // UTC
    public required int Amount { get; init; }        // com sinal, ≠ 0
    public string? Note { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
}

/// StoppedAt == null → rodando; preenchido → parado, aguardando registrar ou descartar.
public sealed record ActiveTimer(Guid GoalId, DateTimeOffset StartedAt, DateTimeOffset? StoppedAt = null);

public interface ICheckinRepository
{
    Task AddAsync(Checkin checkin, CancellationToken ct = default);
    /// done_at em [fromUtc, toUtc), sem excluídos, ordenado por DoneAt.
    Task<IReadOnlyList<Checkin>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
    Task<int> SumAsync(Guid goalId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
    Task<bool> HasAnyAsync(Guid goalId, CancellationToken ct = default);   // ignora excluídos
}

public interface ITimerRepository
{
    Task<ActiveTimer?> GetAsync(CancellationToken ct = default);
    Task<bool> TryStartAsync(ActiveTimer timer, CancellationToken ct = default);  // false se já existe um
    Task SaveAsync(ActiveTimer timer, CancellationToken ct = default);            // substitui o existente
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class CheckinService(IGoalRepository goals, ICheckinRepository checkins, TimeProvider time)
{
    public const int MaxNoteLength = 200;
    /// Registra um ajuste no período atual e devolve o novo total do período.
    public Task<int> AdjustAsync(Guid goalId, int amount, string? note = null, CancellationToken ct = default);
    /// Soma do período atual (0 fora do período).
    public Task<int> PeriodTotalAsync(Guid goalId, CancellationToken ct = default);
}

// GoalService: novo construtor e um método novo
public sealed class GoalService(IGoalRepository goals, ICheckinRepository checkins, ITimerRepository timers, TimeProvider time);
public Task<bool> HasRecordsAsync(Guid id, CancellationToken ct = default);
```

- Regras do `AdjustAsync` (mensagens exatas; período = `GoalPeriod.EvaluationPeriod(goal, hoje)`):
  - Meta inexistente/excluída → `GoalNotFoundException`. Arquivada → `GoalStateException`: `"Meta arquivada não recebe registros."`
  - `!GoalPeriod.IsShownOn(goal, hoje)` → `GoalStateException`: `"Esta meta não está ativa hoje."`
  - `amount == 0` → `GoalValidationException` campo `Amount`: `"Informe um valor diferente de zero."`; fora dos limites do valor (Global Constraints) → campo `Amount`: `"Valor fora do limite."`. Nota com mais de 200 caracteres → campo `Note`: `"A nota deve ter até 200 caracteres."` (nota com `Trim`, vazia → `null`).
  - Total do período + `amount` < 0 → `GoalStateException`: `"Não dá para remover mais do que o registrado no período."`
  - Completion com total + `amount` > 1 → `GoalStateException`: `"Meta já concluída neste período."`
  - Caso válido: grava `Checkin` com `DoneAt = UpdatedAt = agora` e devolve o novo total.
- `GoalService.UpdateAsync`: `draft.Measure != atual.Measure` e `HasAnyAsync` → `GoalValidationException` campo `Measure`: `"A medida não muda depois do primeiro registro."` `ArchiveAsync`, `DeleteAsync` e `ArchiveExpiredAsync` chamam `timers.ClearAsync()` quando o cronômetro ativo é de uma meta afetada.

- [ ] **Step 1: Testes que falham** (relógio `2026-11-02T12:00Z`, fuso `America/Sao_Paulo`; segunda-feira): `Adjust_adds_and_returns_period_total`; `Removing_more_than_total_is_refused_and_nothing_is_saved`; `Completion_accepts_1_then_refuses_second_1_and_accepts_minus_1`; `Zero_and_out_of_limit_amounts_are_rejected`; `Note_is_trimmed_empty_becomes_null_and_201_chars_rejected`; `Archived_or_inactive_today_goal_is_refused` (diária sem segunda nos dias ativos); `Late_night_local_checkin_counts_for_local_day` (registro a `2026-11-03T02:30Z` = 02/11 23:30 local entra no total de 02/11; às `2026-11-03T03:30Z` o total do dia 03/11 é 0); `Weekly_total_resets_on_local_monday`; no `GoalService`: `Measure_change_is_refused_after_first_record`, `Measure_change_is_allowed_without_records`, `Archive_and_delete_clear_the_timer_of_that_goal_only`.
- [ ] **Step 2:** Rodar e ver falhar.
- [ ] **Step 3:** Implementar os tipos, os fakes em memória (mesmo contrato descrito nas interfaces) e as regras. Atualizar as construções `new GoalService(repo, time)` dos testes para o construtor novo.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(core): registros com sinal, total do período não negativo e medida travada`

---

## Task 3: Cronômetro (`TimerService`)

**Files:**
- Create: `src/SlimeGoals.Core/Services/TimerService.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/TimerServiceTests.cs`

**Interfaces:**

```csharp
public sealed class TimerService(IGoalRepository goals, ITimerRepository timers, CheckinService checkins, TimeProvider time)
{
    public Task<ActiveTimer?> GetAsync(CancellationToken ct = default);
    public Task StartAsync(Guid goalId, CancellationToken ct = default);
    /// Congela o cronômetro (StoppedAt = agora; idempotente) e devolve os minutos.
    public Task<int> StopAsync(CancellationToken ct = default);
    /// Registra os minutos (ajustáveis) do cronômetro parado, limpa-o e devolve o novo total do período.
    public Task<int> RegisterAsync(int minutes, string? note = null, CancellationToken ct = default);
    public Task DiscardAsync(CancellationToken ct = default);
    /// Minutos inteiros entre StartedAt e (StoppedAt ?? now), arredondando ao mais próximo (meio → para cima).
    public static int Minutes(ActiveTimer timer, DateTimeOffset now);
}
```

- Regras (mensagens exatas):
  - `StartAsync`: meta não-`Duration` → `GoalStateException`: `"Só metas de tempo têm cronômetro."`; arquivada ou fora de `IsShownOn(hoje)` → as mesmas mensagens do `CheckinService`; já existe cronômetro (rodando ou parado) → `GoalStateException`: `"Já há um cronômetro em \"{título da meta dele}\". Pare-o antes de iniciar outro."` (se a meta dele não existir mais, o cronômetro órfão é limpo e o novo inicia).
  - `StopAsync` sem cronômetro → `GoalStateException`: `"Nenhum cronômetro rodando."`
  - `RegisterAsync` sem cronômetro parado → `GoalStateException`: `"Pare o cronômetro antes de registrar."`; delega o registro a `CheckinService.AdjustAsync(goalId, minutes, note)` (mesmas validações; `minutes` 0 → erro de valor) e só então limpa o cronômetro.
  - `DiscardAsync` limpa (sem cronômetro → nada).

- [ ] **Step 1: Testes que falham:** `Start_then_stop_returns_rounded_minutes` (29 s → 0; 30 s → 1; 25 min 40 s → 26); `Stopped_timer_minutes_stay_frozen_while_time_passes`; `Start_is_refused_for_non_duration_goal`; `Second_start_reports_running_goal_title`; `Orphan_timer_is_replaced_on_start`; `Register_adds_checkin_and_clears`; `Register_with_zero_minutes_is_refused_and_keeps_timer`; `Register_without_stopping_is_refused`; `Discard_clears_without_checkin`; `Timer_survives_new_service_instance` (outra instância de `TimerService` sobre o mesmo repositório lê o mesmo estado).
- [ ] **Step 2:** Rodar e ver falhar. **Step 3:** Implementar. **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(core): cronômetro persistido para metas de tempo`

---

## Task 4: Progresso do Hoje e % da semana (`ProgressService`)

**Files:**
- Create: `src/SlimeGoals.Core/Services/ProgressService.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/ProgressServiceTests.cs`

**Interfaces:**

```csharp
public sealed record GoalProgress(Goal Goal, int Done, bool IsMet);       // Done = soma do período atual
public sealed record WeekSummary(int Percent, int InProgress, int Completed);
public sealed record TodaySnapshot(IReadOnlyList<GoalProgress> Goals, WeekSummary Week, ActiveTimer? Timer);

public sealed class ProgressService(IGoalRepository goals, ICheckinRepository checkins, ITimerRepository timers, TimeProvider time)
{
    public Task<TodaySnapshot> GetTodayAsync(CancellationToken ct = default);
}
```

- **Lista:** metas não arquivadas com `GoalPeriod.IsShownOn(meta, hoje)`; primeiro as não cumpridas, depois as cumpridas; dentro de cada grupo, por `CreatedAt`. `IsMet = Done >= Target`.
- **Uma consulta só:** `checkins.ListAsync` da semana local atual (segunda 00:00 → próxima segunda 00:00, em UTC); somas por meta/dia feitas em memória com `TimeZoneInfo.ConvertTime(doneAt, time.LocalTimeZone)`.
- **`InProgress` / `Completed`:** contagem de não cumpridas / cumpridas da lista.
- **`Percent`** (spec §7): metas não arquivadas cujo período intersecta [segunda, hoje]. Semanal: `min(soma da semana ÷ alvo, 1)`. Diária: dias ativos dentro do período em [segunda, ontem] mais hoje **se já cumprido** (o dia em andamento não puxa a média para baixo, como na regra de sequência da §5.1); fração = dias cumpridos ÷ esses dias; sem nenhum dia considerado → a meta fica fora da média. `Percent = round(média × 100)` (meio → para cima); sem metas na média → 0.
- **`Timer`:** o cronômetro ativo se a meta dele está na lista; senão `null`.

- [ ] **Step 1: Testes que falham** (relógio quarta `2026-11-04T15:00Z`, São Paulo): `Lists_goals_shown_today_pending_first_then_met`; `Excludes_archived_and_out_of_period_and_inactive_day_daily`; `Done_sums_signed_amounts_of_current_period`; `Week_percent_averages_weekly_and_daily_goals` (semanal 3 de 6 → 0,5; diária todos os dias, seg e ter cumpridos, hoje não → 2 ÷ 2 = 1 → `Percent` 75); `Today_counts_in_daily_fraction_only_when_met`; `Daily_goal_starting_today_unmet_is_left_out_of_average`; `No_goals_gives_zero_percent`; `Timer_is_returned_only_for_listed_goal`.
- [ ] **Step 2:** Rodar e ver falhar. **Step 3:** Implementar. **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(core): progresso do período e resumo da semana para a tela Hoje`

---

## Task 5: Persistência (migração 003 e repositórios)

**Files:**
- Create: `src/SlimeGoals.Infrastructure/Data/Migrations/003_measures.sql`, `src/SlimeGoals.Infrastructure/Data/CheckinRepository.cs`, `src/SlimeGoals.Infrastructure/Data/TimerRepository.cs`
- Modify: `src/SlimeGoals.Infrastructure/Data/GoalRow.cs`, `src/SlimeGoals.Infrastructure/Data/GoalRepository.cs`, `src/SlimeGoals.Infrastructure/InfrastructureServiceCollectionExtensions.cs`, `specs/Planejamento_v3.md` (§8)
- Test: `tests/SlimeGoals.Infrastructure.Tests/Data/MigrationRunnerTests.cs` (classe `Migration003Tests`; `Latest` = 3), `tests/SlimeGoals.Infrastructure.Tests/Data/GoalRepositoryTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Data/CheckinRepositoryTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Data/TimerRepositoryTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/InfrastructureServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Produces: `CheckinRepository : ICheckinRepository` e `TimerRepository : ITimerRepository` (Dapper, mesmos padrões do `GoalRepository`: conexão por operação, GUID `"D"`, instantes `"O"` UTC); registro no DI como singletons.
- `003_measures.sql`: recria `goals` (mesmo procedimento da 002) com `measure TEXT NOT NULL DEFAULT 'quantity'`, `unit TEXT` e os CHECKs da spec §8; copia as metas com `measure = 'quantity'`, `unit = 'vezes'`. Cria `active_timer (id INTEGER PRIMARY KEY CHECK (id = 1), goal_id TEXT NOT NULL REFERENCES goals(id), started_at TEXT NOT NULL, stopped_at TEXT)`.
- `TryStartAsync` = `INSERT OR IGNORE` com `id = 1`, devolve se inseriu; `SaveAsync` = `INSERT OR REPLACE`.
- **Spec:** §8 ganha `stopped_at TEXT` em `active_timer` (parado aguardando registrar/descartar) e os limites das Global Constraints em §5.2.

- [ ] **Step 1: Testes que falham:** `Migration003Tests`: `Existing_goals_become_quantity_in_vezes` (banco v2 via `Migrate(c, upTo: 2)` com uma meta → após migrar: `quantity`, `vezes`, demais colunas iguais); `Measure_checks_are_enforced` (`completion` com alvo 2; `quantity` sem unidade; `duration` com unidade → `CHECK`); `Only_one_timer_row`. Repositórios: `Goal_round_trips_measure_and_unit` (as três medidas); `Checkin_round_trip_and_sum_ignore_deleted_and_respect_half_open_range`; `HasAny_ignores_deleted`; `Timer_try_start_twice_returns_false_save_replaces_and_clear`. DI: resolve `ICheckinRepository` e `ITimerRepository`.
- [ ] **Step 2:** Rodar e ver falhar. **Step 3:** Implementar. **Step 4:** Suíte inteira passa.
- [ ] **Step 5:** Rodar a migração numa **cópia** do banco real (`~/.local/share/SlimeGoals/slimegoals.db` → scratchpad, `XDG_DATA_HOME` apontando para a cópia, `timeout 10 dotnet run --project src/SlimeGoals.Desktop`). Expected: `user_version` 3, metas preservadas como `quantity`/`vezes`, `PRAGMA foreign_key_check` vazio.
- [ ] **Step 6: Commit** — `feat(infra): migração 003 com medidas e cronômetro, repositórios de registros e cronômetro`

---

## Task 6: Medida no formulário e textos por medida

**Files:**
- Modify: `src/SlimeGoals.App/Goals/GoalFormatting.cs`, `src/SlimeGoals.App/ViewModels/GoalEditViewModel.cs`, `src/SlimeGoals.App/ViewModels/GoalsPageViewModel.cs`, `src/SlimeGoals.App/Views/GoalEditView.axaml`
- Test: `tests/SlimeGoals.App.Tests/Goals/GoalFormattingTests.cs`, `tests/SlimeGoals.App.Tests/ViewModels/GoalEditViewModelTests.cs`, `tests/SlimeGoals.App.Tests/ViewModels/GoalsPageViewModelTests.cs`

**Interfaces:**

```csharp
public static class GoalFormatting
{
    public static string Duration(int minutes);              // 45 → "45 min"; 120 → "2 h"; 150 → "2 h 30"
    public static string Elapsed(TimeSpan elapsed);          // "0:05:09"; "1:02:03" (horas sem zero à esquerda)
    public static string Target(Goal goal);                  // substitui Target(Recurrence, int):
        // Completion: "Concluir no dia" | "Concluir na semana"
        // Quantity:   "8 copos por dia" | "3 vezes por semana"
        // Duration:   "45 min por dia"  | "5 h por semana"
    public static string Progress(GoalProgress p);           // Completion: IsMet ? ("Feito hoje" | "Feito na semana") : "Pendente"
        // Quantity: "5 de 8 copos hoje" | "2 de 3 vezes na semana"
        // Duration: "2 h 30 de 5 h na semana" | "0 min de 45 min hoje"
    // Summary(Goal) passa a usar Target(goal); Interval/Window/Days/Period sem mudança.
}

// GoalEditViewModel — construtor muda:
public GoalEditViewModel(GoalService service, Goal? existing, bool measureLocked, Action<bool> close);
[ObservableProperty] Measure Measure;                    // padrão na criação: Quantity
public bool IsCompletion { get; set; }  public bool IsQuantity { get; set; }  public bool IsDuration { get; set; }
public bool IsMeasureLocked { get; }                     // true → segmento desabilitado + dica
public string MeasureLockedHint => "A medida não muda depois do primeiro registro.";
[ObservableProperty] string Unit;                        // padrão "vezes"; visível só na quantidade
public decimal? DurationHoursInput { get; set; }         // 0–168
public decimal? DurationMinutesInput { get; set; }       // 0–59
public string? UnitError { get; }  public string? MeasureError { get; }
// GoalService.HasRecordsAsync decide measureLocked: GoalsPageViewModel.Edit vira EditAsync.
```

- Alvo por medida no formulário: Quantity → `TargetInput` (1–9999) + `Unit`; Duration → horas + minutos (`Target = h × 60 + min`; os dois vazios → erro `"Informe o tempo."` em `TargetError`); Completion → nenhum campo de alvo (draft com alvo 1, unidade nula). Ao abrir uma meta de tempo, horas/minutos vêm de `Target`. O sufixo `TargetSuffix` continua ("por dia"/"por semana").
- Trocar a medida no formulário preserva o que foi digitado em cada uma (voltar para quantidade mostra o alvo e a unidade de antes).

- [ ] **Step 1: Testes que falham:** `GoalFormatting` com `[Theory]` dos exemplos acima e `Summary_uses_measure_target`; `GoalEditViewModel`: `New_goal_defaults_to_quantity_in_vezes`; `Duration_hours_and_minutes_build_target_in_minutes` (2 h 30 → 150) e `Editing_duration_goal_splits_target` (150 → 2 e 30); `Empty_duration_reports_informe_o_tempo`; `Completion_saves_target_1_without_unit`; `Missing_unit_shows_unit_error`; `Switching_measure_keeps_each_measure_inputs`; `Locked_measure_is_reported_and_service_error_shows_on_measure_field`; `GoalsPageViewModel`: `Edit_locks_measure_when_goal_has_records`.
- [ ] **Step 2:** Rodar e ver falhar. **Step 3:** Implementar ViewModels e a View (segmento Conclusão | Quantidade | Tempo abaixo do tipo; campos condicionais; `IsEnabled` do segmento ligado a `!IsMeasureLocked`). **Step 4:** Suíte inteira passa, build sem avisos.
- [ ] **Step 5: Commit** — `feat(app): medida no formulário de meta e textos de progresso por medida`

---

## Task 7: `TodayPageViewModel` e cartões

**Files:**
- Create: `src/SlimeGoals.App/ViewModels/TodayGoalViewModel.cs`
- Modify: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`, `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs` (construção)
- Test: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**

```csharp
public sealed partial class TodayPageViewModel(
    ProgressService progress, CheckinService checkins, TimerService timers, GoalService goals, TimeProvider time) : PageViewModel
{
    public override string Title => "Hoje";
    public ObservableCollection<TodayGoalViewModel> Goals { get; }
    [ObservableProperty] int WeekPercent;  [ObservableProperty] int InProgress;  [ObservableProperty] int Completed;
    [ObservableProperty] string? ErrorMessage;
    public bool IsEmpty { get; }                         // EmptyMessage = "Nenhuma meta para hoje. Crie uma em Metas."
    // Activate() → LoadCommand: ArchiveExpiredAsync + GetTodayAsync (versão mais recente vence, como em GoalsPageViewModel)
    // Comandos (parâmetro: TodayGoalViewModel):
    //   Complete, Uncomplete, Increment, Decrement, OpenPanel, CancelPanel, ConfirmPanel,
    //   StartTimer, StopTimer, RegisterTimer, DiscardTimer
}

public sealed partial class TodayGoalViewModel : ObservableObject
{
    public TodayGoalViewModel(GoalProgress progress, ActiveTimer? timer, TimeProvider time);
    public Goal Goal { get; }  public string Title { get; }  public string IconKey { get; }  public GoalColor Color { get; }
    public bool IsCompletion { get; }  public bool IsQuantity { get; }  public bool IsDuration { get; }
    public string ProgressText { get; }                  // GoalFormatting.Progress
    public double Fraction { get; }                      // min(Done ÷ Target, 1), para a barra
    public bool IsMet { get; }
    // Painel (quantidade e tempo)
    [ObservableProperty] bool IsPanelOpen;  [ObservableProperty] bool PanelRemoves;   // Adicionar | Remover
    [ObservableProperty] decimal? PanelAmountInput;                                     // quantidade
    [ObservableProperty] decimal? PanelHoursInput;  [ObservableProperty] decimal? PanelMinutesInput;   // tempo
    [ObservableProperty] string? PanelNote;  [ObservableProperty] string? PanelError;
    // Cronômetro (só tempo)
    public bool IsTimerRunning { get; }  public bool IsTimerStopped { get; }
    [ObservableProperty] string Elapsed;                 // GoalFormatting.Elapsed, atualizado a cada 1 s enquanto roda
    [ObservableProperty] decimal? StoppedMinutesInput;   // minutos sugeridos (TimerService.Minutes), editáveis
}
```

- Cada comando chama o serviço e recarrega o instantâneo. `GoalStateException`/`GoalNotFoundException` → `ErrorMessage` (a mensagem do serviço; meta sumida → `"Esta meta não existe mais."`); `GoalValidationException` em ação de painel → `PanelError` (primeira mensagem) com o painel aberto; qualquer outra exceção → `"Não foi possível registrar."`. `ErrorMessage` é limpa no início de cada ação.
- Valores do painel: quantidade = `PanelAmountInput`; tempo = horas × 60 + minutos; `PanelRemoves` → valor negativo. Campo vazio → `PanelError = "Informe um valor."` sem chamar o serviço.
- Atualização do `Elapsed`: um `ITimer` de `time.CreateTimer` com período de 1 s, criado só enquanto há cronômetro rodando e descartado ao parar/recarregar (testável com `FakeTimeProvider.Advance`).
- Enquanto há cronômetro (rodando ou parado), o ▶ das outras metas de tempo fica desabilitado (`CanStartTimer` falso).

- [ ] **Step 1: Testes que falham** (relógio quarta `2026-11-04T15:00Z`, São Paulo; repositórios em memória): `Load_shows_goals_texts_and_week_summary`; `Increment_and_decrement_update_progress`; `Decrement_at_zero_shows_service_message_and_keeps_total`; `Complete_then_uncomplete_completion_goal`; `Panel_removes_30_minutes_from_duration_goal`; `Panel_with_empty_value_shows_informe_um_valor`; `Panel_note_too_long_keeps_panel_open_with_error`; `Timer_start_ticks_elapsed_then_stop_suggests_minutes_and_register_adds_them` (avançar 25 min 40 s → sugestão 26); `Reopened_page_shows_running_timer_from_started_at` (novo VM sobre os mesmos repositórios); `Reopened_page_shows_stopped_timer_with_frozen_minutes`; `Other_duration_goals_cannot_start_while_timer_exists`; `Archived_goal_with_timer_disappears_without_error`; `Unexpected_error_shows_generic_message`.
- [ ] **Step 2:** Rodar e ver falhar. **Step 3:** Implementar. Atualizar `ShellViewModelTests` para construir o `TodayPageViewModel` com os serviços em memória. **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(app): ViewModel da tela Hoje com registros e cronômetro`

---

## Task 8: `TodayView`, composição e verificação manual

**Files:**
- Modify: `src/SlimeGoals.App/Views/TodayView.axaml`, `src/SlimeGoals.App/Styles/Controls.axaml`, `src/SlimeGoals.Desktop/CompositionRoot.cs`

**Interfaces:**
- Consumes: Task 7 (propriedades e comandos), conversores `IconGeometry`/`GoalColorBrush` da Fase 2.
- Layout (referência v4 desktop, só com dados existentes): título "Hoje"; faixa de resumo com três números (**% da semana**, **em andamento**, **concluídas**); faixa de aviso (`ErrorMessage`); lista "Metas de hoje" de cartões (ícone na cor da meta, título, `ProgressText`, barra de progresso com `Fraction`, controles da medida à direita; cartão cumprido com aparência atenuada e marca "concluída"); painel e confirmação do cronômetro abrem dentro do cartão; estado vazio com `EmptyMessage`.
- Controles: Completion → "Feito" (primário) / "Desmarcar" (ghost); Quantity → "−", "+", "⋯"; Duration → "▶" (ou "⏹" + `Elapsed` rodando; ou painel "Registrar N min" com minutos editáveis, nota, "Descartar"/"Registrar"), "⋯". Botões de ícone com `AutomationProperties.Name` em pt-BR ("Adicionar 1", "Remover 1", "Mais opções", "Iniciar cronômetro", "Parar cronômetro").
- DI: `CheckinService`, `TimerService`, `ProgressService` como singletons; `TodayPageViewModel` com o construtor novo (`ValidateOnBuild` acusa dependência faltando ao subir).

- [ ] **Step 1:** Implementar View, estilos e DI; `dotnet build` sem avisos; suíte inteira passa.
- [ ] **Step 2:** Smoke-run com banco temporário (`XDG_DATA_HOME=<scratchpad>/xdg timeout 12 dotnet run --project src/SlimeGoals.Desktop`). Expected: sobe sem exceção até o timeout.
- [ ] **Step 3:** Verificação manual com o autor (banco real, já migrado na Task 5): criar uma meta de cada medida; no Hoje, Feito/Desmarcar; −/+ e painel com nota; remover mais do que existe (mensagem); iniciar cronômetro, fechar o app, reabrir (continua contando), parar, ajustar os minutos e registrar; trocar a medida de meta com registro (travada); navegação por teclado. O autor envia prints do Hoje e do cronômetro.
- [ ] **Step 4: Commit** — `feat(app): tela Hoje com controles por medida e cronômetro`
