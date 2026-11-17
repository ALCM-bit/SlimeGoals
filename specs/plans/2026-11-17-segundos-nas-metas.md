# Segundos nas metas de tempo — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** metas de tempo passam a guardar e mostrar segundos inteiros, sem arredondar trechos ao minuto.

**Architecture:** `Measure.Duration` passa a valer segundos em `Checkin.Amount`, `Goal.Target` e nos trechos do Pomodoro. A migração 009 converte o banco. O formato de sincronização sobe para 2, e o leitor converte registros do formato 1. O App converte horas e minutos na borda e formata tudo por `GoalFormatting.Duration(int seconds)`.

**Tech Stack:** .NET 10, Avalonia 12 + CommunityToolkit.Mvvm, SQLite (Microsoft.Data.Sqlite + Dapper), xUnit v3 (MTP) + AwesomeAssertions + FakeTimeProvider.

**Spec:** `specs/Segundos_nas_metas_de_tempo_v1.md` (aprovada em 17/11/2026).

## Global Constraints

- `TreatWarningsAsErrors`: um warning quebra o build. TDD: comportamento novo começa por um teste que falha.
- Conventional Commits em português com escopo por camada (`core`, `infra`, `app`, `specs`, `plans`, `build`), terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Testes: `dotnet test --project <proj> -- --filter-class "*Nome"`; suíte inteira `dotnet test --solution SlimeGoals.slnx` (Docker precisa estar rodando).
- Rodar o app só com `XDG_DATA_HOME=/tmp/slimegoals-dev/data XDG_CONFIG_HOME=/tmp/slimegoals-dev/config`. Nunca abrir `~/.local/share/SlimeGoals/`.
- Unidade: metas de tempo em **segundos inteiros**; conclusão e quantidade não mudam. Blocos do Pomodoro (`FocusBlock.Minutes`) continuam em minutos.
- Limites: `CheckinService.MaxDurationAmount = 24 * 3600`; alvo de 60 s a 86 400 s (diária) ou 604 800 s (semanal), mensagens inalteradas ("O tempo deve ser entre 1 min e 24 h." / "… 168 h.").
- Lançar à mão (painel do Hoje), alvo da meta e blocos do Pomodoro continuam em h e min; o App multiplica por 60 ao gravar.
- Os três editores de valor medido (ajuste de trecho de hoje, conferência do cronômetro parado, conferência do Pomodoro) ganham segundos: campos h · min · s, `s` de 0 a 59.
- Formato: `GoalFormatting.Duration(int seconds)` → 0 "0 min"; 40 "40 s"; 160 "2 min 40 s"; 3600 "1 h"; 4360 "1 h 12 min 40 s"; 3605 "1 h 5 s"; 9000 "2 h 30 min"; negativo com "-" na frente.
- "+N" do excesso do Pomodoro aparece a partir de 1 s ("+1 s"); abaixo de 1 s, escondido.
- Sincronização: `SyncCodec.FormatVersion = 2`; o leitor aceita 1 e 2; formato 1 = minutos, convertido ×60 ao aplicar (meta de tempo: alvo; registro de meta de tempo: valor). O servidor não muda.
- Repositório público: nada de dado pessoal ou caminho privado em commits.
- Entre as tarefas 1 e 5, partes do App ainda mostram valores com o formato antigo; cada tarefa diz o que converte. A tarefa 5 fecha a exibição.

## Review Focus

1. **Atualizar com dados mistos:** só metas `duration` e seus registros (inclusive apagados e de compensação) são multiplicados; quantidade e conclusão ficam iguais. Teste na Tarefa 3.
2. **Registro do formato 1 chegando depois da atualização,** inclusive antes da meta (fica em `sync_pending`): entra convertido, e um do formato 2 não é convertido. Teste na Tarefa 4.
3. **Abrir e salvar um editor sem mexer** num trecho de 2 min 40 s mantém 160 s; hoje cairia para 2 ou 3 min. Teste na Tarefa 6.
4. **Sessão de Pomodoro aberta na hora da atualização,** com trechos guardados: os trechos viram segundos (25 → 1500) e a sessão continua legível. Teste na Tarefa 3.
5. **Textos longos a 320 px** ("1 h 12 min 40 s de 2 h 30 min hoje" no card do Hoje e no relatório) quebram linha sem cortar. Conferência na Render, Tarefas 5 e 6.

---

### Task 1: Core e bordas do App em segundos (registros e alvo)

**Files:**
- Modify: `src/SlimeGoals.Core/Services/CheckinService.cs` (`MaxDurationAmount`, `ReplaceAsync`)
- Modify: `src/SlimeGoals.Core/Validation/GoalValidator.cs` (`MaxDailyMinutes`/`MaxWeeklyMinutes` → `MaxDailySeconds = 24 * 3600`, `MaxWeeklySeconds = 7 * 24 * 3600`, mínimo 60)
- Modify: `src/SlimeGoals.Core/Domain/Measure.cs`, `src/SlimeGoals.Core/Domain/Checkin.cs` (comentários: "segundos no tempo")
- Modify: `src/SlimeGoals.App/ViewModels/GoalEditViewModel.cs` (alvo h/min ↔ segundos), `src/SlimeGoals.App/ViewModels/TodayGoalViewModel.cs` (`PanelAmount` em segundos), `src/SlimeGoals.App/ViewModels/TodayChunkViewModel.cs` + `TodayPageViewModel.cs:478-483` (o ajuste passa `Minutes() * 60` até a Tarefa 6)
- Test: `tests/SlimeGoals.Core.Tests/Services/CheckinServiceTests.cs`, `tests/SlimeGoals.Core.Tests/Validation/*`, `tests/SlimeGoals.App.Tests/ViewModels/GoalEditViewModelTests.cs`, `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**
- Produces: `CheckinService.ReplaceAsync(Guid goalId, Guid checkinId, int seconds, string? note, CancellationToken ct = default)` — recusa `seconds < 1 || seconds > MaxDurationAmount` com "Informe de 1 s a 24 h."; `GoalValidator.MaxDailySeconds`, `GoalValidator.MaxWeeklySeconds`; `TodayGoalViewModel.PanelAmount()` devolve segundos com sinal.

- [ ] **Step 1: testes que falham**
  - `CheckinServiceTests.Duration_accepts_up_to_24_hours_in_seconds`: `AdjustAsync(goal, 86_400)` passa; `AdjustAsync(goal, 86_401)` lança `GoalValidationException` com "Valor fora do limite.".
  - `CheckinServiceTests.Replace_accepts_seconds`: trecho de 120 → `ReplaceAsync(..., 160, ...)` deixa o registro com `Amount == 160`; `ReplaceAsync(..., 0, ...)` lança com "Informe de 1 s a 24 h.".
  - Validador: alvo de tempo diário 59 recusado, 60 aceito, 86 400 aceito, 86 401 recusado com "O tempo deve ser entre 1 min e 24 h."; semanal 604 800 aceito, 604 801 recusado com "… 168 h.".
  - `GoalEditViewModelTests.Duration_target_is_saved_in_seconds`: 1 h 30 min no formulário grava `Target == 5400`; abrir uma meta com `Target == 5400` mostra 1 h e 30 min.
  - `TodayPageViewModelTests.Panel_adds_minutes_as_seconds`: painel com 0 h 25 min soma `1500` no registro.
  - Atualize os testes existentes que usam alvos ou valores de tempo em minutos.
- [ ] **Step 2:** `dotnet test --project tests/SlimeGoals.Core.Tests -- --filter-class "*CheckinServiceTests"` e as classes acima. Esperado: FAIL nos novos.
- [ ] **Step 3:** implemente as mudanças da lista de arquivos.
- [ ] **Step 4:** mesmos comandos. Esperado: PASS. Depois, `dotnet test --solution SlimeGoals.slnx`: tudo verde.
- [ ] **Step 5:** commit `feat(core): metas de tempo contam em segundos`.

### Task 2: Cronômetro e Pomodoro cortam em segundos

**Files:**
- Modify: `src/SlimeGoals.Core/Services/TimerService.cs` (`Minutes` → `Seconds`, `StopAsync`, `SwitchTaskAsync`, `RegisterAsync`)
- Modify: `src/SlimeGoals.Core/Domain/FocusClock.cs` (`CutMinutes` → `CutSeconds`), `src/SlimeGoals.Core/Domain/FocusSession.cs` (`FocusChunk.Minutes` → `Seconds`), `src/SlimeGoals.Core/Services/FocusService.cs` (`reviewedMinutes` → `reviewedSeconds`, `FocusSummary.Seconds`, `FocusSummaryLine.Seconds`), `src/SlimeGoals.Infrastructure/Data/FocusSessionRepository.cs` (só o nome da propriedade; a coluna muda na Tarefa 3)
- Modify (só para compilar com a unidade certa): `TodayGoalViewModel.cs` (cronômetro parado: h/min a partir de segundos, registra `StoppedAmount() * 60` até a Tarefa 6), `TodayPageViewModel.cs:591`, `PomodoroViewModel.Running.cs` (conferência: `reviewedSeconds = (h*60+m)*60` até a Tarefa 6), `PomodoroViewModel.End.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/TimerServiceTests.cs`, `tests/SlimeGoals.Core.Tests/Domain/FocusClockTests.cs`, `tests/SlimeGoals.Core.Tests/Services/FocusServiceTests.cs`

**Interfaces:**
- Consumes: Tarefa 1 (`MaxDurationAmount` em segundos).
- Produces: `TimerService.Seconds(ActiveTimer timer, DateTimeOffset now) : int` (segundos inteiros, truncando); `Task<int> TimerService.StopAsync` devolve segundos; `RegisterAsync(int seconds, …)`; `FocusClock.CutSeconds(TimeSpan open, bool last) : int` (trunca; no último, arredonda ao segundo mais próximo, `AwayFromZero`); `FocusChunk(Guid Id, int Seconds, string? Task, DateTimeOffset EndedAt, Guid? CheckinId)`; `FocusService.AdvanceAsync(int? reviewedSeconds = null, …)`, `EndAsync(int? reviewedSeconds = null, …)`; `FocusSummary(int Seconds, IReadOnlyList<FocusSummaryLine> Lines, bool Assigned)`; `FocusSummaryLine(string? Task, int Seconds)`.

- [ ] **Step 1: testes que falham**
  - `TimerServiceTests.Switch_after_40_seconds_records_a_40_second_chunk`: início em T, troca em T+40,4 s → registro de 40 com a nota da tarefa antiga; novo início = T+40 s.
  - `TimerServiceTests.Switch_under_one_second_only_renames`: troca em T+0,6 s → nenhum registro; o nome muda.
  - `TimerServiceTests.Stop_returns_whole_seconds`: parar em T+2 min 40,9 s → `StopAsync` devolve 160.
  - `FocusClockTests.CutSeconds_truncates_mid_session_and_rounds_last`: `CutSeconds(00:02:40.6, false) == 160`; `CutSeconds(00:02:40.6, true) == 161`; `CutSeconds(00:02:40.4, true) == 160`.
  - `FocusServiceTests.Task_switch_records_seconds_and_carries_only_the_fraction`: foco com meta, troca de subtarefa a 2 min 40,5 s → registro de 160; `Carry == 500 ms`.
  - `FocusServiceTests.Reviewed_time_is_in_seconds`: conferência com `reviewedSeconds: 3725` grava 3725.
  - Atualize os testes existentes que esperavam minutos.
- [ ] **Step 2:** rode as três classes. Esperado: FAIL nos novos.
- [ ] **Step 3:** implemente. `Carry` passa a guardar só a fração de segundo do corte truncado.
- [ ] **Step 4:** classes PASS; suíte inteira verde.
- [ ] **Step 5:** commit `feat(core): cronômetro e Pomodoro registram segundos`.

### Task 3: Migração 009

**Files:**
- Create: `src/SlimeGoals.Infrastructure/Data/Migrations/009_seconds.sql`
- Modify: `src/SlimeGoals.Infrastructure/Data/FocusSessionRepository.cs` (coluna `seconds`)
- Test: `tests/SlimeGoals.Infrastructure.Tests/Data/Migration009Tests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Data/FocusSessionRepositoryTests.cs`

**Interfaces:**
- Produces: `focus_chunk.seconds` (no lugar de `minutes`); `sync_pending.format_version INTEGER NOT NULL DEFAULT 1` (usada na Tarefa 4). `MigrationRunner.Migrate(c)` passa a devolver 9.

- [ ] **Step 1: testes que falham** (no estilo de `Migration007Tests`, `MigrationRunner.Migrate(c, upTo: 8)` e depois `Migrate(c).Should().Be(9)`)
  - `Duration_goals_and_their_checkins_become_seconds`: meta de tempo alvo 90 com registros 25, −5, um apagado (15) e um de compensação (10) → alvo 5400, registros 1500, −300, 900, 600. `updated_at` de todas as linhas inalterado.
  - `Quantity_and_completion_are_untouched`: meta de quantidade alvo 8 com registro 3, e de conclusão com registro 1 → iguais.
  - `Held_focus_chunks_become_seconds`: sessão aberta sem meta com trecho de 25 → `FocusSessionRepository` lê o trecho com `Seconds == 1500` e a sessão inteira.
  - `Pending_sync_rows_are_format_1`: linha existente em `sync_pending` → `format_version == 1`.
  - `Nothing_enters_the_outbox`: `sync_outbox` vazio antes, vazio depois.
- [ ] **Step 2:** `dotnet test --project tests/SlimeGoals.Infrastructure.Tests -- --filter-class "*Migration009Tests"`. Esperado: FAIL.
- [ ] **Step 3:** escreva a migração numa transação (o `MigrationRunner` já roda cada arquivo assim; confira) e ajuste o repositório. Renomeie com `ALTER TABLE focus_chunk RENAME COLUMN minutes TO seconds`.
- [ ] **Step 4:** classe PASS; `FocusSessionRepositoryTests` PASS; suíte inteira verde.
- [ ] **Step 5:** commit `feat(infra): migração 009 converte o tempo para segundos`.

### Task 4: Sincronização no formato 2

**Files:**
- Modify: `src/SlimeGoals.Core/Sync/SyncCodec.cs` (`FormatVersion = 2`, `MinReadableVersion = 1`, `DecodeGoal(string json, int version)` converte alvo de tempo ×60 no formato 1, `static Checkin FromFormat1Duration(Checkin c)` com `Amount * 60`)
- Modify: `src/SlimeGoals.Sync/Crypto/AesGcmSyncCrypto.cs` (aceita versão do envelope de `MinReadableVersion` a `FormatVersion`; fora disso, `SyncFormatException`; o byte de versão do blob não muda)
- Modify: `src/SlimeGoals.Infrastructure/Data/SyncApply.cs` (meta: decodifica com `envelope.Version`; registro do formato 1 de meta `duration` → `FromFormat1Duration`; sem meta, grava `format_version` em `sync_pending` e converte em `ApplyPendingAsync` conforme a meta que chegou)
- Test: `tests/SlimeGoals.Core.Tests/Sync/SyncCodecTests.cs`, `tests/SlimeGoals.Sync.Tests/Crypto/AesGcmSyncCryptoTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Data/SyncApplyTests.cs`

**Interfaces:**
- Consumes: Tarefa 3 (`sync_pending.format_version`).
- Produces: `SyncCodec.FormatVersion == 2`, `SyncCodec.MinReadableVersion == 1`, `SyncCodec.DecodeGoal(string json, int version)`, `SyncCodec.FromFormat1Duration(Checkin checkin)`.

- [ ] **Step 1: testes que falham**
  - `SyncCodecTests.Format_1_duration_goal_target_becomes_seconds`: alvo 90, medida `Duration`, versão 1 → 5400; versão 2 → 90; quantidade versão 1 → igual.
  - `AesGcmSyncCryptoTests.Opens_format_1_and_2_and_rejects_3`: envelopes 1 e 2 abrem com a versão certa; 3 lança `SyncFormatException`.
  - `SyncApplyTests.Format_1_checkin_of_a_duration_goal_becomes_seconds`: meta de tempo local; registro v1 de 25 → 1500; v2 de 25 → 25; v1 de meta de quantidade 3 → 3.
  - `SyncApplyTests.Format_1_checkin_waiting_for_its_goal_is_converted_when_the_goal_arrives`: registro v1 de 25 numa página antes da meta v1 (tempo, alvo 60) → depois da meta, registro 1500 e alvo 3600.
  - `SyncApplyTests.Format_2_checkin_waiting_for_its_goal_is_not_converted`: mesmo caminho em v2 → 25.
  - Um teste do motor confere que o envio sai com envelope versão 2.
- [ ] **Step 2:** rode as três classes. Esperado: FAIL.
- [ ] **Step 3:** implemente.
- [ ] **Step 4:** classes PASS; suíte inteira verde (inclui `ConvergenceTests` e os testes do servidor).
- [ ] **Step 5:** commit `feat(core): sincronização no formato 2, com leitura do formato 1`.

### Task 5: Exibição com segundos

**Files:**
- Modify: `src/SlimeGoals.App/Goals/GoalFormatting.cs` (`Duration(int seconds)`)
- Modify (passam segundos): `TodayGoalViewModel.cs` (`ChunksHeader`), `TodayChunkViewModel.cs` (texto do trecho), `TodayPageViewModel.cs:591`, `ReportFormatting.cs`, `PomodoroViewModel.cs:92-93` (`Começar · Duration(totalMinutes * 60)`), `PomodoroViewModel.End.cs` (resumo e linhas), `PomodoroViewModel.Running.cs` (`OvertimeText` a partir de 1 s), `FocusBlockViewModel.cs` (`Duration(minutes * 60)`)
- Modify: `tools/SlimeGoals.Render/Sample.cs` (amostras de tempo em segundos, com pelo menos um progresso "1 h 12 min 40 s de 2 h 30 min" e um trecho "2 min 40 s")
- Test: `tests/SlimeGoals.App.Tests/Goals/GoalFormattingTests.cs`, `ReportFormattingTests.cs`, `tests/SlimeGoals.App.Tests/ViewModels/PomodoroViewModelTests.cs`, `TodayPageViewModelTests.cs`

**Interfaces:**
- Produces: `GoalFormatting.Duration(int seconds) : string` com o formato das Global Constraints.

- [ ] **Step 1: testes que falham**
  - `GoalFormattingTests.Duration_shows_hours_minutes_and_seconds`: os oito exemplos das Global Constraints, mais −160 → "-2 min 40 s".
  - `PomodoroViewModelTests.Overtime_shows_from_the_first_second`: 0,5 s de excesso → `OvertimeText` nulo; 1 s → "+1 s"; 65 s → "+1 min 5 s".
  - `PomodoroViewModelTests.Start_button_shows_minutes_label`: plano de 2 h 30 → "Começar · 2 h 30 min".
  - Atualize os textos esperados nos testes existentes ("2 h 30" → "2 h 30 min", e valores em segundos).
- [ ] **Step 2:** rode as classes. Esperado: FAIL.
- [ ] **Step 3:** implemente.
- [ ] **Step 4:** classes PASS; suíte inteira verde.
- [ ] **Step 5:** `dotnet run --project tools/SlimeGoals.Render -- <pasta> all` e `… time`; leia os PNGs do Hoje, Relatórios e aba Tempo em 320, 360, 412 e 1100 px. Esperado: nada cortado; textos longos quebram linha.
- [ ] **Step 6:** commit `feat(app): tempo das metas com segundos em todas as telas`.

### Task 6: Editores com segundos

**Files:**
- Modify: `src/SlimeGoals.App/ViewModels/TodayChunkViewModel.cs` (`SecondsInput`; `Minutes()` → `Seconds()`), `TodayGoalViewModel.cs` (`StoppedSecondsInput`; `StoppedAmount()` em segundos), `PomodoroViewModel.Running.cs` (`ReviewSecondsInput`; conferência em segundos), `TodayPageViewModel.cs` (sem os `* 60` provisórios das Tarefas 1 e 2)
- Modify: as views desses três editores (`TodayView.axaml`, `PomodoroView.axaml`): terceiro `NumericUpDown` com rótulo "s", `Minimum="0" Maximum="59"`, no mesmo padrão dos de h e min, com as mesmas quebras de linha a 320/360 px
- Modify: `tools/SlimeGoals.Render/Program.cs`, se as cenas de edição precisarem de valor com segundos
- Test: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`, `PomodoroViewModelTests.cs`

**Interfaces:**
- Consumes: `CheckinService.ReplaceAsync(…, int seconds, …)`, `TimerService.RegisterAsync(int seconds, …)`, `FocusService.AdvanceAsync/EndAsync(int? reviewedSeconds)`.
- Produces: `TodayChunkViewModel.Seconds() : int?`, `TodayGoalViewModel.StoppedAmount() : int?` (segundos), propriedades `SecondsInput`, `StoppedSecondsInput`, `ReviewSecondsInput` (`decimal?`).

- [ ] **Step 1: testes que falham**
  - `TodayPageViewModelTests.Chunk_editor_saved_unchanged_keeps_exact_seconds`: trecho de 160 → abrir mostra 0 h, 2 min, 40 s; salvar sem mexer mantém 160 (nenhuma troca).
  - `TodayPageViewModelTests.Chunk_editor_saves_seconds`: mudar para 3 min 5 s → registro 185.
  - `TodayPageViewModelTests.Stopped_timer_review_shows_and_registers_seconds`: cronômetro parado em 2 min 40 s → campos 0/2/40; registrar → 160.
  - `PomodoroViewModelTests.Review_fields_include_seconds`: conferência aberta com excesso → campos com segundos; confirmar 1 h 2 min 5 s → `reviewedSeconds == 3725`.
  - Só segundos preenchidos (h e min vazios) vale como valor; tudo vazio → "Informe um valor.".
- [ ] **Step 2:** rode as classes. Esperado: FAIL.
- [ ] **Step 3:** implemente os VMs e as views.
- [ ] **Step 4:** classes PASS; suíte inteira verde.
- [ ] **Step 5:** Render (`all` e `time`): leia as cenas de edição de trecho, cronômetro parado e conferência do Pomodoro em 320, 360, 412 e 1100 px. Esperado: três campos alinhados, nada cortado a 320.
- [ ] **Step 6:** commit `feat(app): editores de tempo medido com segundos`.

### Task 7: Verificação e spec

**Files:**
- Modify: `specs/Segundos_nas_metas_de_tempo_v1.md` (§6 Verificação)

- [ ] **Step 1:** `dotnet test --solution SlimeGoals.slnx` → 0 falhas, 0 ignorados; `dotnet build SlimeGoals.slnx -c Release` → 0 avisos; build do Android (`dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`) → 0 erros (só XA0033).
- [ ] **Step 2:** app de desenvolvimento com dados isolados: crie uma meta de tempo, rode o cronômetro 40 s com uma subtarefa, troque, pare; confira no Hoje "40 s" e o total com segundos.
- [ ] **Step 3:** preencha a §6 com o que foi conferido e o que falta o autor ver (celular com dados reais migrados; sincronização entre aparelhos na mesma versão).
- [ ] **Step 4:** commit `docs(specs): verificação dos segundos nas metas de tempo`.
