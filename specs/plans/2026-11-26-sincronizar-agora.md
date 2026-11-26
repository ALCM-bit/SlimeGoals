# Sincronizar agora — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** um botão "Sincronizar agora" na seção Sincronização de Configurações roda um ciclo inteiro na hora (envia a fila e busca o que chegou) e diz como terminou.

**Architecture:**
- **Core.** `SyncScheduler.NextWait` passa a devolver `ChangeDelay` (5 s) para `Skipped`, sem mexer na contagem de falhas.
- **Sync.** O corpo do `FlushAsync` vira `RunCycleNowAsync(EngineRun, CancellationToken)`, que roda o ciclo, repete no `Skipped` esperando o ciclo do agendador e encerra a sessão no `Unauthorized`. O `FlushAsync` e o novo `SyncNowAsync` usam esse trecho.
- **App.** `AccountViewModel.SyncNowCommand` passa pelo `RunAsync` com `AccountAction.Sync`. A linha `SyncMessage` fica presa ao status que o ciclo deixou.

**Tech Stack:** .NET 10, Avalonia 12 + CommunityToolkit.Mvvm, xUnit v3 (MTP), AwesomeAssertions, Testcontainers (PostgreSQL).

**Spec:** `specs/Sincronizacao_v1.md`, seção 6, "Decisões do botão Sincronizar agora", e §4, "Quando roda".

## Global Constraints

- `TreatWarningsAsErrors`. Nomes de teste em inglês com `_`; comentários e textos em português.
- No Trace, só o tipo da exceção.
- Os testes do coordenador sobem PostgreSQL: o Docker precisa estar rodando.
- **Textos, exatamente assim:**
  - botão: `Sincronizar agora`;
  - durante o ciclo: `Sincronizando…`;
  - `Done`: `Sincronização concluída.`;
  - `Incomplete` e `Skipped`: `A sincronização não terminou. Tente de novo em instantes.`;
  - `Failed`, `Paused` e `Disabled`: `Não foi possível sincronizar.`;
  - `Unauthorized`: sem linha.

## Review Focus

1. **Mudança durante o ciclo do botão.** O agendador acorda no meio dele, pula e voltaria só em 15 min. Agora volta em 5 s (Task 1).
2. **Botão com o agendador no meio de um ciclo lento.** O botão espera e roda o seu, sem devolver `Skipped` (Task 2).
3. **Token revogado na hora do clique.** A sessão é encerrada antes de o botão devolver, e a fila fica (Task 2).
4. **O mesmo status chega duas vezes.** O aviso do coordenador e o recarregar do `RunAsync` chegam os dois, em qualquer ordem, e não podem apagar a linha. Um status diferente apaga (Task 3).
5. **Exceção inesperada.** "Sincronizando…" não pode ficar preso na tela (Task 3).

---

### Task 1: ciclo pulado tenta de novo em 5 s

**Files:**
- Modify: `src/SlimeGoals.Core/Sync/SyncScheduler.cs` (`NextWait` e o resumo da classe)
- Test: `tests/SlimeGoals.Core.Tests/Sync/SyncSchedulerTests.cs`

**Interfaces:** nenhuma nova.

- [ ] **Step 1: testes que falham**

```csharp
[Fact]
public void Skipped_retries_after_the_change_delay()
{
    _results.Enqueue(SyncCycleResult.Skipped);
    using var scheduler = Create();
    scheduler.Start();
    _time.Advance(SyncScheduler.ChangeDelay - TimeSpan.FromSeconds(1));
    _runs.Should().Be(1);
    _time.Advance(TimeSpan.FromSeconds(1));
    _runs.Should().Be(2);
}

[Fact]
public void Skipped_keeps_the_failure_count()
{
    _results.Enqueue(SyncCycleResult.Failed);
    _results.Enqueue(SyncCycleResult.Skipped);
    _results.Enqueue(SyncCycleResult.Failed);
    using var scheduler = Create();
    scheduler.Start();
    _time.Advance(SyncScheduler.FirstRetry);
    _time.Advance(SyncScheduler.ChangeDelay);
    _runs.Should().Be(3);
    _time.Advance(TimeSpan.FromSeconds(59));
    _runs.Should().Be(3, "a segunda falha espera 60 s: o pulo não zerou a contagem");
    _time.Advance(TimeSpan.FromSeconds(1));
    _runs.Should().Be(4);
}
```

- [ ] **Step 2:** `dotnet test --project tests/SlimeGoals.Core.Tests -- --filter-method "*Skipped*"`. Os dois falham: hoje `Skipped` espera 15 min.
- [ ] **Step 3:** em `NextWait`, `case SyncCycleResult.Skipped: return ChangeDelay;`, sem tocar em `_failures`. O comentário diz por quê: outro ciclo (o do botão ou o do "Sair") segurava o motor.
- [ ] **Step 4:** a classe inteira de testes passa.
- [ ] **Step 5:** commit `feat(core): ciclo pulado pelo agendador tenta de novo em 5 s`.

### Task 2: `SyncNowAsync` no coordenador

**Files:**
- Modify: `src/SlimeGoals.Sync/ISyncAccount.cs`, `src/SlimeGoals.Sync/SyncCoordinator.cs:140-157`
- Modify (implementam a interface): `tests/SlimeGoals.App.Tests/Account/FakeSyncAccount.cs`, `tools/SlimeGoals.Render/Sample.cs`
- Test: `tests/SlimeGoals.Server.Tests/Account/SyncCoordinatorTests.cs`

**Interfaces:**
- Produces: `Task<SyncCycleResult> ISyncAccount.SyncNowAsync(CancellationToken ct = default)`.
- Produces (falso da tela): `FakeSyncAccount.SyncResult` (padrão `Done`), chamada gravada como `"sync-cycle"`, que respeita `Gate` e `Failure` como as outras.
- Produces (Render): `SampleAccount.SyncResult` (padrão `Done`), que respeita `Hang`.

- [ ] **Step 1: testes que falham.** Em `Start_without_session_is_SignedOut_and_makes_no_request`, antes das conferências da rede: `(await a.Account.SyncNowAsync()).Should().Be(SyncCycleResult.Disabled);`. Testes novos, todos depois de `SignUpAsync(a)` e de `WaitUntilAsync(a.Account, s => s.LastSyncAt is not null)`:
  - `SyncNow_sends_the_queue_and_brings_what_other_devices_sent`:
    - B entra e espera o primeiro ciclo dele;
    - A cria "Ler", e então `a.Time.Advance(1 s)`;
    - `SyncNowAsync` em A dá `Done`, com `Pending == 0` e `LastSyncAt == T0 + 1 s`;
    - `SyncNowAsync` em B dá `Done`, e `b.GoalTitles()` é igual a `"Ler"`.
  - `SyncNow_waits_for_the_scheduler_cycle_and_runs_its_own`:
    - prende o próximo pull com `a.Net.HoldNextRequest("/sync/pull")`, pede o ciclo com `a.Account.SyncNow()` e espera `hold.Held`;
    - conta os `GET /sync/pull` e chama `SyncNowAsync`;
    - `Task.WhenAny(sync, Task.Delay(500 ms))` não é o `sync`;
    - depois de `hold.Release.SetResult()`, o resultado é `Done`, e há exatamente mais um `GET /sync/pull`.
  - `SyncNow_with_the_server_down_fails_and_keeps_the_queue`: com `a.Net.Down = true` e uma meta criada, o resultado é `Failed`. O status fica `SignedIn`, com `Pending == 1` e `LastError == "Sem conexão com o servidor."`.
  - `SyncNow_with_a_revoked_token_ends_the_session_and_keeps_the_queue`:
    - B entra e troca a senha, e A cria uma meta;
    - o resultado é `Unauthorized`;
    - logo depois, o status de A já está `SessionExpired`, com `Pending == 1` e `LastError == Expired`.
- [ ] **Step 2:** declarar o método na interface e no coordenador, com `throw new NotImplementedException()`, e implementar os dois falsos. `dotnet test --project tests/SlimeGoals.Server.Tests -- --filter-method "*SyncNow*" --filter-method "*Start_without_session*"`: os cinco falham com `NotImplementedException`.
- [ ] **Step 3:** extrair `private async Task<SyncCycleResult> RunCycleNowAsync(EngineRun run, CancellationToken ct)` do `FlushAsync` (ciclo, até `FlushAttempts` no `Skipped` com `WaitQuietlyAsync(run.CurrentCycle)` e `ExpireCoreAsync` no `Unauthorized`). `FlushAsync` passa a chamá-lo. `SyncNowAsync` é `RunOpAsync(() => _run is { } run ? RunCycleNowAsync(run, ct) : Task.FromResult(SyncCycleResult.Disabled), ct)`. A documentação da interface diz a diferença para `SyncNow`.
- [ ] **Step 4:** a classe `SyncCoordinatorTests` inteira passa, e o build da solução sai sem avisos.
- [ ] **Step 5:** commit `feat(sync): SyncNowAsync roda um ciclo e devolve o resultado`.

### Task 3: botão e linha de resultado

**Files:**
- Modify: `src/SlimeGoals.App/Account/AccountMessages.cs` (`AccountAction.Sync`)
- Modify: `src/SlimeGoals.App/Account/AccountViewModel.cs`
- Modify: `src/SlimeGoals.App/Views/AccountSectionView.axaml:25-42`
- Modify: `tools/SlimeGoals.Render/Program.cs` (`RenderAccount`)
- Test: `tests/SlimeGoals.App.Tests/Account/AccountViewModelTests.cs`

**Interfaces:**
- Consumes: `ISyncAccount.SyncNowAsync`, `FakeSyncAccount.SyncResult` (Task 2).
- Produces:
  - `IAsyncRelayCommand SyncNowCommand`, com `CanExecute = IsIdle` e incluído no `NotifyCanExecuteChangedFor` de `IsBusy`;
  - `string? SyncMessage { get; private set; }`;
  - as constantes `SyncNowLabel`, `SyncingText`, `SyncDoneText`, `SyncUnfinishedText` e `SyncFailedText`.

- [ ] **Step 1: testes que falham** (status `SignedIn()` e `LoadedAsync()` em todos):
  - `Sync_now_runs_a_cycle_and_says_it_finished`: depois de `SyncNowCommand.ExecuteAsync`, `Calls` contém `"sync-cycle"`, `SyncMessage == "Sincronização concluída."` e `Error` é nulo.
  - `Sync_now_says_when_it_did_not_finish(SyncCycleResult, string)` (Theory): `Incomplete` e `Skipped` mostram o texto de não terminou. `Failed`, `Paused` e `Disabled` mostram o de não foi possível.
  - `Sync_now_that_ends_the_session_shows_no_message`: com `SyncResult = Unauthorized` e o falso já em `SessionExpired`, `IsSessionExpired` é verdadeiro e `SyncMessage` é nulo.
  - `Sync_now_shows_progress_and_blocks_the_other_actions`:
    - com `Gate`, `IsBusy` está ligado, `SyncMessage == "Sincronizando…"` e `SyncNowCommand` e `OpenSignOutCommand` não executam;
    - um `RaiseStatusChanged()` no meio não apaga o "Sincronizando…";
    - depois de soltar o portão, a linha diz que concluiu e o botão volta a executar.
  - `Sync_message_stays_while_the_status_is_the_same`:
    - depois do ciclo, `RaiseStatusChanged()` com o mesmo status mantém a linha;
    - com `Status = SignedIn(pending: 1)` e outro aviso, a linha some.
  - `Sync_message_goes_away_on_reload_and_on_other_pages`: a linha some depois de `LoadCommand.ExecuteAsync`. Ela também some depois de um novo ciclo seguido de `OpenChangePasswordCommand.Execute(null)`.
  - `Sync_now_exception_shows_the_generic_error_and_no_message`: com `Failure = new HttpRequestException()`, `Error == "Algo deu errado. Tente de novo."` e `SyncMessage` é nulo.
- [ ] **Step 2:** os testes falham (compilação: `SyncNowCommand` e `SyncMessage` não existem).
- [ ] **Step 3: implementar.**
  - Os campos `_syncResult` (`SyncCycleResult?`, entregue pelo comando, lido pelo próximo recarregar) e `_syncShownFor` (`AccountStatus?`, o status que a linha descreve).
  - `ClearSyncMessage()` zera os dois e a linha. Ele roda:
    - no início e no `catch` do `RunAsync`;
    - no `LoadAsync`;
    - no `OnPageChanged`.
  - O comando põe "Sincronizando…" e guarda `_syncResult`.
  - Em `RefreshAsync`, depois de `Status = status`:
    - com `_syncResult`, troca a linha pelo texto do resultado e guarda o status em `_syncShownFor`;
    - sem ele, um status diferente de `_syncShownFor` apaga a linha. `AccountStatus` é um `record`, então a comparação é por valor.
  - O texto de cada resultado sai de um `switch` com os textos da spec.
  - Na AXAML:
    - um `TextBlock` com `SyncMessage` no fim do `StackPanel` de status, na cor normal, visível só com texto;
    - `<Button Classes="primary" Content="{x:Static acc:AccountViewModel.SyncNowLabel}" Command="{Binding SyncNowCommand}" />` como primeiro item do `WrapPanel`.
  - Na Render, as imagens `sync-running` (`Hang`), `sync-done` (status sem erro) e `sync-failed` (`SyncResult = Failed`, status com o erro de conexão).
- [ ] **Step 4:** `AccountViewModelTests` passa. Rodar a Render em `account`, ler os PNGs em 320, 360, 900 e 1100 e conferir alinhamento, quebra da fileira e a linha sob as pendências.
- [ ] **Step 5:** commit `feat(app): botão Sincronizar agora na seção Sincronização`.

### Task 4: documentação e conferência final

**Files:**
- Modify: `docs/sincronizacao.md:53` (tira "não há botão de sincronizar" e explica o botão)

- [ ] **Step 1:** procurar em `README.md` e `docs/` outras frases sobre quando sincroniza e alinhar.
- [ ] **Step 2:** `dotnet build SlimeGoals.slnx` sem avisos, `dotnet test --solution SlimeGoals.slnx` todo verde e o build do Android (`SlimeGoals.Android.slnx`).
- [ ] **Step 3: ponta a ponta.**
  - Subir o servidor de desenvolvimento com `--build --wait`.
  - No emulador (`-s emulator-5554`, sempre com alvo), com `adb reverse`, apertar o botão:
    - "Sincronizando…" e depois "Sincronização concluída.", com a hora da última sincronização atualizada;
    - com o servidor parado (`docker compose stop server`, nunca `down -v`), "Não foi possível sincronizar." e a faixa "Sem conexão com o servidor.".
- [ ] **Step 4:** commit `docs: botão Sincronizar agora no guia de sincronização`.
