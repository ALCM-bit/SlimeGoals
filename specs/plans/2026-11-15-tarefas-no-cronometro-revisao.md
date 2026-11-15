# Revisão das tarefas no cronômetro: plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: use superpowers:subagent-driven-development (recomendado) ou superpowers:executing-plans, tarefa por tarefa. Os passos usam caixas (`- [ ]`) para acompanhar.

**Objetivo:** o campo da tarefa no Hoje mostra que o nome está valendo, e o cartão de uma meta de tempo lista os registros de hoje, com edição e exclusão.

**Arquitetura:** o Core ganha `CheckinService.TodayAsync`, `ReplaceAsync` e `DeleteAsync`, sobre três métodos novos de `ICheckinRepository`. No SQLite, excluir apaga a linha de verdade (política da 004) e põe a exclusão na fila da sincronização. Substituir é excluir e gravar um registro novo com o mesmo `done_at`, numa transação só. Nada muda no formato da sincronização. O App ganha estados no campo da tarefa e uma lista recolhível de trechos no cartão.

**Tecnologia:** .NET 10, Avalonia 12 + CommunityToolkit.Mvvm, SQLite (Microsoft.Data.Sqlite + Dapper), xUnit v3 + FluentAssertions.

**Spec:** `specs/Tarefas_no_cronometro_v1.md` §7 (aprovada em 15/11/2026). As §1–§6 continuam valendo onde a §7 não as muda.

## Restrições globais

- `TreatWarningsAsErrors`: nenhum warning.
- Os testes nunca tocam o banco real, o chaveiro nem o app instalado.
- O `Trace` nunca registra a mensagem da exceção: `TraceText.Of(e)` ou `TraceText.Types(e)`.
- Conventional Commits em pt-BR, com escopo por camada, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Os textos da interface ficam em pt-BR, exatamente como escritos aqui.
- O limite do nome é `CheckinService.MaxNoteLength` (200), e o dos minutos é `CheckinService.MaxDurationAmount` (24 × 60).
- Excluir apaga a linha (`DELETE`); `deleted_at` continua sem uso (`specs/Planejamento_v3.md`, decisão de 06/11/2026).
- Verificação completa: `dotnet test --solution SlimeGoals.slnx` (com Docker), `dotnet build SlimeGoals.slnx -c Release` e o build de `SlimeGoals.Android.slnx` (os XA0033 são do ambiente).

## Decisões deste plano (além da spec)

- `ReplaceAsync` e `DeleteAsync` recebem também o `goalId`: as mensagens de estado do Core (`GoalStateException`) precisam da meta, e o App tem as duas coisas.
- O sinal do registro é mantido: o editor mostra os minutos sem sinal, e um registro de "tirar" continua negativo.
- Os minutos negativos aparecem como `GoalFormatting.Duration` já os escreve ("-5 min"), igual ao resto do app.
- "Hoje · N min" soma só os registros gravados, limitada a 0; o trecho em andamento não entra.
- Apagar não pede confirmação. Para chegar ao botão, a pessoa já abriu o editor do registro (dois toques).
- No campo sem nome, só aparece o ✓; o ⇄ aparece com o nome gravado (esboço da §7.1). O ✕ aparece só ao editar um nome que já existe.
- Salvar um registro sem mudar nada não grava, para não gerar tráfego de sincronização à toa.

## Foco da revisão

1. **Recarga em segundo plano** (sincronização, janelinha) com o editor de um registro aberto ou com o nome em edição: o texto digitado e o editor aberto continuam lá. Testes nas Tarefas 3 e 4.
2. **Registro apagado por outro aparelho ou processo** com o editor aberto: o editor mostra "O registro mudou. Confira em Hoje." e nada é gravado. Testes nas Tarefas 2 e 4.
3. **Releitura completa da sincronização antes de o servidor confirmar a exclusão:** o registro apagado não volta. Teste na Tarefa 1.
4. **Registro de ontem**, com o Hoje aberto desde antes da meia-noite: "Só dá para ajustar registros de hoje.". Teste na Tarefa 2.
5. **Total bruto já negativo depois de juntar aparelhos:** uma mudança que aumenta o total passa; uma que diminui é recusada. Teste na Tarefa 2.

---

### Tarefa 1: excluir e substituir registros no SQLite e na fila da sincronização

**Arquivos:**
- Modificar: `src/SlimeGoals.Core/Abstractions/ICheckinRepository.cs`
- Modificar: `src/SlimeGoals.Infrastructure/Data/CheckinRepository.cs`
- Modificar: `src/SlimeGoals.Infrastructure/Data/SyncOutbox.cs`
- Modificar: `src/SlimeGoals.Infrastructure/Data/SyncApply.cs` (`ApplyCheckinAsync`)
- Modificar: `tests/SlimeGoals.Testing/InMemoryCheckinRepository.cs`
- Testes: `tests/SlimeGoals.Infrastructure.Tests/Data/CheckinRepositoryTests.cs`, `SyncOutboxTests.cs`, `SyncApplyTests.cs`

**Interfaces:**
- Produz, em `ICheckinRepository`:
  - `Task<Checkin?> GetAsync(Guid id, CancellationToken ct = default)`: o registro com `deleted_at IS NULL`, ou `null`.
  - `Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)`: `false` se a linha já não existia, e aí nada muda.
  - `Task<bool> ReplaceAsync(Guid id, Checkin replacement, CancellationToken ct = default)`: apaga `id` e grava `replacement` numa transação. `false` se `id` não existia, e aí nada é gravado. Lança `GoalNotFoundException` com violação de chave estrangeira, como `AddAsync`.
- Produz, em `SyncOutbox`: `static Task EnqueueDeletionAsync(SqliteConnection c, IDbTransaction tx, Guid recordId, string kind, CancellationToken ct)`.
- `InMemoryCheckinRepository` implementa os três métodos com a mesma regra. A escrita respeita `FailWritesWith`, e a exclusão tira o item da lista.

- [ ] **Passo 1: escrever os testes que falham**

Em `CheckinRepositoryTests`:
- `Get_returns_the_checkin_or_null`
- `Delete_removes_the_row_and_reports_whether_it_existed`: o primeiro `DeleteAsync` devolve `true` e o segundo devolve `false`; `SumAsync` cai pelo valor do registro.
- `Replace_swaps_the_row_in_one_step`: depois da troca, `ListAsync` tem só o novo registro, com o mesmo `DoneAt`.
- `Replace_of_a_missing_checkin_writes_nothing`: devolve `false`, e a contagem de `checkins` não muda.

Em `SyncOutboxTests` (com `EnableSync()`):
- `Deleting_a_known_checkin_queues_a_delete_with_its_seq`: com `sync_records` = 7, a fila tem uma linha com `op = 'delete'` e `base_seq = 7`.
- `Deleting_a_never_sent_checkin_only_leaves_the_queue`: depois de `AddAsync` e `DeleteAsync`, `Count("sync_outbox")` dá 0.
- `Deleting_with_sync_disabled_queues_nothing`
- `Replace_queues_the_old_deletion_and_the_new_upsert`: com o antigo conhecido, a fila tem `delete` para o antigo e `upsert` para o novo.

Em `SyncApplyTests`:
- `Queued_checkin_deletion_is_not_undone_by_a_full_read`: um registro conhecido (`RememberAsync`) e apagado localmente chega de novo com `ApplyAsync(…, In(checkin, seq: 9))`. Resultado: `checkins` não tem a linha, e a fila mantém `op = 'delete'` com `base_seq = 9`.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test --project tests/SlimeGoals.Infrastructure.Tests -- --filter-class "*CheckinRepositoryTests" --filter-class "*SyncOutboxTests" --filter-class "*SyncApplyTests"`
Esperado: erro de compilação (os métodos não existem).

- [ ] **Passo 3: implementar**

- **`CheckinRepository.DeleteAsync` e `ReplaceAsync`:** `DELETE FROM checkins WHERE id = @Id`. Com 0 linhas, desfaz a transação e devolve `false`. Senão chama `SyncOutbox.EnqueueDeletionAsync` na mesma transação. `ReplaceAsync` depois chama `InsertAsync(c, tx, replacement, ct)`.
- **`EnqueueDeletionAsync`:** sem efeito com `sync_state.enabled = 0`. Com o registro em `sync_records`, faz upsert de `op = 'delete'` com `base_seq = sync_records.seq`, reusando `ReplaceOnConflict` e o `WHERE 1` exigido pelo SQLite em `INSERT … SELECT … ON CONFLICT`. Sem o registro em `sync_records`, só `DELETE FROM sync_outbox WHERE record_id = @Id`.
- **`SyncApply.ApplyCheckinAsync`:** depois da checagem de lápide, se `sync_outbox` tem `op = 'delete'` para `envelope.RecordId`, chama `DiscardAsync(record, envelope, SyncOutbox.Checkin)` e retorna.

- [ ] **Passo 4: rodar e ver passar**

Mesmo comando. Esperado: PASS.

- [ ] **Passo 5: commit**

`feat(infra): excluir e substituir registros com a fila da sincronização`

---

### Tarefa 2: ajustar e apagar registros de hoje no Core

**Arquivos:**
- Modificar: `src/SlimeGoals.Core/Services/CheckinService.cs`
- Modificar: `tests/SlimeGoals.Infrastructure.Tests/Sync/SimulatedDevice.cs`
- Testes: `tests/SlimeGoals.Core.Tests/Services/CheckinServiceTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Sync/ConvergenceTests.cs`

**Interfaces:**
- Consome: `ICheckinRepository.GetAsync`, `DeleteAsync` e `ReplaceAsync` (Tarefa 1).
- Produz, em `CheckinService`:
  - `Task<IReadOnlyList<Checkin>> TodayAsync(CancellationToken ct = default)`: os registros do dia local de hoje, de todas as metas, do mais recente para o mais antigo. Vem de `ListAsync` com `DateRange.LocalMidnightToUtc` de hoje e de amanhã.
  - `Task<int> ReplaceAsync(Guid goalId, Guid checkinId, int minutes, string? note, CancellationToken ct = default)`: devolve o novo total do período, limitado.
  - `Task<int> DeleteAsync(Guid goalId, Guid checkinId, CancellationToken ct = default)`: devolve o novo total do período, limitado.
- Produz, em `SimulatedDevice`: `Task<IReadOnlyList<Checkin>> Today()`, `Task<int> Replace(Guid goalId, Guid checkinId, int minutes, string? note)` e `Task<int> DeleteCheckin(Guid goalId, Guid checkinId)`.

**Regras de `ReplaceAsync` e `DeleteAsync`, nesta ordem:**

| Condição | Resultado |
|---|---|
| meta inexistente | `GoalNotFoundException` |
| meta arquivada | `GoalStateException`: "Meta arquivada não recebe registros." |
| meta fora de hoje | `GoalStateException`: "Esta meta não está ativa hoje." |
| medida diferente de `Duration` | `GoalStateException`: "Só dá para ajustar registros de metas de tempo." |
| registro inexistente ou de outra meta | `GoalStateException`: "O registro mudou. Confira em Hoje." |
| `DoneAt` fora do dia local de hoje | `GoalStateException`: "Só dá para ajustar registros de hoje." |
| registro com `Compensation` | `GoalStateException`: "Este registro vem da junção de aparelhos e não pode ser ajustado." |
| `minutes < 1` ou `> MaxDurationAmount` (só em `ReplaceAsync`) | `GoalValidationException("Amount", "Informe de 1 min a 24 h.")` |
| nome com mais de `MaxNoteLength` depois do trim (só em `ReplaceAsync`) | `GoalValidationException("Note", "A nota deve ter até 200 caracteres.")` |
| novo valor menor que o antigo e total bruto novo `< 0` | `GoalStateException`: "O total do período ficaria negativo." |
| nada mudou (minutos e nome normalizado iguais) | não grava e devolve o total atual |
| o repositório devolve `false` | `GoalStateException`: "O registro mudou. Confira em Hoje." |

- **Total bruto novo:** `bruto − antigo.Amount + novo`, com `novo = Math.Sign(antigo.Amount) * minutes` (0 na exclusão).
- **Substituto:** `Id = Guid.CreateVersion7(now)`, mesmos `GoalId` e `DoneAt`, nome normalizado como no `PrepareAsync`, `UpdatedAt = now` e `Compensation = null`.
- Toda escrita bem-sucedida chama `changes?.NotifyChanged()`.

- [ ] **Passo 1: escrever os testes que falham** (em `CheckinServiceTests`, com os helpers do arquivo; meta de tempo diária)

- `Today_lists_todays_checkins_newest_first`: registros às 08:00 e 11:00 locais de hoje e um de ontem. Resultado: os dois de hoje, o das 11:00 primeiro.
- `Replace_changes_minutes_and_name_keeping_the_time`: um de 25 min "Inglês" vira 20 min "Leitura". O total do período cai de 25 para 20, `All` tem um registro vivo com `DoneAt` igual ao antigo e outro `Id`, e o retorno é 20.
- `Replace_keeps_a_negative_record_negative`: com 30 e −5, `ReplaceAsync(…, minutes: 10, …)` no −5 deixa o total em 20.
- `Delete_removes_the_record_from_the_total`: com 25 e 12, apagar o de 12 devolve 25.
- `Delete_of_a_negative_record_raises_the_total`: com 30 e −5, apagar o −5 devolve 30.
- `Change_that_would_leave_the_total_negative_is_refused`: com 10 e −10, apagar o de 10 lança "O total do período ficaria negativo.", e nada é gravado.
- `Change_that_raises_a_negative_raw_total_is_allowed`: com total bruto −20 (registros 10 e −30), apagar o −30 passa e devolve 10.
- `Yesterday_checkin_cannot_be_changed`: lança "Só dá para ajustar registros de hoje.".
- `Compensation_cannot_be_changed`: lança a mensagem de junção, tanto no replace quanto no delete.
- `Minutes_out_of_range_are_refused` (Theory com 0 e 1441): `GoalValidationException` com "Informe de 1 min a 24 h.".
- `Missing_checkin_reports_that_it_changed`: um id desconhecido lança "O registro mudou. Confira em Hoje.".
- `Unchanged_replace_writes_nothing`: `All.Count` não muda.
- `Replace_and_delete_notify_changes`: um `DataChangeNotifier` real recebe dois avisos.

Em `ConvergenceTests`:
- `Edited_and_deleted_checkins_converge`:
  1. A e B ligam a sincronização; A cria uma meta de tempo e registra 25 "Inglês" e 12 "Leitura"; A e B sincronizam.
  2. A troca o de 25 por 20 "Inglês" e apaga o de 12.
  3. A sincroniza, depois B.
  4. `B.ShownTotal` dá 20, `B.Lines` é igual a `A.Lines`, e `A.Pending` e `B.Pending` dão 0.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test --project tests/SlimeGoals.Core.Tests -- --filter-class "*CheckinServiceTests"`
Esperado: erro de compilação (`TodayAsync`, `ReplaceAsync` e `DeleteAsync` não existem).

- [ ] **Passo 3: implementar** os três métodos em `CheckinService`

As checagens de meta (existe, não arquivada, ativa hoje) vão para um helper privado usado também por `PrepareAsync`.

- [ ] **Passo 4: rodar e ver passar**

Rodar o comando do passo 2 e `dotnet test --project tests/SlimeGoals.Infrastructure.Tests -- --filter-class "*ConvergenceTests"`.
Esperado: PASS.

- [ ] **Passo 5: commit**

`feat(core): ajustar e apagar registros de hoje`

---

### Tarefa 3: o campo da tarefa com estados

**Arquivos:**
- Modificar: `src/SlimeGoals.App/ViewModels/TodayGoalViewModel.cs`
- Modificar: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`
- Modificar: `src/SlimeGoals.App/Views/TodayView.axaml`, a área "Tarefa do cronômetro rodando" (hoje nas linhas 218–248)
- Modificar: `src/SlimeGoals.App/Views/TodayView.axaml.cs`
- Modificar: `src/SlimeGoals.App/Styles/Theme.axaml` (ícones `IconPencil` e `IconChevronDown`; o segundo é usado na Tarefa 4)
- Modificar: `tools/SlimeGoals.Render/Program.cs`
- Testes: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**
- Produz, em `TodayGoalViewModel`:
  - `[ObservableProperty] bool IsEditingTask`;
  - `bool ShowsTaskLabel => IsTimerRunning && TimerTask is not null && !IsEditingTask && !IsSwitchingTask`;
  - `bool ShowsTaskField => IsTimerRunning && !ShowsTaskLabel`;
  - `bool ShowsConfirmTask => ShowsTaskField && !IsSwitchingTask`;
  - `string TaskLabel => $"▸ {TimerTask}"`.
  
  `HasTaskDraft` passa a contar também `IsEditingTask`. As propriedades calculadas avisam a mudança quando `IsEditingTask` ou `IsSwitchingTask` mudam.
- Produz, em `TodayPageViewModel`:
  - `[RelayCommand] void BeginEditTask(TodayGoalViewModel?)`: `IsEditingTask = true` e `TaskInput = TimerTask`;
  - `[RelayCommand] void CancelEditTask(TodayGoalViewModel?)`: `IsEditingTask = false` e `TaskInput = TimerTask`.
  
  `SaveTaskCommand` agora também fecha a edição: sem mudança, só põe `IsEditingTask = false`. `LoadAsync` mantém `IsEditingTask` junto do rascunho.
- A tela: o nome gravado aparece como `TextBlock` (`TaskLabel`, com reticências) mais ✎ (`BeginEditTask`, nome acessível "Editar tarefa") e ⇄. O campo aparece com ✓ (`SaveTask`, nome acessível "Confirmar tarefa", visível com `ShowsConfirmTask`) e ✕ (`CancelEditTask`, nome acessível "Cancelar edição", visível com `IsEditingTask`). A linha Cancelar/Trocar continua como está.

- [ ] **Passo 1: escrever os testes que falham**

- `Saved_task_shows_as_label`: depois de gravar "Inglês", `ShowsTaskLabel` é true e `ShowsTaskField` é false.
- `Running_timer_without_task_shows_the_field_with_confirm`
- `Edit_task_opens_the_field_with_the_current_name`: depois de `BeginEditTask`, os valores são `(ShowsTaskField, ShowsConfirmTask, TaskInput) == (true, true, "Inglês")`.
- `Confirm_edit_saves_and_closes`: `TaskInput = "Leitura"` seguido de `SaveTask` deixa o card recarregado com `TimerTask == "Leitura"` e `IsEditingTask == false`.
- `Confirm_unchanged_edit_closes_without_writing`
- `Cancel_edit_restores_the_name`
- `Reload_keeps_the_task_being_edited` (foco da revisão 1): `BeginEditTask` e `TaskInput = "Lei"`, depois `_changes.NotifyChanged()` e a recarga. O novo card tem `IsEditingTask == true` e `TaskInput == "Lei"`.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test --project tests/SlimeGoals.App.Tests -- --filter-class "*TodayPageViewModelTests"`
Esperado: erro de compilação.

- [ ] **Passo 3: implementar o ViewModel e os comandos**

- [ ] **Passo 4: rodar e ver passar**

Mesmo comando. Esperado: PASS, inclusive os testes da tarefa que já existiam.

- [ ] **Passo 5: tela e code-behind**

- Trocar a área do campo pelos dois estados descritos em Interfaces.
- Em `TodayView.axaml.cs`, remover a gravação ao perder o foco (`OnTaskLostFocus` e o `AddHandler` dele). `AfterRelease` continua, por causa do teclado.
- Enter segue como está. Esc cancela a troca, com `IsSwitchingTask`, ou a edição, com `IsEditingTask`.
- O clique em ✎ põe o foco no `AutoCompleteBox` do mesmo cartão depois que ele aparece (`Dispatcher.UIThread.Post`).
- Os ícones novos em `Theme.axaml` seguem o traço dos que já existem (24×24, só contorno).

- [ ] **Passo 6: conferir na Render**

- Rodar `dotnet run --project tools/SlimeGoals.Render -- /tmp/render all`, depois de acrescentar a cena `today-timer-edit-{width}` (com `BeginEditTask` depois de `today-timer-{width}`).
- Ler os PNGs de `today-timer-*` em 320, 360 e 1024 px.
- Esperado:
  - o nome com ✎ e ⇄ alinhados à direita, na mesma altura dos botões do cabeçalho;
  - o campo com ✓ (e ✕ na edição) sem cortar o placeholder;
  - nada saindo do cartão.

- [ ] **Passo 7: commit**

`feat(app): nome da tarefa confirmado no cartão do cronômetro`

---

### Tarefa 4: trechos de hoje no cartão

**Arquivos:**
- Criar: `src/SlimeGoals.App/ViewModels/TodayChunkViewModel.cs`
- Modificar: `src/SlimeGoals.App/ViewModels/TodayGoalViewModel.cs`
- Modificar: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`
- Modificar: `src/SlimeGoals.App/Views/TodayView.axaml` (seção nova depois da `ProgressBar` do cartão)
- Modificar: `tools/SlimeGoals.Render/Program.cs`
- Testes: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**
- Consome: `CheckinService.TodayAsync`, `ReplaceAsync` e `DeleteAsync` (Tarefa 2).
- Produz `TodayChunkViewModel : ObservableObject`:
  - `Guid? CheckinId`, nulo no trecho em andamento;
  - `int Amount`;
  - `string Name`: a nota ou "Sem nome";
  - `[ObservableProperty] string Text`: `GoalFormatting.Duration(Amount)` ou, no trecho em andamento, `GoalFormatting.Elapsed`;
  - `bool IsRunning`;
  - `bool CanEdit`: `!IsRunning && Compensation is null`;
  - `[ObservableProperty]` para `IsEditing`, `decimal? MinutesInput`, `string? NoteInput` e `string? Error`.
- Produz, em `TodayGoalViewModel`:
  - o construtor ganha `IReadOnlyList<Checkin> todayCheckins` (os da meta, do mais recente para o mais antigo);
  - `IReadOnlyList<TodayChunkViewModel> Chunks`: com o cronômetro rodando, o trecho em andamento vem primeiro, com `TimerTask` ou "Sem nome";
  - `bool HasChunks => IsDuration && todayCheckins.Count > 0`;
  - `string ChunksHeader => $"Hoje · {GoalFormatting.Duration(Math.Max(0, soma))}"`;
  - `[ObservableProperty] bool IsChunksOpen`, falso por padrão.
  
  `UpdateElapsed` também atualiza o `Text` do trecho em andamento.
- Produz, em `TodayPageViewModel`:
  - `[RelayCommand] void ToggleChunks(TodayGoalViewModel?)`;
  - `[RelayCommand] void EditChunk(TodayChunkViewModel?)`: fecha qualquer outro editor da página e preenche `MinutesInput = |Amount|` e `NoteInput`;
  - `[RelayCommand] void CancelChunkEdit(TodayChunkViewModel?)`;
  - `[RelayCommand] Task SaveChunkAsync(TodayChunkViewModel?)`: `MinutesInput` nulo dá `Error = "Informe um valor."`; senão arredonda como `PanelAmount` e chama `ReplaceAsync`;
  - `[RelayCommand] Task DeleteChunkAsync(TodayChunkViewModel?)`.

**Erros e recarga:**
- `GoalValidationException`, `GoalStateException` e falha inesperada ("Não foi possível salvar.") vão para `chunk.Error`, sem recarregar.
- `GoalNotFoundException` vai para a faixa da página, como em `RunCoreAsync`. Falhas inesperadas também vão para o `Trace`, com `TraceText.Of`.
- Depois de gravar, recarrega.
- `LoadAsync` lê `checkins.TodayAsync()` uma vez e entrega a cada cartão os registros da meta dele.
- A página guarda os ids das metas com a lista aberta e o rascunho do editor aberto (id do registro, minutos, nome), e os restaura depois de recarregar se o registro ainda existir. Depois de salvar ou apagar com sucesso, o rascunho é limpo antes da recarga.

**Tela:**
- Uma linha-botão com `ChunksHeader` e o chevron (girado com a lista aberta), nome acessível "Trechos de hoje".
- Ao abrir, um `ItemsControl` com: nome (com reticências), texto do tempo e ✎ (nome acessível "Ajustar trecho", visível com `CanEdit && !IsEditing`).
- O editor no lugar da linha usa um `inline-panel` com:
  - minutos, no mesmo controle e estilo do painel de registro;
  - nome, num `TextBox` com `MaxLength` = `MaxNoteLength`;
  - `field-error`;
  - "Apagar" à esquerda (estilo de perigo já existente) e "Cancelar" e "Salvar" à direita.

- [ ] **Passo 1: escrever os testes que falham**

- `Duration_card_lists_todays_records_newest_first`: com 25 "Inglês" e 12 sem nota, `Chunks` traz `Name` e `Text` iguais a `[("Sem nome", "12 min"), ("Inglês", "25 min")]`, `ChunksHeader == "Hoje · 37 min"` e `IsChunksOpen == false`.
- `Card_without_records_today_has_no_list`
- `Running_chunk_comes_first_and_is_not_editable`
- `Compensation_record_is_not_editable`
- `Toggle_survives_reload`: abrir e chamar `_changes.NotifyChanged()` deixa o novo card com `IsChunksOpen == true`.
- `Edit_chunk_prefills_and_closes_other_editors`
- `Save_chunk_replaces_the_record`: 25 → 20 dá `ChunksHeader == "Hoje · 32 min"` depois da recarga.
- `Delete_chunk_removes_it`
- `Chunk_error_stays_in_the_editor` (foco da revisão 2): o registro é apagado por fora, com `_checkins.DeleteAsync` direto, antes de salvar. O resultado é `Error == "O registro mudou. Confira em Hoje."` com o editor ainda aberto.
- `Reload_keeps_the_open_chunk_editor` (foco da revisão 1): com o editor aberto e `MinutesInput = 18`, `NotifyChanged()` deixa o mesmo registro com `IsEditing == true` e `MinutesInput == 18`.
- `Empty_minutes_shows_inform_value`

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test --project tests/SlimeGoals.App.Tests -- --filter-class "*TodayPageViewModelTests"`
Esperado: erro de compilação.

- [ ] **Passo 3: implementar os ViewModels e os comandos**

- [ ] **Passo 4: rodar e ver passar**

Mesmo comando. Esperado: PASS.

- [ ] **Passo 5: tela** conforme o bloco "Tela" acima

- [ ] **Passo 6: conferir na Render**

- Nas cenas, gravar para a meta de tempo dois registros de hoje (25 "Inglês" e 12 sem nota) pelo `CheckinService` da amostra.
- Acrescentar `today-chunks-{width}` (lista aberta) e `today-chunk-edit-{width}` (editor aberto no primeiro registro).
- Ler os PNGs em 320, 360 e 1024 px.
- Esperado: os nomes e os tempos alinhados em coluna; o ✎ na mesma coluna dos botões do cartão; os botões do editor sem quebrar a linha a 320 px.

- [ ] **Passo 7: commit**

`feat(app): trechos de hoje no cartão, com ajuste e exclusão`

---

### Tarefa 5: verificação e spec

**Arquivos:**
- Modificar: `specs/Tarefas_no_cronometro_v1.md` (§4 item 3, uma linha apontando para a §7.1; nova §7.4 "Verificação")

- [ ] **Passo 1: verificação completa**

Rodar: os três comandos das restrições globais.
Esperado: todos os testes passam e 0 warnings.

- [ ] **Passo 2: emulador** (`emulator-5554`, conta de teste)

Instalar com o comando `-t:Install` do README.

Conferir em "Estudo teste":
- o nome gravado aparece como texto;
- ✎ abre o campo com ✓ e ✕, e o ✓ grava;
- sair do campo não grava;
- a lista "Hoje · …" abre, e ajustar 1 trecho e apagar outro mudam o total;
- com o teclado aberto, o editor do trecho fica visível.

Guardar as capturas no scratchpad.

- [ ] **Passo 3: atualizar a spec**

A §7.4 lista o que foi verificado e o que ficou para o autor conferir.

- [ ] **Passo 4: commit**

`docs(specs): verificação do campo confirmado e dos trechos de hoje`
