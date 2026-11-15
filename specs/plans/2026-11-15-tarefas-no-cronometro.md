# Tarefas no cronômetro — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** dar um nome opcional ao trabalho do cronômetro de uma meta de tempo, mostrá-lo dentro e fora do app e trocar de tarefa sem parar, com o tempo de cada tarefa somando na meta.

**Architecture:**
- O nome da tarefa é a nota do registro (`Checkin.Note`, já sincronizada).
- O cronômetro local (`active_timer`) ganha a coluna `task`.
- Trocar de tarefa registra o trecho e recomeça o cronômetro numa transação só, no repositório do cronômetro.
- A tela Hoje ganha o campo da tarefa no cartão rodando. A janelinha e a notificação mostram "Meta · Tarefa".

**Tech Stack:** .NET 10, Avalonia 12 + CommunityToolkit.Mvvm, SQLite (Microsoft.Data.Sqlite + Dapper), xUnit v3 (MTP), AwesomeAssertions/FluentAssertions como nos testes atuais.

**Spec:** `specs/Tarefas_no_cronometro_v1.md`

## Global Constraints

- `TreatWarningsAsErrors`: nenhum warning.
- `Trace` nunca grava a mensagem de uma exceção; use `TraceText.Of(e)` ou `TraceText.Types(e)`.
- Use `System.Threading.Lock`, e não `object`, em travas novas.
- Testes nunca tocam o banco real, o chaveiro nem o app instalado.
- Textos, exatamente assim:
  - campo: `No que está trabalhando?`
  - botões do modo de troca: `Trocar` e `Cancelar`
  - erro de tamanho: `A tarefa deve ter até 200 caracteres.`
  - cronômetro sumido: `Nenhum cronômetro rodando.`
  - cronômetro mudado durante a troca: `O cronômetro mudou. Confira em Hoje.`
- Limite do nome: `CheckinService.MaxNoteLength` (200). O nome passa por trim, e vazio ou só espaços vira `null`.
- Sugestões: até 5 (`CheckinService.RecentNotesLimit = 5`) notas distintas dos registros não excluídos da meta, da mais recente para a mais antiga.
- Rótulo fora do app: `Title` sem tarefa, ou `$"{Title} · {Task}"` com tarefa (ponto médio U+00B7 entre espaços).
- O cronômetro continua local: nada de `active_timer` entra na sincronização.
- Commits em Conventional Commits pt-BR, com escopo `core`, `infra`, `app`, `desktop`, `android` ou `specs`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A tela Hoje recarrega enquanto a pessoa digita a tarefa.** Uma sincronização, outro processo ou o cronômetro mudando recriam os cartões (`Goals.Clear()`). O texto digitado e o modo de troca precisam sobreviver ao recarregamento, se o cronômetro da mesma meta continua rodando. Teste na Task 4.
2. **Trocar de tarefa com o cronômetro parado ou descartado por outro processo.** Por exemplo, o "Parar" da notificação ou da janelinha. Nenhum registro é criado, o cronômetro não ressuscita, e a pessoa vê `O cronômetro mudou. Confira em Hoje.`. Testes nas Tasks 1 e 2.
3. **Trecho que a validação do registro recusa.** Por exemplo, mais de 24 h (`MaxDurationAmount`) ou meta fora do dia depois da meia-noite. A troca falha antes de gravar qualquer coisa, e o cronômetro fica como estava. Teste na Task 1.
4. **Cronômetro iniciado pelo "Iniciar" do lembrete.** Ele começa sem tarefa, e o campo aparece vazio e funciona. Teste na Task 4.
5. **Nome com só espaços, com 201 caracteres, com emoji, ou muito longo.** Respectivamente: vira `null`; dá o erro de tamanho; é aceito; e aparece cortado com reticências na janelinha e na notificação. Testes nas Tasks 1 e 3.

---

### Task 1: Core — tarefa no cronômetro, troca sem perder tempo

**Files:**
- Modify: `src/SlimeGoals.Core/Domain/ActiveTimer.cs`
- Modify: `src/SlimeGoals.Core/Abstractions/ITimerRepository.cs`
- Modify: `src/SlimeGoals.Core/Abstractions/ICheckinRepository.cs`
- Modify: `src/SlimeGoals.Core/Services/CheckinService.cs`
- Modify: `src/SlimeGoals.Core/Services/TimerService.cs`
- Modify: `tests/SlimeGoals.Testing/InMemoryTimerRepository.cs`
- Modify: `tests/SlimeGoals.Testing/InMemoryCheckinRepository.cs`
- Modify: `specs/Tarefas_no_cronometro_v1.md` (item 5 da seção 4; cabeçalho "Aprovado pelo autor em 15/11/2026")
- Test: `tests/SlimeGoals.Core.Tests/Services/TimerServiceTests.cs`, `tests/SlimeGoals.Core.Tests/Services/CheckinServiceTests.cs`

**Interfaces:**
- Produces:
  - `record ActiveTimer(Guid GoalId, DateTimeOffset StartedAt, DateTimeOffset? StoppedAt = null, string? Task = null)`.
  - `ITimerRepository.SetTaskAsync(string? task, CancellationToken ct = default) : Task<bool>`. Devolve falso quando não há cronômetro.
  - `ITimerRepository.SwitchAsync(DateTimeOffset expectedStartedAt, ActiveTimer next, Checkin chunk, CancellationToken ct = default) : Task<bool>`. Numa transação só: atualiza o cronômetro, mas só se ele ainda é o da mesma meta, com o mesmo início e rodando; grava o trecho, incluindo a fila da sincronização. Devolve falso, sem gravar nada, se o cronômetro mudou.
  - `ICheckinRepository.RecentNotesAsync(Guid goalId, int limit, CancellationToken ct = default) : Task<IReadOnlyList<string>>`.
  - `CheckinService.RecentNotesLimit = 5`.
  - `CheckinService.RecentNotesAsync(Guid goalId, CancellationToken ct = default) : Task<IReadOnlyList<string>>`.
  - `internal CheckinService.PrepareAsync(Guid goalId, int amount, string? note, CancellationToken ct) : Task<(Checkin Checkin, int Total)>`. Faz toda a validação e a montagem que `AdjustAsync` faz hoje, sem gravar. `AdjustAsync` passa a ser `PrepareAsync`, depois `AddAsync`, depois `NotifyChanged`.
  - `TimerService.SetTaskAsync(string? task, CancellationToken ct = default) : Task`.
  - `TimerService.SwitchTaskAsync(string? newTask, CancellationToken ct = default) : Task<int>`. Devolve o novo total do período.
  - `TimerService.TaskSuggestionsAsync(Guid goalId, CancellationToken ct = default) : Task<IReadOnlyList<string>>`. Delega para `CheckinService.RecentNotesAsync`.
  - `InMemoryTimerRepository(InMemoryCheckinRepository? checkins = null)`. `SwitchAsync` grava o trecho nesse repositório e lança `InvalidOperationException` sem ele.

**A regra da troca** (é o que garante "nada contado duas vezes nem perdido"):
- Os minutos do trecho são os **minutos inteiros, arredondados para baixo**: `floor((now - StartedAt).TotalMinutes)`.
- O novo início é `StartedAt + minutos`, não `now`. Assim, os segundos que sobram passam para a próxima tarefa.
- Com 0 minutos, só o nome muda: o início fica o mesmo, e não sai registro.
- O "Parar" continua arredondando ao minuto mais próximo, como hoje.
- Na spec, o item 5 da seção 4 passa a dizer isso em lugar de "o novo início é o mesmo instante usado para medir".

- [ ] **Step 1: Escrever os testes que falham** em `TimerServiceTests`. Declare `_timers = new(_checkins)`.
  - `Set_task_trims_and_is_kept_on_the_timer`: `SetTaskAsync("  Inglês ")` deixa `(await GetAsync())!.Task == "Inglês"`.
  - `Set_task_with_only_spaces_clears_it` (Review Focus 5): o resultado é `Task == null`.
  - `Set_task_accepts_emoji_and_200_characters`: `new string('a', 200)` e `"📖 Ler"` são aceitos. O limite conta unidades UTF-16 (`string.Length`), como o da nota.
  - `Set_task_longer_than_200_characters_is_refused`: lança `GoalValidationException` com o erro `A tarefa deve ter até 200 caracteres.`.
  - `Set_task_without_timer_fails`: lança `GoalStateException` com a mensagem `Nenhum cronômetro rodando.`.
  - `Switch_registers_whole_minutes_with_the_old_task_and_carries_the_seconds`:
    - começa em T0, define "Inglês" e avança 25 min 40 s;
    - `SwitchTaskAsync("Capítulo 3")` devolve 25;
    - existe um registro de `Amount == 25` com `Note == "Inglês"`;
    - o cronômetro fica com `StartedAt == T0 + 25 min`, `Task == "Capítulo 3"` e `StoppedAt == null`.
  - `Switch_then_stop_adds_up_to_the_whole_elapsed_time`: em T0, troca aos 10 min 50 s; para aos 30 min 20 s; registra os minutos devolvidos. A soma dos registros é 30.
  - `Switch_under_a_minute_only_renames`: aos 40 s, troca para "B". Não há registro, `StartedAt == T0` e `Task == "B"`.
  - `Switch_with_a_stopped_timer_fails_and_registers_nothing`: lança `GoalStateException`, e nenhum registro é criado.
  - `Switch_refused_by_checkin_validation_keeps_the_timer` (Review Focus 3): avança 24 h 1 min; `SwitchTaskAsync` lança `GoalValidationException`; o cronômetro fica igual ao de antes, e não há registro.
  - `Switch_loses_the_race_when_the_timer_changed` (Review Focus 2): use um `ITimerRepository` de teste cujo `SwitchAsync` devolve falso. O resultado é `GoalStateException` com a mensagem `O cronômetro mudou. Confira em Hoje.`, e não há registro.
  - Em `CheckinServiceTests`: `Recent_notes_are_distinct_newest_first_and_limited_to_5`. Com 7 registros (um nome repetido e um sem nota), o resultado são as 5 notas distintas mais recentes, em ordem.

- [ ] **Step 2: Rodar e ver falhar.**
  Run: `dotnet test --project tests/SlimeGoals.Core.Tests -- --filter-class "*TimerServiceTests" --filter-class "*CheckinServiceTests"`
  Expected: erro de compilação por causa dos membros novos e, depois de criar as assinaturas vazias, falhas nas asserções.

- [ ] **Step 3: Implementar** os membros do bloco Interfaces, nos fakes e no Core.
  - `SetTaskAsync` chama `SetTaskAsync` do repositório, nunca `SaveAsync`: assim, um cronômetro descartado por outro processo não volta.
  - `SwitchTaskAsync`:
    - lê o cronômetro e exige `StoppedAt == null`;
    - normaliza e valida o nome novo antes de qualquer gravação;
    - calcula os minutos inteiros;
    - com 0 minutos, chama `SetTaskAsync`;
    - com mais, chama `PrepareAsync(goalId, minutos, timer.Task)` e depois `SwitchAsync`;
    - termina com `NotifyChanged`.
  - Atualize a spec, como dito acima.

- [ ] **Step 4: Rodar e ver passar.**
  Run: o comando do Step 2, e depois `dotnet test --project tests/SlimeGoals.Core.Tests`.
  Expected: tudo passa, inclusive os testes antigos de `AdjustAsync`.

- [ ] **Step 5: Commit.**
  ```bash
  git add src/SlimeGoals.Core tests/SlimeGoals.Testing tests/SlimeGoals.Core.Tests specs/Tarefas_no_cronometro_v1.md
  git commit -m "feat(core): tarefa no cronômetro e troca sem perder tempo"
  ```

### Task 2: Infra — coluna `task`, troca numa transação, sugestões

**Files:**
- Create: `src/SlimeGoals.Infrastructure/Data/Migrations/007_timer_task.sql`. Ela é embutida e descoberta como a 006; confira o `.csproj` e o executor de migrações.
- Modify: `src/SlimeGoals.Infrastructure/Data/TimerRepository.cs`
- Modify: `src/SlimeGoals.Infrastructure/Data/CheckinRepository.cs`
- Test: `tests/SlimeGoals.Infrastructure.Tests/Data/TimerRepositoryTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Data/CheckinRepositoryTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Data/Migration007Tests.cs`

**Interfaces:**
- Consumes: `ITimerRepository.SetTaskAsync`, `ITimerRepository.SwitchAsync` e `ICheckinRepository.RecentNotesAsync`, da Task 1.
- Produces:
  - `internal static CheckinRepository.InsertAsync(SqliteConnection c, SqliteTransaction tx, Checkin checkin, CancellationToken ct) : Task`. Faz o INSERT e o `SyncOutbox.EnqueueAsync`, e é usado por `AddAsync` e por `TimerRepository.SwitchAsync`.
  - A migração: `ALTER TABLE active_timer ADD COLUMN task TEXT;`.

- [ ] **Step 1: Escrever os testes que falham.**
  - `Migration007Tests.Existing_timer_keeps_running_with_no_task`: um banco na versão 6 com um cronômetro, migrado, lê o mesmo cronômetro com `Task == null`.
  - `TimerRepositoryTests`:
    - `Task_round_trips`: `SaveAsync` com `Task = "Inglês"` e depois `GetAsync`.
    - `Set_task_updates_only_the_name`: o início e a parada continuam iguais.
    - `Set_task_without_timer_returns_false_and_creates_nothing`.
    - `Switch_writes_the_chunk_and_the_new_start_together`: o registro existe, a fila da sincronização tem o id dele, e o cronômetro tem o novo início e o novo nome.
    - `Switch_with_a_different_start_or_a_stopped_timer_returns_false_and_writes_nothing` (Review Focus 2): sem registro, sem fila, e o cronômetro fica intacto.
    - `Switch_after_the_timer_was_cleared_does_not_bring_it_back`.
  - `CheckinRepositoryTests.Recent_notes_skip_deleted_and_null_and_are_distinct_newest_first`.

- [ ] **Step 2: Rodar e ver falhar.**
  Run: `dotnet test --project tests/SlimeGoals.Infrastructure.Tests -- --filter-class "*TimerRepositoryTests" --filter-class "*CheckinRepositoryTests" --filter-class "*Migration007Tests"`
  Expected: FAIL. A coluna `task` não existe, e os métodos novos estão vazios.

- [ ] **Step 3: Implementar.**
  - `SetTaskAsync`: `UPDATE active_timer SET task = @Task WHERE id = 1`. Devolve falso se nenhuma linha mudou.
  - `SwitchAsync`, numa transação só:
    - `UPDATE active_timer SET started_at = @NewStart, task = @Task WHERE id = 1 AND goal_id = @GoalId AND started_at = @Expected AND stopped_at IS NULL`, com o instante formatado como os outros `started_at`;
    - com 0 linhas, desfaz e devolve falso;
    - senão, chama `CheckinRepository.InsertAsync` e confirma a transação;
    - a violação de chave estrangeira vira `GoalNotFoundException`, como em `AddAsync`.
  - `RecentNotesAsync`: `SELECT note FROM checkins WHERE goal_id = @GoalId AND note IS NOT NULL AND deleted_at IS NULL GROUP BY note ORDER BY MAX(done_at) DESC LIMIT @Limit`.
  - `GetAsync`, `TryStartAsync` e `SaveAsync` passam a ler e gravar a coluna `task`.

- [ ] **Step 4: Rodar e ver passar.**
  Run: o comando do Step 2, e depois `dotnet test --project tests/SlimeGoals.Infrastructure.Tests`.
  Expected: PASS.

- [ ] **Step 5: Commit.**
  ```bash
  git add src/SlimeGoals.Infrastructure tests/SlimeGoals.Infrastructure.Tests
  git commit -m "feat(infra): tarefa do cronômetro gravada e troca numa transação"
  ```

### Task 3: Fora do app — "Meta · Tarefa" na janelinha e na notificação

**Files:**
- Modify: `src/SlimeGoals.Core/Services/RunningTimerWatcher.cs`
- Modify: `src/SlimeGoals.App/ViewModels/MiniTimerViewModel.cs`
- Modify: `src/SlimeGoals.App/Views/MiniTimerWindow.axaml`, só se o texto ainda não corta com reticências.
- Modify: `src/SlimeGoals.Android/Reminders/AndroidTimerNotification.cs`
- Modify: `src/SlimeGoals.Android/Resources/layout/notification_timer.xml` e `notification_timer_big.xml`, só se `timer_title` ainda não tem `ellipsize="end"` e `maxLines="1"`.
- Test: `tests/SlimeGoals.Infrastructure.Tests/Services/RunningTimerWatcherTests.cs`, os testes do `MiniTimerViewModel` em `tests/SlimeGoals.App.Tests`, `tests/SlimeGoals.Core.Tests/Services/TimerNotificationSyncTests.cs`

**Interfaces:**
- Consumes: `ActiveTimer.Task` e `TimerService.SetTaskAsync`, da Task 1.
- Produces: `record RunningTimer(Guid GoalId, string Title, string Icon, GoalColor Color, DateTimeOffset StartedAt, string? Task = null)` com a propriedade `string Label`. `Label` vale `Title` sem tarefa, ou `$"{Title} · {Task}"` com tarefa.

- [ ] **Step 1: Escrever os testes que falham.**
  - `RunningTimerWatcherTests`:
    - `Current_carries_the_task`;
    - `Changing_the_task_raises_Changed`: depois de `SetTaskAsync("B")` e de `NotifyChanged`, o evento dispara e `Current!.Task == "B"`.
  - Teste da `Label`: `"Estudar"` sem tarefa, e `"Estudar · Inglês"` com tarefa.
  - `MiniTimerViewModel`: o `Title` mostrado é a `Label`.
  - `TimerNotificationSyncTests.Task_change_republishes_with_the_new_label`: o fake de `ITimerNotification` recebe um `Show` cuja `Label` tem a tarefa nova.

- [ ] **Step 2: Rodar e ver falhar.**
  Run: `dotnet test --solution SlimeGoals.slnx -- --filter-class "*RunningTimerWatcherTests" --filter-class "*MiniTimer*" --filter-class "*TimerNotificationSyncTests"`
  Expected: FAIL. `Task` e `Label` não existem.

- [ ] **Step 3: Implementar.**
  - O watcher preenche `Task` a partir de `ActiveTimer.Task`.
  - `MiniTimerViewModel.Title` passa a usar `Label`.
  - No Android, `SetContentTitle` e `timer_title` passam a usar `Label`.
  - A janelinha e a notificação devem cortar com reticências numa linha só (Review Focus 5).

- [ ] **Step 4: Rodar e ver passar.**
  Run: o comando do Step 2 e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`.
  Expected: PASS, e o Android compila. O aviso XA0033 é do ambiente.

- [ ] **Step 5: Commit.**
  ```bash
  git add src/SlimeGoals.Core src/SlimeGoals.App src/SlimeGoals.Android tests
  git commit -m "feat(app): nome da tarefa na janelinha e na notificação do cronômetro"
  ```

### Task 4: Hoje — campo da tarefa, sugestões, troca e nota preenchida

**Files:**
- Modify: `src/SlimeGoals.App/ViewModels/TodayGoalViewModel.cs`
- Modify: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`
- Modify: `src/SlimeGoals.App/Views/TodayView.axaml`. Há duas variantes do bloco do cronômetro, `narrow` e `wide`, e as duas recebem o campo. Também os ícones, se faltar o `arrow-left-right` do Lucide nos recursos.
- Modify: `src/SlimeGoals.App/Views/TodayView.axaml.cs`, só para Enter e perda de foco, se não der por binding.
- Test: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs` (usa `Support/TimerFixture.cs`)

**Interfaces:**
- Consumes: `TimerService.SetTaskAsync`, `SwitchTaskAsync` e `TaskSuggestionsAsync`, e `ActiveTimer.Task`, da Task 1.
- Produces:
  - em `TodayGoalViewModel`:
    - `string? TimerTask { get; }`;
    - `[ObservableProperty] string? TaskInput`, que começa igual a `TimerTask`;
    - `[ObservableProperty] bool IsSwitchingTask`;
    - `[ObservableProperty] IReadOnlyList<string> TaskSuggestions`, que começa como `[]`;
  - em `TodayPageViewModel`: os comandos `SaveTaskCommand`, `BeginSwitchTaskCommand`, `ConfirmSwitchTaskCommand` e `CancelSwitchTaskCommand`, cada um com parâmetro `TodayGoalViewModel`.

**Comportamento:**
- **O campo:** é um `AutoCompleteBox` com marca d'água `No que está trabalhando?`, `MaxLength` 200, filtro "contém" sem diferenciar maiúsculas e as sugestões em `TaskSuggestions`. Ele aparece só com o cronômetro rodando, logo abaixo do título da meta.
- **Gravar o nome:** Enter, perda de foco ou escolher uma sugestão chamam `SaveTask`, que só grava se o nome normalizado mudou. No modo de troca, essas ações chamam `ConfirmSwitchTask`.
- **⇄** fica ao lado do ■ e chama `BeginSwitchTask`, que limpa o campo e mostra os botões `Trocar` e `Cancelar` no lugar de ⇄ e ■. `Cancelar` devolve o nome atual ao campo.
- **Sugestões:** são carregadas no `LoadAsync` só para o item com o cronômetro rodando.
- **Cronômetro parado:** ao abrir o painel de conferência, `PanelNote` começa igual a `TimerTask`.
- **Recarregamento** (Review Focus 1): antes do `Goals.Clear()`, guarde `TaskInput` e `IsSwitchingTask` do item rodando, se o texto foi editado ou se está no modo de troca. Devolva ao item recriado, se ele ainda for o cronômetro rodando da mesma meta.
- **Erros** de `SetTaskAsync` e `SwitchTaskAsync` aparecem como os outros erros do cartão (`RunAsync`).

- [ ] **Step 1: Escrever os testes que falham** em `TodayPageViewModelTests`.
  - `Running_item_shows_the_task_and_suggestions`: há uma nota "Inglês" num registro anterior e um cronômetro com `Task = "Capítulo 3"`. O resultado é `TaskInput == "Capítulo 3"` e `TaskSuggestions` contendo "Inglês".
  - `Timer_started_from_a_reminder_has_an_empty_task_field` (Review Focus 4): um cronômetro iniciado por `TimerService.StartAsync`, sem nome, dá `TaskInput == null`, e `SaveTask` com "Ler" grava "Ler".
  - `Save_task_unchanged_does_not_write`: o `DataChangeNotifier` não dispara.
  - `Confirm_switch_registers_and_renames`: depois de 25 min, o registro tem `Note` igual ao nome antigo, e o cronômetro tem o nome novo.
  - `Cancel_switch_restores_the_current_name`.
  - `Stopped_timer_panel_starts_with_the_task_as_note`.
  - `Typing_survives_a_reload` (Review Focus 1):
    - com `TaskInput = "Rev"` e `IsSwitchingTask = true`, um `NotifyChanged` externo dispara o recarregamento;
    - o item novo mantém `"Rev"` e o modo de troca;
    - depois que o cronômetro para, o recarregamento seguinte não restaura nada.
  - `Switch_race_shows_the_error`: o cronômetro é descartado entre o carregamento e a confirmação. O cartão mostra `O cronômetro mudou. Confira em Hoje.` ou `Nenhum cronômetro rodando.`.

- [ ] **Step 2: Rodar e ver falhar.**
  Run: `dotnet test --project tests/SlimeGoals.App.Tests -- --filter-class "*TodayPageViewModelTests"`
  Expected: FAIL. Os membros e os comandos não existem.

- [ ] **Step 3: Implementar** o ViewModel e depois a view, conforme o comportamento acima.

- [ ] **Step 4: Rodar e ver passar.**
  Run: o comando do Step 2, e `dotnet test --solution SlimeGoals.slnx`.
  Expected: PASS.

- [ ] **Step 5: Conferir as telas.**
  Run: `dotnet run --project tools/SlimeGoals.Render -- /tmp/render all`. Se a Render não tiver um cenário com cronômetro rodando e tarefa, acrescente um.
  Expected: leia os PNGs de celular e de desktop em larguras diferentes. O campo fica alinhado com o título, ⇄ e ■ têm o mesmo tamanho e espaçamento, nada transborda, e o modo de troca mostra `Trocar` e `Cancelar` simétricos.

- [ ] **Step 6: Commit.**
  ```bash
  git add src/SlimeGoals.App tests/SlimeGoals.App.Tests tools/SlimeGoals.Render
  git commit -m "feat(app): campo da tarefa e troca de tarefa no cronômetro do Hoje"
  ```

### Task 5: Verificação no app e nos aparelhos

**Files:**
- Modify: `specs/Planejamento_v3.md`. Em RF03, uma frase sobre a tarefa do cronômetro, com a referência a `specs/Tarefas_no_cronometro_v1.md`.
- Modify: `specs/Tarefas_no_cronometro_v1.md`. Uma seção "Verificação" com o que foi conferido e onde.

- [ ] **Step 1: Desktop.**
  Run:
  ```bash
  XDG_DATA_HOME=/tmp/slimegoals-dev/data XDG_CONFIG_HOME=/tmp/slimegoals-dev/config dotnet run --project src/SlimeGoals.Desktop
  ```
  Expected:
  - numa meta de tempo, ▶ começa direto;
  - dar o nome "Inglês" faz a janelinha, com o app minimizado, mostrar "Meta · Inglês";
  - ⇄ e "Capítulo 3" depois de 1 min criam um registro de 1 min com a nota "Inglês";
  - ao parar, a nota vem preenchida com "Capítulo 3".

- [ ] **Step 2: Emulador** (`emulator-5554`, Debug com `-t:Install`).
  Expected: a notificação fixa mostra "Meta · Tarefa", e o "Iniciar" de um lembrete começa sem tarefa, com o campo vazio no Hoje.

- [ ] **Step 3: Verificação completa.**
  Run:
  ```bash
  dotnet test --solution SlimeGoals.slnx
  dotnet build SlimeGoals.slnx -c Release
  dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk
  ```
  Expected: tudo passa, com 0 avisos no Release do Linux.

- [ ] **Step 4: Commit.**
  ```bash
  git add specs
  git commit -m "docs(specs): verificação das tarefas no cronômetro"
  ```
