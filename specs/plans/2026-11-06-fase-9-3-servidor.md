# Fase 9.3 — Servidor — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Um servidor ASP.NET Core com PostgreSQL, rodando em Docker Compose, cria contas, autentica aparelhos e guarda blobs opacos seguindo a §5 da spec. O motor da 9.2 sincroniza por ele exatamente como pelo servidor falso.

**Architecture:**
- **`src/SlimeGoals.Sync.Contracts`:** DTOs e contexto JSON gerado, compartilhados por servidor e cliente. Sem dependências.
- **`src/SlimeGoals.Server`:** minimal APIs, Npgsql + Dapper, migrações SQL próprias, autenticação por token de aparelho.
- **`src/SlimeGoals.Sync`:** `HttpSyncTransport`, que implementa `ISyncTransport`. Nenhum projeto do app o referencia ainda, então o app continua sem rede (RS02).
- **Testes:** `tests/SlimeGoals.Server.Tests`, com PostgreSQL real em contêiner. Uma suíte de contrato roda igual contra o `FakeSyncServer` e contra o servidor real, e é ela que mantém os dois iguais.

**Tech Stack:** ASP.NET Core 10, Npgsql 10.0.3, Dapper 2.1.89, Testcontainers.PostgreSql 4.16.0, Microsoft.AspNetCore.Mvc.Testing 10.0.14, imagens `postgres:18-alpine` e `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`.

**Spec:** `specs/Sincronizacao_v1.md`, §2 (RS04, RS05, RS06), §5, §6 (só o que o servidor guarda), §9 (Servidor) e §10 (linha 9.3).

**Fora deste plano:** criptografia e telas de cadastro (9.4), cliente das rotas `/auth` (9.4), ligar o motor ao app, `INTERNET` e TLS no cliente (9.5), e-mail real, domínio e backup (9.6). A busca no dump do banco por título, unidade e nota (§9 Privacidade) depende da cifra real e fica para a 9.4.

## Pré-requisito do autor

O SDK desta máquina não tem o ASP.NET Core: um projeto web falha com `NETSDK1226`. Antes da execução, rode:

```
sudo pacman -S aspnet-runtime-10.0 aspnet-targeting-pack-10.0
```

Depois disso, `dotnet test --solution SlimeGoals.slnx` passa a exigir também o Docker ligado, por causa do PostgreSQL em contêiner.

## Decisões deste plano (o autor confirma na revisão)

1. **Hash da `auth_key` e do código é SHA-256, sem Argon2id no servidor.** O custo de testar senhas já está no Argon2id do aparelho, e a `auth_key` tem 256 bits. Um hash lento no servidor só abriria espaço para negação de serviço. A comparação usa `CryptographicOperations.FixedTimeEquals`.
2. **Token do aparelho:** 256 bits aleatórios, enviado em `Authorization: Bearer`, guardado como SHA-256 em `devices.token_hash`. Não expira: dura até sair, revogar, trocar senha ou recuperar. `last_seen_at` é atualizado no máximo uma vez por hora.
3. **`accounts` ganha colunas (acréscimo à spec):** `purge_point bigint NOT NULL DEFAULT 0`, `confirm_hash bytea`, `confirm_expires_at timestamptz`. `next_seq` começa em 1, e o maior `seq` da conta é `next_seq - 1`.
4. **Exclusão é explícita no envio (ajuste à spec).** O item leva `delete: true` e `blob` nulo. Um cliente com defeito que omita o blob recebe `400` em vez de apagar um registro.
5. **Recuperar tem duas etapas (ajuste à spec).** `POST /auth/recover/begin` confere o código e devolve `wrapped_key_recovery`, sem a qual o aparelho não abre a chave de dados. `POST /auth/recover` define a nova senha e revoga todos os aparelhos. O código de recuperação continua o mesmo.
6. **Cadastro não revela quem tem conta.** E-mail já usado recebe a mesma resposta `202`, sem alterar nada. Conta não confirmada some depois de 24 h, para ninguém reservar o e-mail de outra pessoa. Até confirmar, a conta é tratada como inexistente: `prelogin` devolve o sal falso, e `login` responde `401` mesmo com a chave certa. Sem isso, cadastrar com chave própria e tentar entrar revelaria se o e-mail estava livre.
7. **Apagar a conta pede a `auth_key` no corpo, além do token.** Um token roubado lê os blobs cifrados, mas não apaga a cópia da nuvem.
8. **E-mail simulado:** `IEmailSender` com `LogEmailSender`, que só existe em `Development`. É a exceção à regra 8 da §5, prevista na §10. Fora de `Development`, o servidor recusa iniciar sem um envio real.
9. **Leitura do `pull` numa transação `REPEATABLE READ`, somente leitura.** Ponto da limpeza e registros vêm do mesmo instante. Com leituras separadas, uma limpeza entre as duas faria um aparelho perder marcas sem receber `410`.
10. **Limpeza por conta, com a linha da conta travada.** `candidato = maior seq das marcas com deleted_at anterior ao corte + 1`; `ponto = MAX(purge_point atual, MIN(candidato, next_seq - 1))`. Remove marcas anteriores ao corte com `seq` abaixo do ponto. Roda uma vez por dia em `MaintenanceService`.
11. **Cota:** só `Sync:MaxRecordsPerAccount`, nula por padrão. Passar dela recusa o lote inteiro com `403 quota_exceeded`.
12. **`limit` acima do máximo é reduzido, não recusado.** `has_more` continua correto, então o aparelho não percebe diferença.
13. **Valores que a spec não fixa:** `limit` padrão 200 e máximo 500; lote máximo 100 itens; blob máximo 64 KiB; corpo máximo 10 MiB; `/auth` com 20 pedidos por minuto por IP e 10 por minuto por e-mail; 10 falhas de `auth_key` por minuto por conta; `record_key` de 1 a 128 caracteres; e-mail até 254; `auth_key` e chave de recuperação com 32 bytes; `kdf_salt` de 16 a 64 bytes; `kdf_params` até 256 caracteres; chaves cifradas até 256 bytes; token de confirmação vale 24 h.
14. **Sal falso:** `HMAC-SHA-256(Server:PreloginSecret, e-mail)`, cortado em 16 bytes, com `Server:DefaultKdfParams` igual a `{"v":1,"m":65536,"t":3,"p":1}`.
15. **E-mail normalizado:** `Trim()` e `ToLowerInvariant()` antes de qualquer uso, com índice único na coluna.

## Respostas de erro

Corpo `{"error":"<código>"}`, sempre em minúsculas, sem detalhes internos.

| Status | Código | Quando |
|---|---|---|
| 400 | `invalid_request` | Campo ausente, formato ou tamanho errado, `limit` ou `after` inválidos, lote acima do máximo |
| 400 | `invalid_token` | Confirmação desconhecida ou vencida |
| 401 | `unauthorized` | Token ausente, desconhecido ou revogado; credencial errada; e-mail desconhecido ou não confirmado no login |
| 403 | `quota_exceeded` | Cota da conta |
| 404 | `not_found` | Aparelho que não existe nesta conta |
| 410 | `reset_required` | Regra 5 da §5 |
| 413 | `too_large` | Blob ou corpo acima do máximo |
| 429 | `rate_limited` | Limite de taxa, com `Retry-After` |

## Global Constraints

- Identificadores em inglês; textos e comentários em pt-BR. `TreatWarningsAsErrors` ligado.
- "O servidor nunca recebe a senha, a chave de dados nem conteúdo legível" (RS04): blobs e chaves cifradas são bytes opacos.
- "Toda consulta filtra pela conta da sessão, nunca por parâmetro do cliente" (regra 6). A conta vem só de `ClaimsPrincipal`.
- "Logs. Nunca registram e-mail completo, token ou blob" (regra 8).
- Nenhum segredo no repositório: `.env` já está no `.gitignore`, e só `.env.example` entra.
- Porta do servidor publicada só em `127.0.0.1`; PostgreSQL sem porta publicada.
- `SlimeGoals.Server`, `SlimeGoals.Sync` e `SlimeGoals.Sync.Contracts` não entram em `SlimeGoals.Android.slnx`, e nenhum projeto do app os referencia.
- Core continua sem I/O (`ArchitectureTests`) e sem mudança de comportamento.
- Testes nunca usam o banco real nem o app instalado.
- **Verificação:** `dotnet test --solution SlimeGoals.slnx` e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, sem avisos.
- **Commits:** Conventional Commits em português, escopos `server`, `sync`, `build`, `specs`, com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Branch `feat/fase-9-3-servidor`, a partir da `main`.

## Review Focus

1. Limpeza entre a leitura do ponto e a dos registros: o aparelho recebe `410` ou recebe as marcas, nunca nenhum dos dois (Tarefa 5).
2. Dois envios simultâneos da mesma chave: um aceito, outro em conflito, sem buraco em `seq` (Tarefa 4).
3. Aceite perdido e reenvio: o segundo envio dá conflito, e o registro não duplica nem gasta `seq` (Tarefa 4).
4. Token revogado com o aparelho em uso: o pedido seguinte recebe `401`, sem cache (Tarefa 3).
5. Dois servidores iniciando juntos no mesmo banco: a migração roda uma vez, sem erro (Tarefa 1).

---

### Task 1: Projetos, banco e fixture de testes

**Files:**
- Create: `src/SlimeGoals.Sync.Contracts/SlimeGoals.Sync.Contracts.csproj`, `src/SlimeGoals.Server/SlimeGoals.Server.csproj` (`Sdk="Microsoft.NET.Sdk.Web"`), `src/SlimeGoals.Server/Program.cs`, `ServerOptions.cs`, `appsettings.json`, `Data/Db.cs`, `Data/ServerMigrator.cs`, `Data/Migrations/001_initial.sql`
- Create: `tests/SlimeGoals.Server.Tests/SlimeGoals.Server.Tests.csproj`, `PostgresFixture.cs`, `ServerFactory.cs`, `Data/ServerMigratorTests.cs`, `HealthTests.cs`
- Modify: `Directory.Packages.props`, `SlimeGoals.slnx`

**Interfaces:**
- Produces: `public partial class Program`; `ServerOptions` com `Server`, `Sync`, `Limits`, `Retention`, `Maintenance`, ligado à configuração; `Db.OpenAsync(CancellationToken): Task<NpgsqlConnection>`; `ServerMigrator.RunAsync(CancellationToken): Task`; `PostgresFixture` como `[assembly: AssemblyFixture]`, com `ConnectionString`; `ServerFactory(PostgresFixture, IDictionary<string,string?>? overrides = null) : WebApplicationFactory<Program>`, com `FakeTimeProvider Time`, `Maintenance:Enabled=false` e limites altos.

- [ ] **Step 1: Escrever os testes**
  - `Migrator_creates_the_tables_and_is_idempotent`: duas execuções; `accounts`, `devices`, `records` e `schema_version` existem; versão 1.
  - `Two_migrators_at_once_run_the_script_once`: `Task.WhenAll` de dois `RunAsync` num banco novo; nenhum lança; uma linha em `schema_version`.
  - `Health_answers_ok_with_the_database_up`: `GET /health` dá `200`.
  - `Health_answers_503_without_the_database`: conexão apontando para porta fechada, sem migrar na partida; `503`.
  - `Production_refuses_to_start_without_the_prelogin_secret`: ambiente `Production`, `Server:PreloginSecret` vazio; a partida lança `OptionsValidationException`.
- [ ] **Step 2: Rodar e ver falhar:** `dotnet test --project tests/SlimeGoals.Server.Tests/SlimeGoals.Server.Tests.csproj`, falha de compilação.
- [ ] **Step 3: Implementar.** `001_initial.sql` com as tabelas da §5 e a decisão 3, `records.blob bytea NULL`, `ON DELETE CASCADE` a partir de `accounts`, índice único `accounts(email)`, índice `records(account_id, seq)` e índice parcial `records(deleted_at) WHERE deleted_at IS NOT NULL`. `ServerMigrator` usa `pg_advisory_xact_lock` e scripts embutidos, rodando na partida do `Program`, salvo com `Server:MigrateOnStart=false`.
- [ ] **Step 4: Rodar:** os testes passam, e `dotnet build SlimeGoals.slnx` sai sem avisos.
- [ ] **Step 5: Commit** `feat(server): cria o servidor, o banco e os testes com PostgreSQL`

### Task 2: Cadastro, entrada e token de aparelho

**Files:**
- Create: `src/SlimeGoals.Sync.Contracts/AuthContracts.cs`, `SyncJsonContext.cs`
- Create: `src/SlimeGoals.Server/Accounts/AccountEndpoints.cs`, `AccountStore.cs`, `SecretHash.cs`, `IEmailSender.cs`, `LogEmailSender.cs`, `Auth/DeviceTokenHandler.cs`, `Auth/CurrentDevice.cs`
- Test: `tests/SlimeGoals.Server.Tests/Accounts/RegisterTests.cs`, `LoginTests.cs`, `TestAccount.cs`

**Interfaces:**
- Consumes: `Db`, `ServerFactory`.
- Produces: DTOs `PreloginRequest(Email)`, `PreloginResponse(KdfSalt, KdfParams)`, `RegisterRequest(Email, KdfSalt, KdfParams, AuthKey, RecoveryKey, WrappedKeyPassword, WrappedKeyRecovery)`, `ConfirmRequest(Token)`, `LoginRequest(Email, AuthKey, DeviceName)`, `LoginResponse(Token, DeviceId, WrappedKeyPassword)`; `SyncJsonContext` com `SnakeCaseLower`; `ClaimsPrincipal.AccountId()` e `.DeviceId()`; `IEmailSender.SendConfirmationAsync(string email, string token, CancellationToken)`; ajudante de teste `TestAccount.CreateAsync(ServerFactory): Task<TestAccount>`, com `Email`, `AuthKey`, `Client` já autenticado e `LoginAsync(string device)`; `FakeEmailSender` registrado pela fábrica, com `LastToken`.

- [ ] **Step 1: Escrever os testes**
  - `Prelogin_returns_the_salt_of_the_account`, `Prelogin_of_unknown_email_is_stable_and_has_the_same_shape`: o mesmo e-mail dá o mesmo sal; 16 bytes; os `kdf_params` da decisão 14.
  - `Register_answers_202_and_sends_confirmation`.
  - `Register_with_used_email_answers_202_and_changes_nothing`: `kdf_salt` e `auth_hash` ficam iguais; nenhum e-mail novo.
  - `Email_is_normalized`: entrar com `"  Ana@Exemplo.COM "` acha a conta de `ana@exemplo.com`.
  - `Confirm_with_unknown_or_expired_token_answers_400`: avança `Time` em 24 h e 1 s.
  - `Login_before_confirming_answers_401`, `Login_with_wrong_key_answers_401`, `Login_with_unknown_email_answers_401`: corpo igual ao da chave errada.
  - `Login_creates_a_device_and_returns_the_wrapped_key`: em `devices`, `token_hash` difere do token e é SHA-256 dele.
  - `Stored_account_has_no_plain_secret`: `auth_hash` difere de `auth_key`; `recovery_hash` difere de `recovery_key`.
  - `Logout_revokes_only_this_device`: dois aparelhos; o primeiro sai e recebe `401`; o segundo segue com `200` em `GET /devices` (rota da Tarefa 3, então aqui usa uma rota de teste protegida registrada pela fábrica).
  - `Request_without_token_answers_401`.
  - `Production_refuses_to_start_with_only_the_log_email_sender`: ambiente `Production` com o segredo preenchido; a partida lança (decisão 8).
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** `POST /auth/prelogin`, `/auth/register`, `/auth/confirm`, `/auth/login`, `/auth/logout`, com a tabela de erros, as decisões 1, 2, 6, 8, 13, 14 e 15, e `DeviceTokenHandler`, que consulta `devices` a cada pedido (sem cache) e recusa `revoked_at` preenchido.
- [ ] **Step 4: Rodar:** os testes passam.
- [ ] **Step 5: Commit** `feat(server): adiciona cadastro, entrada e token de aparelho`

### Task 3: Senha, recuperação, aparelhos e apagar conta

**Files:**
- Modify: `src/SlimeGoals.Sync.Contracts/AuthContracts.cs`, `src/SlimeGoals.Server/Accounts/AccountEndpoints.cs`, `AccountStore.cs`
- Test: `tests/SlimeGoals.Server.Tests/Accounts/PasswordTests.cs`, `RecoverTests.cs`, `DevicesTests.cs`, `DeleteAccountTests.cs`

**Interfaces:**
- Consumes: `TestAccount`, `ClaimsPrincipal.AccountId()`.
- Produces: DTOs `ChangePasswordRequest(CurrentAuthKey, AuthKey, KdfSalt, KdfParams, WrappedKeyPassword)`, `RecoverBeginRequest(Email, RecoveryKey)`, `RecoverBeginResponse(WrappedKeyRecovery)`, `RecoverRequest(Email, RecoveryKey, AuthKey, KdfSalt, KdfParams, WrappedKeyPassword)`, `DeviceDto(Id, Name, LastSeenAt, IsCurrent)`, `DeleteAccountRequest(AuthKey)`.

- [ ] **Step 1: Escrever os testes**
  - `Change_password_with_wrong_current_key_answers_401_and_changes_nothing`.
  - `Change_password_revokes_other_devices_and_keeps_this_one`: a chave antiga deixa de entrar e a nova entra.
  - `Recover_begin_returns_the_wrapped_key_only_with_the_right_code`: errado dá `401`, igual a e-mail desconhecido.
  - `Recover_sets_the_password_and_revokes_all_devices`: o código continua válido depois.
  - `Devices_lists_only_this_account`: a outra conta não aparece; `is_current` só no aparelho do pedido.
  - `Revoked_device_gets_401_on_the_next_request`.
  - `Device_of_another_account_answers_404_and_stays_active`.
  - `Delete_account_with_wrong_key_answers_401_and_keeps_everything`.
  - `Delete_account_removes_records_and_devices`: contagem 0 nas três tabelas para a conta; o e-mail pode se cadastrar de novo.
  - `Last_seen_is_updated_at_most_once_per_hour`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** `POST /auth/password`, `/auth/recover/begin`, `/auth/recover`, `GET /devices`, `DELETE /devices/{id}`, `DELETE /account`, com as decisões 5 e 7.
- [ ] **Step 4: Rodar:** os testes passam.
- [ ] **Step 5: Commit** `feat(server): adiciona troca de senha, recuperação e gestão de aparelhos`

### Task 4: Envio, leitura e transporte HTTP

**Files:**
- Create: `src/SlimeGoals.Sync.Contracts/SyncContracts.cs`, `src/SlimeGoals.Server/Sync/SyncEndpoints.cs`, `RecordStore.cs`
- Create: `src/SlimeGoals.Sync/SlimeGoals.Sync.csproj`, `HttpSyncTransport.cs`
- Modify: `SlimeGoals.slnx`
- Test: `tests/SlimeGoals.Server.Tests/Sync/SyncContractTests.cs`, `FakeServerContractTests.cs`, `RealServerContractTests.cs`, `RecordStoreTests.cs`, `HttpSyncTransportTests.cs`

**Interfaces:**
- Consumes: `ISyncTransport`, `FakeSyncServer`, `TestAccount`.
- Produces: DTOs `RecordDto(Key, Seq, Blob)`, `PullResponse(Records, HasMore)`, `PushItemDto(RecordKey, BaseSeq, Blob, Delete)`, `PushRequest(Items)`, `AcceptedDto(Key, Seq)`, `PushResponse(Accepted, Conflicts)`; `RecordStore.PullAsync(Guid account, long after, int limit, bool full, CancellationToken)` e `PushAsync(Guid account, IReadOnlyList<PushItemDto> items, CancellationToken)`; `HttpSyncTransport(HttpClient http, Func<CancellationToken, ValueTask<string?>> token) : ISyncTransport`, que traduz `410` para `SyncResetRequiredException` e qualquer outra falha para `HttpRequestException` com `StatusCode`; `abstract class SyncContractTests` com `abstract Task<ISyncTransport> NewAccountAsync()` e `abstract Task PurgeBeforeAsync(ISyncTransport account, long seq)`. Nos testes de limpeza, `seq` é sempre o da marca mais nova a limpar mais 1, valor em que a fórmula da decisão 10 e o `PurgeBefore` do falso coincidem.

- [ ] **Step 1: Escrever a suíte de contrato**, herdada pelas duas classes, com blobs de bytes fixos:
  - `Pull_of_empty_account_is_empty`: sem registros, `HasMore` falso.
  - `Accepted_items_get_increasing_seq_without_gaps`: três itens, `seq` 1, 2, 3.
  - `Pull_pages_in_seq_order`: cinco itens, `limit` 2, páginas de 2, 2 e 1; `HasMore` verdadeiro, verdadeiro, falso.
  - `Stale_base_is_a_conflict_and_the_rest_is_accepted`: `Conflicts` traz a chave; o item recusado não gasta `seq`.
  - `Repeated_send_after_lost_ack_is_a_conflict_and_does_not_duplicate`.
  - `Absent_key_with_base_above_zero_is_a_conflict`, para `upsert` e `delete`.
  - `Delete_with_base_zero_of_absent_key_creates_a_mark`: blob nulo, `seq` novo.
  - `Delete_keeps_a_mark_with_a_new_seq_and_no_blob`.
  - `Edit_raises_the_seq_and_the_old_seq_no_longer_appears`.
  - `Same_key_twice_in_a_batch_accepts_the_first_only`.
- [ ] **Step 2: Escrever os testes do servidor real**
  - `Concurrent_pushes_leave_no_gaps`: 8 envios paralelos de 25 itens em uma conta; `seq` é exatamente 1 a 200.
  - `Concurrent_push_of_the_same_key_accepts_one`: 8 envios com `base_seq` 0 da mesma chave; um aceito, sete em conflito, `next_seq` igual a 2.
  - `Pull_during_pushes_never_skips_a_seq`: leitura em laço com `after` avançando durante os envios; todos os `seq` são vistos, em ordem.
  - `Account_never_reads_or_changes_another_account`: mesma `record_key` nas duas contas; cada uma vê só a sua; `base_seq` de uma não vale na outra.
  - `Deleted_record_has_no_blob_in_the_database`: consulta direta, `blob` nulo e `deleted_at` preenchido (RS05).
  - `Item_with_blob_and_delete_or_with_neither_answers_400`.
  - `Pull_with_negative_after_or_zero_limit_answers_400`, `Pull_limit_above_the_maximum_is_reduced`.
  - `Transport_maps_410_to_reset_and_401_to_http_failure`.
- [ ] **Step 3: Rodar e ver falhar:** a suíte do `FakeSyncServer` já passa, e a do servidor real falha.
- [ ] **Step 4: Implementar** `GET /sync/pull` e `POST /sync/push`. `PushAsync`, numa transação: `SELECT next_seq ... FOR UPDATE` da conta, lê os `seq` atuais com `record_key = ANY(@keys)`, percorre os itens em ordem mantendo esses valores na memória, grava e atualiza `next_seq`. Exclusão zera `blob` e grava `deleted_at`. `upsert` limpa `deleted_at`, como o servidor falso aceita.
- [ ] **Step 5: Rodar:** as duas suítes passam. Divergência entre elas é defeito, nunca motivo para relaxar um teste.
- [ ] **Step 6: Commit** `feat(server): adiciona envio e leitura de registros` e `feat(sync): adiciona o transporte HTTP`

### Task 5: Limpeza de marcas e `410`

**Files:**
- Create: `src/SlimeGoals.Server/Sync/RecordPurger.cs`, `MaintenanceService.cs`
- Modify: `src/SlimeGoals.Server/Sync/RecordStore.cs`, `SyncEndpoints.cs`
- Test: `tests/SlimeGoals.Server.Tests/Sync/SyncContractTests.cs`, `RecordPurgerTests.cs`, `RealServerContractTests.cs`

**Interfaces:**
- Consumes: `RecordStore`, `SyncContractTests.PurgeBeforeAsync`.
- Produces: `RecordPurger.RunAsync(CancellationToken): Task<PurgeResult>`, com `PurgeResult(int Marks, int Accounts)`; `internal Func<Task>? RecordStore.AfterPointRead`, ponto de teste entre as duas leituras. Na classe do servidor real, `PurgeBeforeAsync` envelhece as marcas com `seq` abaixo do pedido e chama `RunAsync`.

- [ ] **Step 1: Acrescentar à suíte de contrato**
  - `Purge_answers_reset_below_the_point_only_without_full`: `Pull(after abaixo, full: false)` lança `SyncResetRequiredException`; `full: true` devolve os vivos; `after` igual ao ponto não lança.
  - `Purge_answers_reset_at_zero_after_a_purge`.
  - `Account_without_purge_never_answers_reset`.
  - `Purge_keeps_live_records_and_the_newest_record`: a marca mais nova da conta continua.
  - `Full_read_reaches_the_purge_point`: ler tudo com `full` e repetir `Pull(último seq, full: false)` não lança.
- [ ] **Step 2: Escrever os testes do servidor**
  - `Purge_removes_only_marks_older_than_180_days`: com 179 dias a marca fica, com 181 sai.
  - `Purge_never_passes_the_highest_seq`: conta cuja última escrita é uma marca velha; `purge_point` igual ao maior `seq`, e a marca fica.
  - `Purge_point_never_goes_back`.
  - `Purge_does_not_touch_other_accounts`.
  - `Purge_removes_unconfirmed_accounts_after_24_hours`: a confirmada fica; o e-mail da removida pode se cadastrar de novo.
  - `Pull_sees_reset_or_marks_when_a_purge_runs_between_reads`: `AfterPointRead` roda a limpeza; o resultado traz as marcas, ou lança reset, nunca uma página sem elas.
  - `Maintenance_survives_a_failed_run`: uma falha vai para o log e o laço continua.
- [ ] **Step 3: Rodar e ver falhar.**
- [ ] **Step 4: Implementar** as decisões 9 e 10, com `purge_point` lido na transação do `pull`. `MaintenanceService` roda a cada 24 h, e `Maintenance:Enabled` o desliga.
- [ ] **Step 5: Rodar:** as duas suítes passam.
- [ ] **Step 6: Commit** `feat(server): limpa marcas antigas e responde 410`

### Task 6: Limites e logs

**Files:**
- Modify: `src/SlimeGoals.Server/Program.cs`, `Accounts/AccountEndpoints.cs`, `Sync/SyncEndpoints.cs`, `Sync/RecordStore.cs`
- Create: `src/SlimeGoals.Server/Limits/AuthRateLimits.cs`
- Test: `tests/SlimeGoals.Server.Tests/Limits/RateLimitTests.cs`, `SizeLimitTests.cs`, `QuotaTests.cs`, `LogPrivacyTests.cs`

**Interfaces:**
- Consumes: `ServerFactory(overrides)`, `TestAccount`.
- Produces: nada usado por outras tarefas.

- [ ] **Step 1: Escrever os testes**
  - `Auth_routes_answer_429_past_the_ip_limit`: limite 3 na configuração; o quarto dá `429` com `Retry-After`.
  - `Login_answers_429_past_the_email_limit_from_any_ip`, com `Limits:AuthPerIpPerMinute` alto.
  - `Limit_resets_after_a_minute`.
  - `Sync_routes_have_no_auth_rate_limit`.
  - `Wrong_auth_key_answers_429_past_the_account_limit`: em `DELETE /account` e `POST /auth/password`, com o limite por IP alto; depois do limite, até a chave certa recebe `429`, e o teste confirma que a conta segue intacta.
  - `Blob_above_64_KiB_answers_413_and_saves_nothing`, `Batch_above_100_items_answers_400`, `Body_above_10_MiB_answers_413`.
  - `Quota_is_off_by_default`, `Batch_past_the_quota_answers_403_and_saves_nothing`, `Edit_at_the_quota_is_accepted`: editar um registro existente não aumenta a conta.
  - `Logs_never_contain_email_token_or_blob`: `ILoggerProvider` de teste em nível `Trace`, percorrendo cadastro, entrada, envio, leitura e erros; nenhuma linha contém o e-mail, o token, a `auth_key` em base64 ou o blob em base64. A fábrica usa aqui o `FakeEmailSender`, não o `LogEmailSender`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** Limite por IP com `AddRateLimiter` em janela fixa, no grupo `/auth`. Limite por e-mail com `PartitionedRateLimiter`, chamado no início de login, prelogin, cadastro e recuperação, com a chave vinda do SHA-256 do e-mail normalizado. Limite por conta de falhas na `auth_key` (10 por minuto, `Limits:AuthKeyFailuresPerAccountPerMinute`), consultado antes de comparar a chave em `DELETE /account` e `POST /auth/password`: sem ele, quem tem um token roubado testaria senhas sem o limite do login. Corpo máximo em `/sync/push` por `IHttpMaxRequestBodySizeFeature`, e 64 KiB nas rotas de `/auth`; corpo acima do máximo responde `413 too_large`, inclusive onde hoje `BadHttpRequestException` vira `400`. Registrar e-mail só por máscara (`a***@exemplo.com`), quando for preciso.
- [ ] **Step 4: Rodar:** os testes passam.
- [ ] **Step 5: Commit** `feat(server): aplica limites de taxa, tamanho e cota`

### Task 7: Motor contra o servidor real

**Files:**
- Modify: `tests/SlimeGoals.Infrastructure.Tests/Sync/SimulatedDevice.cs` (construtor passa a receber `ISyncTransport`), `tests/SlimeGoals.Server.Tests/SlimeGoals.Server.Tests.csproj` (referência à Infrastructure e `Compile Include` com `Link` de `Sync/SimulatedDevice.cs` e `TestDatabase.cs`, da Infrastructure.Tests)
- Test: `tests/SlimeGoals.Server.Tests/Sync/EngineOnRealServerTests.cs`

**Interfaces:**
- Consumes: `SimulatedDevice`, `HttpSyncTransport`, `PlainSyncCrypto`, `RecordPurger`, `TestAccount`.
- Produces: nada usado por outras tarefas.

- [ ] **Step 1: Trocar o tipo no construtor de `SimulatedDevice`** e confirmar que `ConvergenceTests` continua passando sem outra mudança. `DeviceTransport` já embrulha um `ISyncTransport`, então só o parâmetro muda.
- [ ] **Step 2: Escrever os testes**, com dois ou três aparelhos numa conta real. `SettleAsync` é privado em `ConvergenceTests` e lê `server.Records`, então esta classe tem o seu: o mesmo laço, com `latest` vindo de `next_seq - 1` no banco.
  - `Devices_converge_after_editing_offline`: edições nas duas pontas e registros nas duas; depois de `SettleAsync`, `Snapshot` igual.
  - `New_device_receives_everything`.
  - `Deletion_beats_a_concurrent_edit_and_erases_the_blob`: no banco, `blob` nulo.
  - `Conflict_in_a_batch_resolves_in_the_next_cycle`.
  - `Device_behind_the_purge_resyncs_and_stops_asking`: `410` uma vez e, nos ciclos seguintes, nenhum pedido com `full`.
  - `Empty_account_removes_nothing_from_a_device_that_only_sent`: apaga os registros da conta direto no banco, sem limpeza; o aparelho mantém tudo.
  - `Revoked_device_fails_the_cycle_and_keeps_its_queue`: ciclo `Failed`, `last_error` preenchido, fila igual.
- [ ] **Step 3: Rodar.** O código de produção já existe, então uma falha aqui é divergência real entre o servidor e o motor: achar a causa e corrigir o servidor. Mudar o motor exige registrar o motivo.
- [ ] **Step 4: Commit** `test(server): prova o motor contra o servidor real`

### Task 8: Docker Compose, documentos e verificação

**Files:**
- Create: `src/SlimeGoals.Server/Dockerfile`, `compose.yaml`, `.dockerignore`, `.env.example`
- Modify: `specs/Sincronizacao_v1.md`, `README.md`, `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: tudo acima.
- Produces: `docker compose up` com a API em `http://127.0.0.1:8080`.

- [ ] **Step 1: Escrever o Dockerfile:** build em duas etapas, `sdk:10.0` e `aspnet:10.0-alpine`, com `USER app`. O contexto é a raiz do repositório.
- [ ] **Step 2: Escrever `compose.yaml`:** serviços `db` (`postgres:18-alpine`, volume nomeado, `pg_isready`) e `server` (`depends_on` com `service_healthy`, `ASPNETCORE_ENVIRONMENT=Development`, porta `127.0.0.1:8080:8080`). `POSTGRES_PASSWORD` e `SERVER_PRELOGIN_SECRET` vêm do `.env`, com `${VAR:?}` para falhar se faltarem. `.env.example` traz nomes sem valores reais.
- [ ] **Step 3: Verificar de ponta a ponta com um `.env` temporário:**

```
docker compose up -d --build --wait
curl -fsS http://127.0.0.1:8080/health
docker compose logs server
docker compose down -v
```

Esperado: `/health` responde `200`, o log mostra a migração aplicada e o processo roda como `app`. Cadastrar uma conta por `curl`, pegar o token no log, confirmar, entrar e fazer um `push` e um `pull`. Reiniciar só o `server` e ver os dados ainda lá.

- [ ] **Step 4: Atualizar `specs/Sincronizacao_v1.md`:** colunas novas de `accounts` e índices (decisão 3); `delete` explícito no envio (decisão 4); rota `/auth/recover/begin` (decisão 5); cadastro sem revelar conta e remoção da não confirmada (decisão 6); `auth_key` para apagar a conta (decisão 7); exceção do log em `Development` (decisão 8); leitura em `REPEATABLE READ` e fórmula da limpeza (decisões 9 e 10); tabela de erros; valores da decisão 13. Registrar em §3 ou §5 a consequência aceita: uma meta excluída pode voltar por um aparelho parado há mais de 180 dias, se quem excluiu saiu da conta.
- [ ] **Step 5: Atualizar `README.md`** com como subir o servidor local, e `THIRD-PARTY-NOTICES.md` com o Npgsql, se a imagem o distribuir.
- [ ] **Step 6: Rodar a verificação completa** da seção Global Constraints, e confirmar que `git grep -n "SlimeGoals.Sync" -- src/SlimeGoals.App src/SlimeGoals.Android src/SlimeGoals.Desktop` não encontra nada.
- [ ] **Step 7: Commit** `build(server): adiciona Docker Compose` e `docs(specs): registra as decisões da fase 9.3`
