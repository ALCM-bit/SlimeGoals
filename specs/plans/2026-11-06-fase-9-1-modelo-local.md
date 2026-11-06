# Fase 9.1 — Modelo local da sincronização — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O banco ganha as tabelas de sincronização e a fila local, e as telas passam a limitar os totais. Nada usa rede, e com a sincronização desligada o app segue igual para o usuário.

**Architecture:**
- **Infrastructure:** migração `005_sync.sql`; `SyncOutbox` grava na fila dentro da transação que os repositórios já abriram; `SyncStateRepository` liga e desliga a sincronização.
- **Core:** porta `ISyncStateStore`; `GoalTotals.Effective` é o único lugar que limita um total; `GoalService` garante a regra da data de edição; `CheckinService` compensa o total limitado.
- **Sem interface nova:** ninguém chama `EnableAsync` no app até a Fase 9.5, então a fila fica vazia para todos os usuários.

**Tech Stack:** as mesmas versões atuais, sem pacote novo. SQLite + Dapper, xUnit v3, AwesomeAssertions, FakeTimeProvider.

**Spec:** `specs/Sincronizacao_v1.md`, §3 (conflitos, data de edição, totais), §4 (migração, fila, gravação), RS01, RS07 e §10 (linha 9.1).

**Fora deste plano:** `SyncEngine`, merge, `sync_pending` em uso, leitura da fila para envio (Fase 9.2); servidor; criptografia; telas; `PlatformCapabilities`; limpeza de marcas de exclusão antigas.

## Decisões deste plano (o autor confirma na revisão)

1. **Regra da data de edição entra aqui, não na 9.2.** A conversa previu a 9.2, mas a regra mora em `GoalService`, não depende do motor e a spec a aplica com a sincronização ligada ou não.
2. **Compensação ao registrar (acréscimo à spec).** Limitar só a leitura deixaria botões sem efeito: com total real 2 numa meta de conclusão, "Desmarcar" gravaria −1, o total seguiria 1 e a tela continuaria "Feito". O `CheckinService` passa a calcular a partir do total limitado e grava a diferença até o total real. Registros existentes não mudam. Sem sincronização, total real e limitado são iguais, e o valor gravado é o mesmo de hoje.
3. **Coluna `revision` em `sync_outbox` (acréscimo à spec).** Se uma edição acontece durante um envio, a 9.2 só pode tirar o item da fila se a revisão não mudou; sem isso a edição se perderia. Entra agora para não exigir outra migração.
4. **`position` mantém a ordem da primeira entrada.** Nova mudança substitui a operação, mas não move o item: uma meta editada continua à frente dos registros dela.
5. **Excluir um registro que o servidor nunca viu só o tira da fila.** "Viu" = tem linha em `sync_records`. Não há o que apagar lá.
6. **`sync_tombstones` guarda só o id.** A spec não pede data, e `IGoalRepository.DeleteAsync` não recebe relógio. Marca é gravada só com a sincronização ligada, pelo mesmo motivo da fila.
7. **`DisableAsync` cumpre a RS07:** limpa fila, `sync_records`, marcas e `sync_pending`, zera `last_seq` e mantém os dados e o `device_id`.
8. **O limite vale para o total do período, nunca para o dia.** Numa semanal, +5 na segunda e −2 na terça é um ajuste legítimo: a terça tem soma negativa e a semana tem 3.

## Global Constraints

- Identificadores em inglês; textos e comentários em pt-BR. `TreatWarningsAsErrors` ligado.
- Core sem I/O e sem referência a Dapper, SQLite ou outros projetos (`ArchitectureTests`).
- Fila só com a sincronização ligada: "Sem conta, nada é gravado na fila".
- "Repositórios gravam dado e fila na mesma transação."
- Totais: `[0, alvo]` nas metas de conclusão e no mínimo 0 nas demais. "Os registros não são alterados."
- Exclusão continua definitiva: nenhum dado da meta sobra nas tabelas novas.
- Testes nunca usam o banco real nem o app instalado.
- **Verificação:** `dotnet test --solution SlimeGoals.slnx` e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, sem avisos.
- **Commits:** Conventional Commits em português, escopos `core`, `infra`, `specs`, com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Branch `feat/fase-9-1-modelo-local`, a partir da `main`.

## Review Focus

1. **Desmarcar conclusão com total real 2:** um toque volta para "Pendente" (Tarefa 6).
2. **Registrar com total real negativo:** +1 mostra 1, não 0 (Tarefa 6).
3. **Falha entre o dado e a fila:** nenhum dos dois fica gravado (Tarefa 2).
4. **Banco de um usuário atual, na versão 4:** migra com os dados intactos, sincronização desligada e fila vazia (Tarefa 1).
5. **Ajuste negativo de um dia numa meta semanal:** a semana soma certo, sem limitar o dia (Tarefa 5).

---

### Task 1: Migração 005

**Files:**
- Create: `src/SlimeGoals.Infrastructure/Data/Migrations/005_sync.sql`
- Create: `tests/SlimeGoals.Infrastructure.Tests/Data/Migration005Tests.cs`
- Modify: `tests/SlimeGoals.Infrastructure.Tests/Data/MigrationRunnerTests.cs:9` (`Latest = 5`)

**Interfaces:**
- Produces: as tabelas abaixo, usadas pelas Tarefas 2 a 4.

- [ ] **Step 2: Escrever os testes** em `Migration005Tests`, usando `new TestDatabase(migrate: false)` e `MigrationRunner.Migrate(db.Anchor, upTo: 4)`, como em `Migration004Tests`:
  - `Upgrade_from_4_keeps_goals_and_checkins`: insere meta e registro na versão 4, migra, e as linhas seguem iguais.
  - `Upgrade_leaves_sync_disabled_and_queue_empty`: `SELECT enabled, device_id, last_seq FROM sync_state` devolve uma linha `(0, NULL, 0)`, e `sync_outbox` tem 0 linhas.
  - `Sync_state_accepts_only_one_row`: `INSERT INTO sync_state (id) VALUES (2)` lança `SqliteException`.
  - `Outbox_refuses_unknown_kind_and_op`: `kind = 'timer'` e `op = 'merge'` lançam `SqliteException`.

- [ ] **Step 3: Rodar:** `dotnet test --solution SlimeGoals.slnx`. Expected: falham por versão e tabelas ausentes.

- [ ] **Step 4: Criar `005_sync.sql`,** com um comentário curto no topo citando a spec:

```sql
CREATE TABLE sync_state (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  enabled INTEGER NOT NULL DEFAULT 0 CHECK (enabled IN (0, 1)),
  device_id TEXT,
  last_seq INTEGER NOT NULL DEFAULT 0,
  last_sync_at TEXT,
  last_error TEXT
);
INSERT INTO sync_state (id) VALUES (1);

CREATE TABLE sync_outbox (
  position INTEGER PRIMARY KEY AUTOINCREMENT,  -- ordem da primeira entrada
  record_id TEXT NOT NULL UNIQUE,
  kind TEXT NOT NULL CHECK (kind IN ('goal', 'checkin')),
  op TEXT NOT NULL CHECK (op IN ('upsert', 'delete')),
  base_seq INTEGER NOT NULL DEFAULT 0,
  revision INTEGER NOT NULL DEFAULT 1          -- muda a cada substituição
);

CREATE TABLE sync_records (
  record_id TEXT PRIMARY KEY,
  kind TEXT NOT NULL CHECK (kind IN ('goal', 'checkin')),
  seq INTEGER NOT NULL
);

CREATE TABLE sync_tombstones (goal_id TEXT PRIMARY KEY);

CREATE TABLE sync_pending (
  record_id TEXT PRIMARY KEY,
  goal_id TEXT NOT NULL,                       -- sem chave estrangeira: a meta ainda não chegou
  seq INTEGER NOT NULL,
  payload TEXT NOT NULL
);
CREATE INDEX idx_sync_pending_goal ON sync_pending(goal_id);
```

- [ ] **Step 5: Rodar os testes.** Expected: passam.

- [ ] **Step 6: Commit:** `feat(infra): adiciona as tabelas de sincronização`.

---

### Task 2: Fila nas gravações

**Files:**
- Create: `src/SlimeGoals.Infrastructure/Data/SyncOutbox.cs`
- Modify: `src/SlimeGoals.Infrastructure/Data/GoalRepository.cs` (`AddAsync`, `UpdateAsync`)
- Modify: `src/SlimeGoals.Infrastructure/Data/CheckinRepository.cs` (`AddAsync`)
- Create: `tests/SlimeGoals.Infrastructure.Tests/Data/SyncOutboxTests.cs`

**Interfaces:**
- Consumes: tabelas da Tarefa 1.
- Produces: `internal static class SyncOutbox` com `const string Goal = "goal"`, `const string Checkin = "checkin"` e `static Task EnqueueAsync(SqliteConnection c, IDbTransaction tx, Guid recordId, string kind, CancellationToken ct)`.

- [ ] **Step 1: Escrever os testes.** Ligam a fila com `UPDATE sync_state SET enabled = 1`:
  - `Disabled_sync_writes_nothing_to_queue`: criar e editar meta e registrar: fila com 0 linhas.
  - `Goal_add_and_update_keep_one_line`: com sync ligado, criar e editar: 1 linha, `kind = 'goal'`, `op = 'upsert'`, `revision = 2`.
  - `Checkin_add_is_queued`: 1 linha de `kind = 'checkin'`.
  - `Replaced_item_keeps_its_position`: criar meta, registrar, editar a meta: em `ORDER BY position`, a meta vem antes do registro.
  - `Base_seq_comes_from_sync_records`: com linha `seq = 7` em `sync_records`, a edição grava `base_seq = 7`; sem linha, 0.
  - `Queue_failure_rolls_back_the_data`: cria `CREATE TRIGGER fail_outbox BEFORE INSERT ON sync_outbox BEGIN SELECT RAISE(ABORT, 'teste'); END;`, tenta criar meta: lança `SqliteException` e `goals` fica vazia. Repete para registro.
  - `Update_of_missing_goal_queues_nothing`: lança `GoalNotFoundException`, fila com 0 linhas.
  - `Checkin_of_missing_goal_queues_nothing`: lança `GoalNotFoundException`, fila com 0 linhas.

- [ ] **Step 2: Rodar.** Expected: falham.

- [ ] **Step 3: Implementar `SyncOutbox.EnqueueAsync`** com este comando, que respeita a chave de ligado sem ida extra ao banco:

```sql
INSERT INTO sync_outbox (record_id, kind, op, base_seq)
SELECT @Id, @Kind, 'upsert', COALESCE((SELECT seq FROM sync_records WHERE record_id = @Id), 0)
WHERE (SELECT enabled FROM sync_state) = 1
ON CONFLICT (record_id) DO UPDATE
  SET op = excluded.op, base_seq = excluded.base_seq, revision = revision + 1;
```

- [ ] **Step 4: Abrir transação explícita** em `GoalRepository.AddAsync`, `GoalRepository.UpdateAsync` e `CheckinRepository.AddAsync`, como em `DeleteAsync`, chamando `EnqueueAsync` antes do `CommitAsync`. `UpdateAsync` lança `GoalNotFoundException` antes de enfileirar. O `catch` de chave estrangeira do registro continua valendo.

- [ ] **Step 5: Rodar todos os testes.** Expected: passam.

- [ ] **Step 6: Commit:** `feat(infra): grava dado e fila na mesma transação`.

---

### Task 3: Exclusão na fila

**Files:**
- Modify: `src/SlimeGoals.Infrastructure/Data/SyncOutbox.cs`
- Modify: `src/SlimeGoals.Infrastructure/Data/GoalRepository.cs` (`DeleteAsync`)
- Modify: `tests/SlimeGoals.Infrastructure.Tests/Data/SyncOutboxTests.cs`

**Interfaces:**
- Produces: `static Task EnqueueGoalDeletionAsync(SqliteConnection c, IDbTransaction tx, Guid goalId, CancellationToken ct)`.

- [ ] **Step 1: Escrever os testes:**
  - `Deleting_with_sync_off_touches_no_sync_table`: as 3 tabelas seguem vazias.
  - `Deleting_goal_the_server_knows_queues_delete_and_tombstone`: com linha em `sync_records`, a fila tem `op = 'delete'` com `base_seq` certo, e `sync_tombstones` tem o id.
  - `Deleting_goal_never_sent_only_leaves_the_queue`: meta sem `sync_records` some da fila; a marca é gravada.
  - `Deleting_goal_handles_each_checkin`: registro conhecido vira `delete`, registro nunca enviado sai da fila.
  - `Deleting_goal_purges_pending_checkins`: linha de `sync_pending` da meta some, e a de outra meta fica.
  - `Deleting_missing_goal_changes_no_sync_table`: lança `GoalNotFoundException`, e fila e marcas seguem iguais.

- [ ] **Step 2: Rodar.** Expected: falham.

- [ ] **Step 3: Implementar `EnqueueGoalDeletionAsync`,** chamado em `DeleteAsync` antes dos `DELETE`, enquanto os registros ainda existem. Com a sincronização desligada, retorna sem fazer nada. Ligada, nesta ordem:
  1. registros da meta com linha em `sync_records`: upsert com `op = 'delete'`, mesma cláusula `ON CONFLICT` da Tarefa 2;
  2. registros da meta sem essa linha: `DELETE FROM sync_outbox`;
  3. a meta: igual aos passos 1 e 2, com `kind = 'goal'`;
  4. `INSERT OR IGNORE INTO sync_tombstones`;
  5. `DELETE FROM sync_pending WHERE goal_id = @Id`.

- [ ] **Step 4: Rodar todos os testes.** Expected: passam, incluindo `GoalDeletionSqliteTests`.

- [ ] **Step 5: Commit:** `feat(infra): leva a exclusão de metas para a fila`.

---

### Task 4: Ligar e desligar

**Files:**
- Create: `src/SlimeGoals.Core/Abstractions/ISyncStateStore.cs`
- Create: `src/SlimeGoals.Infrastructure/Data/SyncStateRepository.cs`
- Modify: `src/SlimeGoals.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- Create: `tests/SlimeGoals.Infrastructure.Tests/Data/SyncStateRepositoryTests.cs`
- Modify: `tests/SlimeGoals.Infrastructure.Tests/InfrastructureServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Produces, em `SlimeGoals.Core.Abstractions`:

```csharp
public sealed record SyncState(bool Enabled, Guid? DeviceId, long LastSeq);

public interface ISyncStateStore
{
    Task<SyncState> GetAsync(CancellationToken ct = default);
    /// <summary>Liga e põe na fila tudo o que existe. Repetir não muda nada.</summary>
    Task EnableAsync(CancellationToken ct = default);
    /// <summary>Desliga e limpa fila e estado (RS07). Metas e registros ficam.</summary>
    Task DisableAsync(CancellationToken ct = default);
}
```

- [ ] **Step 1: Escrever os testes:**
  - `Starts_disabled`: `new SyncState(false, null, 0)`.
  - `Enable_queues_goals_before_checkins`: com 2 metas e 3 registros, 5 linhas, e as metas têm as menores `position`.
  - `Enable_twice_changes_nothing`: mesmo `DeviceId`, 5 linhas, `revision = 1`.
  - `Enable_keeps_invalid_rows_from_stopping_it`: meta com `color` inválida também entra na fila, sem exceção.
  - `Disable_clears_sync_tables_and_keeps_data`: preenche as 4 tabelas e `last_seq = 9`; depois de desligar, as 4 ficam vazias, `LastSeq = 0`, e metas e registros seguem lá.
  - `Enable_after_disable_keeps_device_id`.
  - `Writes_after_disable_are_not_queued`.
  - DI: `ISyncStateStore` resolve para `SyncStateRepository`.

- [ ] **Step 2: Rodar.** Expected: falha de compilação.

- [ ] **Step 3: Implementar `SyncStateRepository`,** tudo em uma transação por operação. `EnableAsync` sai sem fazer nada se já ligado; grava `device_id = COALESCE(device_id, @New)` com `Guid.NewGuid()`; enfileira por SQL direto, sem passar pelo modelo, metas por `created_at` e depois registros por `done_at`. Registrar como singleton.

- [ ] **Step 4: Rodar todos os testes.** Expected: passam.

- [ ] **Step 5: Commit:** `feat(infra): liga e desliga a fila de sincronização`.

---

### Task 5: Data de edição e limite dos totais na leitura

**Files:**
- Create: `src/SlimeGoals.Core/Domain/GoalTotals.cs`
- Modify: `src/SlimeGoals.Core/Services/GoalService.cs` (`UpdateAsync`, `ArchiveAsync`, `UnarchiveAsync`, `ArchiveExpiredAsync`)
- Modify: `src/SlimeGoals.Core/Services/ProgressService.cs:44`
- Modify: `src/SlimeGoals.Core/Domain/GoalHistory.cs` (os dois `done`)
- Modify: `src/SlimeGoals.Core/Services/ReportService.cs` (`MonthRow`, `Row`, `DayEntries`)
- Modify: `src/SlimeGoals.Core/Services/ReminderService.cs` (`Snapshot.PeriodTotal`)
- Create: `tests/SlimeGoals.Core.Tests/Domain/GoalTotalsTests.cs`
- Modify: `tests/SlimeGoals.Core.Tests/Services/GoalServiceStateTests.cs`, `GoalServiceTests.cs`, `ProgressServiceTests.cs`, `ReportServiceWeekTests.cs`, `ReportServiceMonthTests.cs`

**Interfaces:**
- Produces: `public static class GoalTotals { public static int Effective(Goal goal, int total); }`. Conclusão: `Math.Clamp(total, 0, 1)`. Demais: `Math.Max(0, total)`.
- Produces: `private DateTimeOffset NextUpdatedAt(Goal current)` em `GoalService`: o maior entre `time.GetUtcNow()` e `current.UpdatedAt.AddMilliseconds(1)`.

- [ ] **Step 1: Escrever os testes.** O `FakeTimeProvider` não volta no tempo: para simular relógio atrasado, grave a meta com `UpdatedAt = T0.AddMinutes(10)` em `_goals.UpdateAsync`.
  - `GoalTotalsTests`, em `[Theory]`: conclusão `(-1→0, 0→0, 1→1, 2→1)`; quantidade com alvo 8 `(-3→0, 5→5, 12→12)`; duração `(-1→0)`.
  - `Edit_with_late_clock_beats_the_known_date`: depois de editar, `UpdatedAt == T0.AddMinutes(10).AddMilliseconds(1)`. Repetir em `ArchiveAsync`, `UnarchiveAsync` e `ArchiveExpiredAsync`.
  - `Archive_with_late_clock_keeps_real_archive_date`: `ArchivedAt == T0`.
  - `Edit_with_correct_clock_uses_now`: `UpdatedAt == time.GetUtcNow()`.
  - `Today_limits_totals`: conclusão com dois +1 tem `Done == 1`; quantidade com soma −2 tem `Done == 0` e `IsMet == false`.
  - `Week_row_limits_negative_weekly_total`: semanal com soma −2 tem `Done == 0` e `Percent == 0`.
  - `Month_week_percent_is_never_negative`: semanal com soma −2 no mês dá 0, não valor negativo.
  - `Daily_negative_day_does_not_lower_week_done`: diária com −2 na segunda e +3 na terça tem `Done == 3`.
  - `Weekly_negative_day_is_not_limited`: semanal com +5 na segunda e −2 na terça tem `Done == 3`.

- [ ] **Step 2: Rodar.** Expected: falha de compilação.

- [ ] **Step 3: Implementar `GoalTotals` e `NextUpdatedAt`.** Nas quatro gravações de `GoalService`, só `UpdatedAt` usa `NextUpdatedAt`; `ArchivedAt` continua com a hora do relógio.

- [ ] **Step 4: Aplicar `GoalTotals.Effective` só onde há total de período:**
  - `ProgressService`: `done` da lista de hoje. `WeekFraction` já limita.
  - `GoalHistory.Periods`: `done` diário e semanal.
  - `ReportService`: `done` de `MonthRow`, `weekDone` de `Row`, `Done` de `DayEntries`. Em `Row`, o laço de `bestSum` continua usando a soma do dia.
  - `ReminderService.Snapshot.PeriodTotal`.

  `DailySums` não muda.

- [ ] **Step 5: Rodar todos os testes.** Expected: passam.

- [ ] **Step 6: Commit:** dois commits, `feat(core): edição depois de ver outra sempre vence` e `feat(core): limita os totais na leitura`.

---

### Task 6: Compensação ao registrar

**Files:**
- Modify: `src/SlimeGoals.Core/Services/CheckinService.cs` (`AdjustAsync`, `PeriodTotalAsync`)
- Modify: `tests/SlimeGoals.Core.Tests/Services/CheckinServiceTests.cs`

**Interfaces:**
- Consumes: `GoalTotals.Effective`.

- [ ] **Step 1: Escrever os testes,** criando o estado com `_checkins.AddAsync` direto:
  - `Unmark_with_total_2_goes_back_to_zero_in_one_step`: `AdjustAsync(id, -1)` devolve 0, grava `Amount == -2`, e os registros antigos seguem iguais.
  - `Mark_with_negative_total_reaches_one`: total −1, `AdjustAsync(id, 1)` devolve 1 e grava `Amount == 2`.
  - `Quantity_with_negative_total_counts_from_zero`: total −3, `AdjustAsync(id, 2)` devolve 2 e grava `Amount == 5`.
  - `Removing_from_zero_limited_total_is_refused`: total −1, `AdjustAsync(id, -1)` lança `GoalStateException`.
  - `Marking_again_with_total_2_is_refused`: total 2, `AdjustAsync(id, 1)` lança `GoalStateException`.
  - `Normal_total_records_the_amount_as_is`: total 3, `AdjustAsync(id, 2)` grava `Amount == 2`.
  - `Period_total_is_limited`: total −1, `PeriodTotalAsync` devolve 0.

- [ ] **Step 2: Rodar.** Expected: falham.

- [ ] **Step 3: Implementar em `AdjustAsync`:**

```csharp
var raw = await TotalAsync(goal, today, ct);
var total = GoalTotals.Effective(goal, raw) + amount;
// as duas validações atuais olham `total`
// Amount = total - raw: sem sincronização raw e o total limitado são iguais, e o valor gravado é `amount`.
```

  O limite de `MaxAmount` continua valendo para o valor pedido. Atualizar o `<summary>` da classe. `PeriodTotalAsync` devolve `GoalTotals.Effective`.

- [ ] **Step 4: Rodar todos os testes.** Expected: passam.

- [ ] **Step 5: Commit:** `feat(core): compensa o total limitado ao registrar`.

---

### Task 7: Spec e verificação final

**Files:**
- Modify: `specs/Sincronizacao_v1.md`

- [ ] **Step 1: Atualizar a spec:**
  - §3, "Totais depois de juntar": trocar a última frase por "A validação de `CheckinService` parte do total limitado, e o valor gravado é a diferença até o total real, para que um toque tenha o efeito visível. Sem sincronização, os dois totais são iguais.";
  - §4, `sync_outbox`: acrescentar `revision`, que muda a cada substituição e impede que um envio em andamento tire da fila uma edição mais nova; dizer que a substituição mantém a posição;
  - §4: registro que o servidor nunca viu sai da fila ao ser excluído;
  - §10, linha 9.1: acrescentar "data de edição".
  - §11: acrescentar "4. Fila no servidor (broker e consumidor) para muitos usuários: fica para depois da funcionalidade combinada.".

- [ ] **Step 2: Rodar a verificação das Global Constraints.** Expected: testes passam; build do Android com 0 avisos e 0 erros.

- [ ] **Step 3: Conferir à mão num banco temporário,** com `XDG_DATA_HOME` e `XDG_RUNTIME_DIR` no scratchpad: abrir o app, criar meta, registrar e excluir. Depois, em `slimegoals.db`: `PRAGMA user_version` dá 5, `SELECT enabled FROM sync_state` dá 0, e `sync_outbox` tem 0 linhas.

- [ ] **Step 4: Commit:** `docs(specs): registra as decisões da fase 9.1`.
