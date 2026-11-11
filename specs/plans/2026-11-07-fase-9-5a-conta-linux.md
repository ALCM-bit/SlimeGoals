# Fase 9.5a — Conta e sincronização no Linux — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No build de desenvolvimento do Linux, Configurações ganha a seção "Conta e sincronização". Por ela, a pessoa cria conta, confirma, entra, recupera, troca a senha, vê e remove aparelhos, sai (enviando a fila antes) e apaga a conta. Com a conta ligada, o motor da 9.2 sincroniza de verdade com o servidor do Docker Compose, e a tela mostra a última sincronização, as pendências e o erro. O app instalado (Release) continua sem rede e sem a seção.

**Architecture:**
- **Core:** `SyncErrorText` traduz falhas do ciclo para textos de `last_error`; `SyncUnauthorizedException` e o resultado `SyncCycleResult.Unauthorized` separam "token morto" de "rede caiu".
- **`src/SlimeGoals.Sync`:**
  - `HttpSyncTransport` passa a lançar `SyncUnauthorizedException` no `401` e usa `ConfigureAwait(false)`;
  - `AccountService` ganha `ReauthenticateAsync`, e `PendingRegistration` passa a levar o servidor;
  - **`SyncCoordinator`** (novo) implementa `ISyncAccount`: dono do ciclo de vida da sessão, do motor e do agendador. Serializa as operações de conta, roda tudo fora da thread da interface e para o ciclo antes de desligar.
- **Platform.Linux:** `SecretServiceVault` ganha um perfil por banco de dados, para dois apps de teste no mesmo chaveiro não dividirem o segredo.
- **App:** passa a referenciar `SlimeGoals.Sync`. `AccountViewModel` e `AccountView` formam a seção e suas subpáginas dentro de Configurações; `AccountMessages` dá o texto de cada erro; `SyncHost` (um `IAppHostedService`) inicia e para o coordenador.
- **Desktop:** só com a constante `SYNC_DEV` (Debug, ou `-p:SyncDev=true`) registra cofre, coordenador, ViewModel e host, e liga `PlatformCapabilities.Sync`.

**Tech Stack:** as mesmas versões atuais, sem pacote novo. Avalonia 12.1.4, CommunityToolkit.Mvvm 8.4.2, Tmds.DBus.Protocol 0.95.2, xUnit v3, AwesomeAssertions, FakeTimeProvider, Testcontainers.

**Spec:** `specs/Sincronizacao_v1.md`: §2 (RS01, RS02, RS03, RS07, RS08, RS09, RS10), §4 ("Quando roda"), §6, "Decisões da fase 9.4" (todas as obrigações para a 9.5 que não são do Android), §7 (menos o Android). Planos anteriores: `2026-11-07-fase-9-4-cripto-e-conta.md`, `2026-11-06-fase-9-2-motor.md`.

**Fora deste plano (9.5b, Android):**
- cofre sobre o Keystore e os vetores de cripto no emulador e no celular;
- `INTERNET`, `network_security_config.xml` e o APK de desenvolvimento no `build.sh`;
- sincronizar ao voltar ao primeiro plano;
- composição do Android com a conta.

O head Android passa a compilar o `SlimeGoals.Sync` (porque o App o referencia), mas não registra nada dele: `PlatformCapabilities.Sync` fica falso, e a seção não aparece.

## Decisões deste plano (o autor confirma na revisão)

1. **Divisão e builds (decididas pelo autor):** 9.5a é só Linux. A seção aparece só no build de desenvolvimento até a 9.6. Release continua sem rede, sem cofre e sem a seção.
2. **Build de desenvolvimento = constante `SYNC_DEV`** no `SlimeGoals.Desktop.csproj`, definida quando `Configuration` é `Debug` ou quando `SyncDev` é `true`. Sem ela, a composição é idêntica à de hoje. O `packaging/linux/build.sh` publica em Release e não muda.
3. **Telas dentro de Configurações (decidida pelo autor):** a seção fica no topo de Configurações, antes de "Geral". Formulários abrem como subpáginas, como a lista de Licenças. O Voltar fecha um nível por vez.
4. **`App` referencia `SlimeGoals.Sync`.** A tela precisa dos tipos de erro (`AccountApiException`, `KdfParamsException`...) para escolher o texto, e do `PendingRegistration`. Uma porta própria no App duplicaria esses tipos. O App fala com a sincronização só pela interface `ISyncAccount`, e os testes do App usam um falso dela.
5. **Endereço do servidor** fica em `settings` (chave `sync_server`), com padrão `http://127.0.0.1:8080` (o do `compose.yaml`). Só pode ser editado com a conta desligada; com a conta ligada, vale o servidor gravado na sessão do cofre. Aceita só URL absoluta `http` ou `https`, sem consulta nem fragmento. `http` é aceito porque a seção só existe no build de desenvolvimento (RS10); a 9.6 passa a exigir `https` no Release.
6. **Aparelho revogado não perde a fila.** O texto da 9.4 dizia que o aparelho revogado sai e entra de novo, baixando tudo. Mas sair chama `DisableAsync`, que apaga a fila, e as edições offline feitas antes de notar a revogação se perderiam. Em vez disso:
   - o `401` no ciclo vira `SyncUnauthorizedException`, o motor devolve `SyncCycleResult.Unauthorized` e grava "A sessão deste aparelho foi encerrada. Entre de novo." em `last_error`;
   - o coordenador para o motor, descarta a sessão e passa ao estado `SessionExpired`. A fila, `last_seq` e `sync_state.enabled` ficam como estão;
   - a tela pede só a senha. `ReauthenticateAsync` usa o e-mail e o servidor da sessão do cofre, faz `prelogin`, deriva, `login`, abre a chave de dados e regrava o segredo com o token e o id de aparelho novos, sem `EnableAsync`. O motor volta e envia a fila.
   - "Sair" continua disponível nesse estado, com o aviso de que a fila se perde.
   - A chave de dados não muda com a troca de senha, então a fila cifrada com ela continua válida.
7. **Estados da conta** (`AccountState`): `SignedOut`, `SignedIn`, `SessionExpired`, `VaultLocked` (o cofre não abriu ao iniciar: a sessão pode existir, mas não dá para ler) e `SessionUnreadable` (o segredo está corrompido: só "Sair" funciona, como a 9.4 já garante). `SyncState.Enabled` falso e cofre sem segredo é `SignedOut`. Habilitado sem segredo também é `SessionUnreadable`, para a pessoa poder limpar.
8. **Uma operação de conta por vez.** Todas as operações do `ISyncAccount` passam por um `SemaphoreSlim(1)` no coordenador. A segunda espera a primeira, e a tela desabilita os botões enquanto `IsBusy`. Isso resolve a obrigação "dois `login` juntos deixam um aparelho órfão".
9. **Nada pesado na thread da interface.** O coordenador roda cada operação de conta em `Task.Run`, porque o Argon2id leva ~0,5 s e o `AccountService` deriva logo depois do primeiro `await`, que voltaria à thread da interface. O ciclo do motor também roda em `Task.Run`, inclusive quando o pedido vem de `RequestNow` chamado pela tela.
10. **Parar antes de desligar.** O coordenador guarda a `Task` do ciclo em andamento. Para parar: `scheduler.Stop()` (cancela), espera essa `Task` (ignorando o cancelamento e a falha), descarta o agendador, a `SyncSession` e o `HttpClient`. Só então chama `SignOutAsync`, `DeleteAccountAsync` ou passa a `SessionExpired`. Isso cumpre "`DisableAsync` com o ciclo cancelado".
11. **Sair em dois passos** (§7): `FlushAsync` roda um ciclo agora e devolve quantos itens ficaram na fila (`SyncStatus.Pending`). Se ficou 0, a tela sai direto. Se não, mostra o aviso com "Esperar" (volta ao estado normal) e "Sair mesmo assim". Em `SessionExpired`, `VaultLocked` ou `SessionUnreadable`, o ciclo não roda, e o aviso aparece sempre que houver pendências.
12. **Revogar o próprio aparelho:** a lista de aparelhos não mostra o botão no atual (mostra "Este aparelho"), e `RevokeDeviceAsync` com o id atual lança `InvalidOperationException("Para desconectar este aparelho, use Sair.")`.
13. **Sem cancelar depois do servidor:** as subpáginas não têm botão de cancelar durante a operação. O Voltar fica desabilitado enquanto `IsBusy`.
14. **Perfil do cofre por banco.** `SecretServiceVault` recebe um `profile` (o mesmo hash do caminho do banco que a instância única já usa) e o grava como atributo `profile`. Sem isso, dois apps de teste com `XDG_DATA_HOME` diferentes no mesmo login dividiriam o `sync-session`, e o segundo a entrar sobrescreveria o primeiro. A procura usa `application`, `name` e `profile`.
15. **Textos de `last_error`** (obrigação da 9.3) vêm de `SyncErrorText.For(Exception)`. O detalhe técnico vai só para o Trace:

    | Falha | Texto |
    |---|---|
    | `SyncUnauthorizedException` | A sessão deste aparelho foi encerrada. Entre de novo. |
    | `HttpRequestException` sem `StatusCode` | Sem conexão com o servidor. |
    | `HttpRequestException` 5xx | O servidor está com problemas. O app tenta de novo sozinho. |
    | `HttpRequestException` 413 | Um registro passou do tamanho aceito pelo servidor. |
    | `HttpRequestException` 403 | A conta chegou ao limite de registros do servidor. |
    | `HttpRequestException` 429 | Muitos pedidos ao servidor. O app tenta de novo daqui a pouco. |
    | outro `HttpRequestException` | O servidor recusou a sincronização (código N). |
    | `TimeoutException`, `OperationCanceledException` que não é do chamador | O servidor demorou para responder. |
    | `InvalidOperationException` do próprio motor (página que não avança, página vazia com `has_more`) | O servidor respondeu de um jeito inesperado. |
    | qualquer outra | Erro inesperado na sincronização. |

    A pausa por versão nova continua com "Atualize o app para continuar sincronizando.".
16. **Textos das telas.** Copiar exatamente:
    - Explicação antes do cadastro e da entrada: "Com uma conta, suas metas e registros vão cifrados para o servidor e aparecem nos seus outros aparelhos. Ninguém além de você consegue lê-los, nem quem cuida do servidor. Por isso, se você perder a senha e o código de recuperação, a cópia da nuvem se perde. Os dados deste aparelho continuam."
    - Código de recuperação: título "Seu código de recuperação"; texto "Guarde este código num lugar seguro. Ele é a única forma de recuperar a conta se você esquecer a senha, e não será mostrado de novo."; caixa "Guardei o código"; botão "Continuar" só habilitado com a caixa marcada; botão "Copiar".
    - Confirmação: "Enviamos um código para {e-mail}. Neste servidor de desenvolvimento, o código aparece no log do servidor (`docker compose logs server`)."
    - Aviso de sair: "Há {N} alteração(ões) que ainda não foram enviadas. Se sair agora, elas não chegam aos outros aparelhos, e metas excluídas depois disso podem voltar quando você entrar de novo." Botões "Esperar" e "Sair mesmo assim".
    - Apagar conta: "Isto apaga a conta e todos os dados dela no servidor, para todos os aparelhos. As metas e registros deste aparelho continuam aqui." Pede a senha; botão "Apagar conta".
    - Estado ligado: "Conectado como {e-mail}", "Servidor: {endereço}", "Última sincronização: {hoje às HH:mm | dd/MM às HH:mm | ainda não}", "Pendências: {N}", e o `last_error` quando houver.
    - `SessionExpired`: "A sessão deste aparelho foi encerrada (a senha mudou ou o aparelho foi removido). Entre de novo para continuar sincronizando. Nada do que está na fila se perde." Campo de senha, botão "Entrar de novo".
    - `VaultLocked`: "O chaveiro do sistema está travado ou indisponível. Destrave-o para continuar sincronizando." Botão "Tentar de novo".
    - `SessionUnreadable`: "Os dados da sessão neste aparelho estão ilegíveis. Saia da conta e entre de novo."
17. **Textos dos erros de conta** (`AccountMessages.For(Exception, AccountAction)`):

    | Erro | Texto |
    |---|---|
    | `AccountApiException(Unauthorized)` em Login, Reauth, DeleteAccount, ChangePassword | E-mail ou senha incorretos. (ChangePassword e DeleteAccount: "Senha incorreta.") |
    | `AccountApiException(Unauthorized)` em Confirm | Código inválido ou vencido. |
    | `AccountApiException(Unauthorized)` em Recover | E-mail ou código de recuperação incorretos. |
    | `AccountApiException(InvalidToken)` | A sessão deste aparelho foi encerrada. Entre de novo. |
    | `AccountApiException(RateLimited)` | Muitas tentativas. Tente de novo em {RetryAfter arredondado para cima, em s} s. (sem `RetryAfter`: "daqui a um minuto") |
    | `AccountApiException(Unavailable)` | Não foi possível falar com o servidor. Confira o endereço e a conexão. |
    | `AccountApiException(InvalidRequest, TooLarge)` | O servidor recusou os dados. Confira os campos. |
    | `AccountApiException(NotFound)` | Não encontrado no servidor. Atualize a lista. |
    | `KdfParamsException` | O servidor pediu parâmetros de segurança fora do aceito. Nada foi enviado. |
    | `SecretVaultUnavailableException` | O chaveiro do sistema não está disponível ou o pedido foi recusado. Sem ele, a sincronização não liga. |
    | `FormatException` (código de recuperação) | Código de recuperação inválido. Confira os 26 caracteres. |
    | `CryptographicException` (Recover) | Este código não abre a conta. Confira se é o desta conta. |
    | `ArgumentException`, `InvalidOperationException` | a própria `Message` (já em pt-BR) |
    | qualquer outra | Algo deu errado. Tente de novo. (detalhe no Trace) |

    Senha nova e confirmação diferentes: "As senhas não são iguais.", conferido pela tela antes de chamar o serviço.
18. **Nome do aparelho:** campo nos formulários de entrar, confirmar e recuperar, preenchido com `Environment.MachineName` (o head passa o valor por `AccountDefaults`).
19. **README e Sobre:** o README ganha "Sincronização (em desenvolvimento)", explicando como subir o Compose e rodar o build de desenvolvimento. O texto do Sobre não muda no Release; no build de desenvolvimento, a seção da conta já explica (decisão 16).

## Global Constraints

- Identificadores em inglês; textos e comentários em pt-BR. `TreatWarningsAsErrors` ligado.
- "Sem conta, o app não faz nenhuma chamada de rede" (RS02): sem sessão, o coordenador não cria `HttpClient` nem agendador. Release não registra nada de sincronização.
- "Uma falha de sincronização nunca bloqueia nem atrasa o uso do app" (RS08): nenhuma exceção do ciclo chega à tela; operações de conta mostram o texto e devolvem o controle.
- "Sem cofre disponível, a sincronização não liga: nunca em arquivo sem proteção" (§6).
- Core continua sem I/O e sem pacote novo (`ArchitectureTests`).
- `SlimeGoals.Sync` e `SlimeGoals.Sync.Contracts` entram no `SlimeGoals.Android.slnx` (o App passa a referenciá-los). O head Android não registra nada deles.
- Testes nunca usam o banco real, o app instalado, o chaveiro nem o barramento de verdade do usuário.
- **Verificação:** `dotnet test --solution SlimeGoals.slnx` (Docker ligado), `dotnet build SlimeGoals.slnx -c Release` e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, sem avisos.
- **Commits:** Conventional Commits em português, escopos `sync`, `app`, `linux`, `desktop`, `specs`, com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Branch `feat/fase-9-5a-conta-linux`, a partir da `main`.

## Review Focus

1. O aparelho é revogado (troca de senha em outro aparelho) enquanto tem edições offline na fila: elas chegam ao servidor depois de "Entrar de novo", sem baixar tudo e sem duplicar (Tarefa 3).
2. A pessoa clica "Sair" com um ciclo em andamento e a rede lenta: nenhum pedido sai depois do `DisableAsync`, e nenhum item volta à fila depois de limpa (Tarefa 3).
3. Dois cliques rápidos em "Entrar" (ou entrar em uma janela enquanto apaga a conta noutra subpágina): um só aparelho aparece no servidor (Tarefa 3).
4. O chaveiro está travado ao abrir o app, e a pessoa recusa o diálogo: o app abre normal, mostra `VaultLocked`, e "Tentar de novo" funciona depois de destravar (Tarefas 3 e 6, esta à mão).
5. Servidor fora do ar ou endereço errado durante entrar, confirmar e recuperar: texto claro, botões reabilitados, nada ligado nem gravado no cofre (Tarefas 3 e 4).

---

### Task 1: Erros do ciclo com texto próprio e 401 separado

**Files:**
- Create: `src/SlimeGoals.Core/Sync/SyncErrorText.cs`
- Modify: `src/SlimeGoals.Core/Sync/SyncModels.cs` (`SyncUnauthorizedException`), `src/SlimeGoals.Core/Sync/SyncEngine.cs`, `src/SlimeGoals.Sync/HttpSyncTransport.cs`
- Test: `tests/SlimeGoals.Core.Tests/Sync/SyncErrorTextTests.cs`, `tests/SlimeGoals.Core.Tests/Sync/SyncEngineTests.cs` (casos novos), `tests/SlimeGoals.Server.Tests/Sync/HttpSyncTransportTests.cs` (ou o arquivo de transporte existente)

**Interfaces:**
- Produces:
  - `public sealed class SyncUnauthorizedException() : Exception("O servidor recusou o token deste aparelho.")` em `SlimeGoals.Core.Sync`.
  - `SyncCycleResult.Unauthorized` (novo valor, no fim do enum).
  - `public static class SyncErrorText { public static string For(Exception e); }`, com a tabela da decisão 15.
  - `SyncEngine`: `SyncUnauthorizedException` não é tratada como falha comum: grava o texto da tabela em `last_error` e devolve `Unauthorized`. Todas as outras falhas gravam `SyncErrorText.For(e)`; o `Trace` continua com a exceção inteira.
  - `HttpSyncTransport`: `401` em `pull` ou `push` lança `SyncUnauthorizedException`; todos os `await` com `ConfigureAwait(false)`.

- [ ] **Step 1: Escrever os testes**
  - `For_maps_each_failure_to_its_text` (Theory): uma linha por linha da tabela da decisão 15, inclusive `HttpRequestException(null, null, HttpStatusCode.BadRequest)` → "O servidor recusou a sincronização (código 400)."
  - `Unauthorized_transport_returns_Unauthorized_and_records_text`: transporte falso que lança `SyncUnauthorizedException` no `pull`; resultado `Unauthorized`; `GetStatusAsync().LastError` igual ao texto.
  - `Network_failure_records_friendly_text_not_raw_message`: transporte falso lança `HttpRequestException("Connection refused")`; `LastError` é "Sem conexão com o servidor." e não contém "refused".
  - `Revoked_token_throws_SyncUnauthorizedException` (servidor real): entra, revoga o aparelho por outro, `PullAsync` e `PushAsync` lançam `SyncUnauthorizedException`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --project tests/SlimeGoals.Core.Tests` e `dotnet test --project tests/SlimeGoals.Server.Tests`. Esperado: falha de compilação nos tipos novos.
- [ ] **Step 3: Implementar.** Em `SyncEngine.RunCycleAsync`, um `catch (SyncUnauthorizedException)` antes do `catch (Exception)`. A detecção de "`InvalidOperationException` do próprio motor" usa um tipo interno (`SyncProtocolException : InvalidOperationException`) lançado nos dois pontos do `PullAsync`, para não confundir com outras.
- [ ] **Step 4: Rodar:** passa, e os testes existentes do motor continuam verdes (os que conferem `LastError` cru passam a conferir o texto novo).
- [ ] **Step 5: Commit** `feat(sync): grava erros do ciclo com texto próprio e separa o token recusado`

### Task 2: Entrar de novo sem perder a fila, e o servidor no cadastro pendente

**Files:**
- Modify: `src/SlimeGoals.Sync/Account/AccountService.cs`, `src/SlimeGoals.Sync/Account/PendingRegistration.cs`
- Test: `tests/SlimeGoals.Server.Tests/Account/AccountServiceTests.cs`

**Interfaces:**
- Produces:
  - `PendingRegistration.Server: Uri` (o `server` do `AccountService` que o criou). `ConfirmAsync` lança `ArgumentException("Este cadastro é de outro servidor.")` se não bater.
  - `AccountService.ReauthenticateAsync(string password, CancellationToken ct = default): Task`. Exige segredo legível no cofre e `SyncState.Enabled`; sem eles, `InvalidOperationException("Entre na conta antes.")`. Usa o e-mail da sessão e o `server` do serviço (o coordenador cria o serviço com o servidor da sessão). Faz `prelogin`, confere `kdf_params`, deriva, `login`, abre a chave de dados e regrava o segredo com o novo token e o novo `device_id` do servidor. Não chama `EnableAsync` nem `DisableAsync`. Falha depois do `login`: revoga o token novo e deixa o segredo antigo como estava.

- [ ] **Step 1: Escrever os testes**
  - `Reauthenticate_after_revocation_keeps_queue_and_state`: entra com A; muda a senha por B (A fica revogado); edita uma meta em A (fila com 1 item, `last_seq` = N); `ReauthenticateAsync(nova senha)`; `SyncState` igual (enabled, `last_seq` = N), fila ainda com o item, segredo com token novo, e um `push` com a sessão nova é aceito.
  - `Reauthenticate_with_wrong_password_changes_nothing`: segredo e estado intactos; lança `AccountApiException(Unauthorized)`.
  - `Reauthenticate_without_session_is_refused`.
  - `Reauthenticate_failing_after_login_revokes_the_new_token`: cofre que falha no `SetAsync`; o segredo antigo continua; o servidor não lista aparelho novo ativo.
  - `Confirm_with_pending_from_other_server_is_refused`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --project tests/SlimeGoals.Server.Tests`. Esperado: falha de compilação.
- [ ] **Step 3: Implementar.** Reaproveitar `DeriveForAccountAsync` e o trecho de `EnterAsync` que faz `login`, abre a chave e grava o segredo; extrair para um método comum, com um parâmetro que diz se liga o estado.
- [ ] **Step 4: Rodar:** passa, com os testes antigos do `AccountService` verdes.
- [ ] **Step 5: Commit** `feat(sync): entra de novo depois da revogação sem perder a fila`

### Task 3: `SyncCoordinator`

**Files:**
- Create: `src/SlimeGoals.Sync/ISyncAccount.cs`, `src/SlimeGoals.Sync/SyncCoordinator.cs`
- Test: `tests/SlimeGoals.Server.Tests/Account/SyncCoordinatorTests.cs`

**Interfaces:**
- Consumes: Tarefa 1 (`SyncCycleResult.Unauthorized`), Tarefa 2 (`ReauthenticateAsync`, `PendingRegistration.Server`), `AccountService`, `SyncSession`, `SyncEngine`, `SyncScheduler`, `HttpSyncTransport`.
- Produces (em `SlimeGoals.Sync`):
  ```csharp
  public enum AccountState { SignedOut, SignedIn, SessionExpired, VaultLocked, SessionUnreadable }
  public sealed record AccountStatus(AccountState State, string? Email, Uri? Server, DateTimeOffset? LastSyncAt,
      int Pending, string? LastError);
  public interface ISyncAccount
  {
      event EventHandler? StatusChanged;            // qualquer thread
      Task<AccountStatus> GetStatusAsync(CancellationToken ct = default);
      Task StartAsync(CancellationToken ct = default);
      Task StopAsync();
      Task<PendingRegistration> RegisterAsync(Uri server, string email, string password, CancellationToken ct = default);
      Task ConfirmAsync(PendingRegistration pending, string code, string deviceName, CancellationToken ct = default);
      Task LoginAsync(Uri server, string email, string password, string deviceName, CancellationToken ct = default);
      Task<string> RecoverAsync(Uri server, string email, string recoveryCode, string newPassword, string deviceName, CancellationToken ct = default);
      Task ReauthenticateAsync(string password, CancellationToken ct = default);
      Task RetryVaultAsync(CancellationToken ct = default);
      Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default);
      Task<int> FlushAsync(CancellationToken ct = default);
      Task SignOutAsync(CancellationToken ct = default);
      Task DeleteAccountAsync(string password, CancellationToken ct = default);
      Task<IReadOnlyList<DeviceDto>> ListDevicesAsync(CancellationToken ct = default);
      Task RevokeDeviceAsync(Guid id, CancellationToken ct = default);
      void SyncNow();
  }
  public sealed class SyncCoordinator(ISecretVault vault, ISyncStateStore state, ISyncLocalStore store,
      IRemoteDeletionGate gate, DataChangeNotifier changes, TimeProvider time, Func<Uri, HttpClient> createHttp)
      : ISyncAccount, IAsyncDisposable
  ```
  - `createHttp(server)` devolve um `HttpClient` com `BaseAddress = server`; o coordenador é dono dele e o descarta ao parar.
  - Decisões 6 a 13 valem aqui. `StatusChanged` dispara depois de cada ciclo, de cada operação e de cada mudança de estado.
  - `StartAsync`: lê `SyncState` e `OpenSessionAsync`. `SecretVaultUnavailableException` → `VaultLocked`; `InvalidDataException`, ou ligado sem segredo → `SessionUnreadable`; sessão → liga o motor (`SignedIn`); senão `SignedOut`. Nunca lança.
  - Ciclo: `runCycle = ct => Task.Run(() => engine.RunCycleAsync(ct))`, com a `Task` guardada. `Unauthorized` → para o motor (fora do próprio ciclo, sob o portão) e vai a `SessionExpired`.
  - `FlushAsync`: com o motor ligado, roda `engine.RunCycleAsync` (se der `Skipped`, espera o ciclo em andamento e roda de novo) e devolve `store.GetStatusAsync().Pending`. Sem motor, só devolve `Pending`.
  - `DeleteAccountAsync`: para o motor; se o servidor recusar, liga o motor de novo e relança.
  - `RevokeDeviceAsync(id atual)` → decisão 12. O id atual é o `SyncSession.DeviceId` da sessão aberta; sem sessão aberta (fora de `SignedIn`), a lista e a revogação lançam `InvalidOperationException("Entre na conta antes.")`.

- [ ] **Step 1: Escrever os testes** (servidor real via `ServerFactory`, `createHttp` usando o handler do `WebApplicationFactory`; SQLite de teste; `InMemorySecretVault`; `FakeTimeProvider` para o agendador)
  - `Start_without_session_is_SignedOut_and_makes_no_request`: `createHttp` nunca é chamado.
  - `Login_starts_syncing_and_reports_status`: depois do primeiro ciclo, `SignedIn`, `LastSyncAt` preenchido, `Pending` 0.
  - `Start_with_session_resumes_sync`: segundo coordenador sobre o mesmo banco e cofre baixa o que outro aparelho enviou.
  - `Start_with_locked_vault_is_VaultLocked_and_retry_recovers`: cofre indisponível → `VaultLocked`; disponível + `RetryVaultAsync` → `SignedIn`.
  - `Start_with_unreadable_secret_is_SessionUnreadable_and_sign_out_clears`.
  - `Revocation_moves_to_SessionExpired_and_reauth_sends_the_queue` (Review Focus 1): A e B entram; B troca a senha; A edita offline; ciclo de A → `SessionExpired`, fila intacta; `ReauthenticateAsync(nova)` → `SignedIn`, e B recebe a edição de A; nenhuma meta duplicada em B.
  - `SignOut_waits_for_the_running_cycle_before_disabling` (Review Focus 2): handler HTTP que segura o `pull` até ser liberado; `SignOutAsync` só termina depois do ciclo cancelado; depois dele, o handler não recebe mais pedidos e a fila está vazia.
  - `Concurrent_logins_create_one_device` (Review Focus 3): dois `LoginAsync` simultâneos; um passa, o outro lança `InvalidOperationException("Saia da conta antes de entrar em outra.")`; `ListDevicesAsync` tem 1 item.
  - `Flush_reports_pending_left_when_server_is_down`: `createHttp` com handler que falha; `FlushAsync` devolve o número de itens na fila.
  - `Revoking_the_current_device_is_refused`.
  - `Failed_login_leaves_nothing_on` (Review Focus 5): servidor recusa; estado `SignedOut`, cofre vazio, nenhum agendador criado.
  - `Account_operations_run_off_the_caller_thread`: num `SynchronizationContext` de thread única que falha se for usado depois do primeiro `await`, `LoginAsync` termina sem tocar nele.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --project tests/SlimeGoals.Server.Tests`. Esperado: falha de compilação.
- [ ] **Step 3: Implementar** com um único `SemaphoreSlim _ops` para operações, início e parada. O descarte (`DisposeAsync`) chama `StopAsync`.
- [ ] **Step 4: Rodar:** passa. Rodar a classe 5 vezes seguidas para conferir que não depende de tempo.
- [ ] **Step 5: Commit** `feat(sync): coordena a conta, a sessão e o motor`

### Task 4: Endereço do servidor, textos e `AccountViewModel`

**Files:**
- Create: `src/SlimeGoals.App/Account/AccountViewModel.cs`, `src/SlimeGoals.App/Account/AccountMessages.cs`, `src/SlimeGoals.App/Account/ServerAddress.cs`, `src/SlimeGoals.App/Account/AccountDefaults.cs`
- Modify: `src/SlimeGoals.App/SlimeGoals.App.csproj` (referência a `SlimeGoals.Sync`), `SlimeGoals.Android.slnx` (Sync e Sync.Contracts), `src/SlimeGoals.App/Hosting/PlatformCapabilities.cs` (`bool Sync = false`, parâmetro opcional no fim), `src/SlimeGoals.Core/Services/AppSettings.cs` (`GetSyncServerAsync`/`SetSyncServerAsync`, chave `sync_server`), `src/SlimeGoals.App/ViewModels/SettingsPageViewModel.cs`
- Create: `tests/SlimeGoals.App.Tests/Account/FakeSyncAccount.cs`, `AccountViewModelTests.cs`, `AccountMessagesTests.cs`, `ServerAddressTests.cs`; caso novo em `tests/SlimeGoals.Core.Tests` para o `AppSettings`

**Interfaces:**
- Consumes: `ISyncAccount`, `AccountStatus`, `AccountState` (Tarefa 3).
- Produces:
  - `ServerAddress.TryParse(string text, out Uri? server, out string? error): bool`. Erro: "Informe um endereço http:// ou https:// completo.".
  - `AccountMessages.For(Exception e, AccountAction action): string`, com `enum AccountAction { Register, Confirm, Login, Recover, Reauthenticate, ChangePassword, SignOut, DeleteAccount, Devices }` e a tabela da decisão 17.
  - `AccountDefaults(string DeviceName)`.
  - `AccountViewModel(ISyncAccount account, AppSettings settings, AccountDefaults defaults)`:
    - `AccountPage Page` (`None, Register, RecoveryCode, Confirm, Login, Recover, RecoveredCode, Reauthenticate, ChangePassword, Devices, SignOut, DeleteAccount`), `bool IsBusy`, `string? Error`, `AccountStatus Status`, os campos de cada formulário e os textos da decisão 16 já formatados;
    - comandos `Open{Página}`, `Submit` (um por página, com o nome dela) e `bool TryGoBack()` (falso em `None`; com `IsBusy`, verdadeiro sem mudar nada);
    - ao receber `StatusChanged`, recarrega o status na thread da interface (`Dispatcher` injetável por `Action<Action>`, para teste).
  - `SettingsPageViewModel` recebe `AccountViewModel? account = null`; `ShowAccount => account is not null && capabilities.Sync`; `IsMainOpen` falso quando `account.Page != None`; `TryGoBack` pergunta primeiro ao `account`.

- [ ] **Step 1: Escrever os testes** (com `FakeSyncAccount`, que grava as chamadas e devolve o que o teste mandar)
  - `ServerAddress` (Theory): aceita `http://127.0.0.1:8080`, `https://sync.exemplo.com/`; recusa vazio, `ftp://x`, `127.0.0.1:8080`, `http://x/?a=1`, `http://x/#y`.
  - `AccountMessages` (Theory): uma linha por linha da tabela da decisão 17, inclusive `RateLimited` com 2,1 s → "… em 3 s." e o `Unauthorized` em cada ação.
  - `Register_flow_shows_code_then_requires_checkbox_then_confirm`: `SubmitRegister` → `Page == RecoveryCode` com o código do pending; `ContinueCommand.CanExecute` falso até marcar "Guardei o código"; depois `Confirm`; `SubmitConfirm` chama `ConfirmAsync` com o mesmo pending e volta a `None`.
  - `Mismatched_passwords_are_caught_before_calling_the_service`.
  - `Server_field_is_read_only_while_signed_in` e `Invalid_server_blocks_submit_with_error`.
  - `Failure_shows_text_and_reenables_buttons` (Review Focus 5): o falso lança `AccountApiException(Unavailable)`; `Error` igual ao texto; `IsBusy` falso; página continua.
  - `Back_is_ignored_while_busy`.
  - `Sign_out_with_pending_shows_warning_and_wait_keeps_session`: `FlushAsync` devolve 3; `Page == SignOut` com "Há 3 alterações…"; "Esperar" volta a `None` sem `SignOutAsync`; "Sair mesmo assim" chama `SignOutAsync`.
  - `Sign_out_without_pending_leaves_directly`.
  - `Devices_list_hides_revoke_for_current_device`.
  - `Session_expired_offers_reauth_and_sign_out`.
  - `Settings_hides_account_without_capability` e `Settings_back_closes_account_page_first`.
  - `AppSettings_sync_server_defaults_to_local_compose`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --project tests/SlimeGoals.App.Tests` e `dotnet test --project tests/SlimeGoals.Core.Tests`.
- [ ] **Step 3: Implementar.** Toda chamada ao `ISyncAccount` passa por um único método `RunAsync(AccountAction, Func<Task>)` que liga `IsBusy`, limpa e preenche `Error` e captura a exceção. Os textos ficam como constantes no `AccountViewModel` e em `AccountMessages`, iguais às decisões 16 e 17.
- [ ] **Step 4: Rodar:** passa. `dotnet build SlimeGoals.Android.slnx …` também, sem avisos.
- [ ] **Step 5: Commit** `feat(app): adiciona a conta e a sincronização em Configurações`

### Task 5: Telas da conta

**Files:**
- Create: `src/SlimeGoals.App/Views/AccountView.axaml` (+ `.axaml.cs`), uma `DataTemplate` ou `Panel` por página
- Modify: `src/SlimeGoals.App/Views/SettingsView.axaml`, `src/SlimeGoals.App/Styles/Controls.axaml` (só se faltar estilo), `tools/SlimeGoals.Render/Program.cs` e `Sample.cs` (modo `account`)
- Test: `tests/SlimeGoals.App.Tests/Views/AccountViewTests.cs` (se o projeto já testa Views em Headless; senão, só o Render)

**Interfaces:**
- Consumes: `AccountViewModel` (Tarefa 4).

- [ ] **Step 1:** No `SettingsView`, a seção "Conta e sincronização" vem antes de "Geral", visível por `ShowAccount`, e o conteúdo das subpáginas fica no mesmo lugar onde a lista de Licenças abre, com o título da página ("Criar conta", "Entrar", "Recuperar conta", "Código de recuperação", "Confirmar e-mail", "Entrar de novo", "Trocar senha", "Aparelhos", "Sair da conta", "Apagar conta").
- [ ] **Step 2:** Campos de senha com `PasswordChar="•"` e `RevealPassword` desligado; o código de recuperação em `SelectableTextBlock` monoespaçado, com o botão "Copiar" (área de transferência do `TopLevel`). Botões desabilitados por `IsBusy`, com `ProgressBar IsIndeterminate` visível enquanto ocupado. Erro em vermelho, abaixo do formulário. Toque de 44 px, como o resto do app.
- [ ] **Step 3:** `Render -- <pasta> account` gera PNG de cada estado (`SignedOut`, `SignedIn` com erro, `SessionExpired`, `VaultLocked`) e de cada subpágina, a 360 e a 1100 px, com um `FakeSyncAccount` de exemplo. Conferir que nada corta nem transborda a 320 px (`narrow`).
- [ ] **Step 4:** `dotnet test --solution SlimeGoals.slnx` verde.
- [ ] **Step 5: Commit** `feat(app): desenha as telas da conta`

### Task 6: Composição do Linux, cofre por perfil, documentos e verificação

**Files:**
- Modify: `src/SlimeGoals.Platform.Linux/Secrets/SecretServiceVault.cs` (`profile`), `src/SlimeGoals.Desktop/SlimeGoals.Desktop.csproj` (`SyncDev`, `SYNC_DEV`, referência a `SlimeGoals.Sync`), `src/SlimeGoals.Desktop/CompositionRoot.cs`, `README.md`, `specs/Sincronizacao_v1.md` ("Decisões da fase 9.5a")
- Create: `src/SlimeGoals.App/Hosting/SyncHost.cs`
- Test: `tests/SlimeGoals.Platform.Linux.Tests/SecretServiceVaultTests.cs` (caso novo), `tests/SlimeGoals.App.Tests/Hosting/SyncHostTests.cs`

**Interfaces:**
- Consumes: `ISyncAccount` (Tarefa 3), `AccountViewModel` (Tarefa 4).
- Produces:
  - `SecretServiceVault(Func<Task<DBusConnection>> connect, string profile)` e `SecretServiceVault.ForSessionBus(string profile)`.
  - `SyncHost(ISyncAccount account) : IAppHostedService`: `StartAsync` chama `account.StartAsync()`; `StopAsync` chama `account.StopAsync()`, com o prazo curto que o `IAppHostedService` já prevê.
  - `CompositionRoot`, só com `#if SYNC_DEV`: `ISecretVault` (perfil = hash do caminho do banco, o mesmo da instância única), `SyncCoordinator` como `ISyncAccount` (singleton, com `createHttp` = `new HttpClient { BaseAddress = server, Timeout = TimeSpan.FromSeconds(30) }`), `AccountDefaults(Environment.MachineName)`, `AccountViewModel`, `SyncHost`, e `PlatformCapabilities.Desktop with { Sync = true }`. O `IRemoteDeletionGate` é o `ReminderService` já registrado.

- [ ] **Step 1: Escrever os testes**
  - `Profiles_do_not_share_the_secret` (chaveiro isolado): dois cofres com perfis diferentes gravam `sync-session`; cada um lê o seu.
  - `SyncHost_starts_and_stops_the_account` (com `FakeSyncAccount`).
- [ ] **Step 2: Rodar e ver falhar**; **Step 3: implementar**; **Step 4: rodar e passar.**
- [ ] **Step 5: Conferir o Release.** `dotnet build SlimeGoals.slnx -c Release` sem avisos; `dotnet run --project src/SlimeGoals.Desktop -c Release` com `XDG_DATA_HOME` e `XDG_CONFIG_HOME` temporários: Configurações não mostra a conta. (Registrar no relatório.)
- [ ] **Step 6: Documentos.**
  - README: seção "Sincronização (em desenvolvimento)": `docker compose up -d`, `dotnet run --project src/SlimeGoals.Desktop` (Debug), onde ver o código de confirmação (`docker compose logs server`), e como simular dois aparelhos (`XDG_DATA_HOME` diferente em cada um).
  - Spec, "Decisões da fase 9.5a": decisões 2, 5, 6, 7, 8, 11, 12 e 14 em uma ou duas frases cada; corrigir o parágrafo da 9.4 que dizia que o revogado "sai e entra", apontando para a decisão 6.
- [ ] **Step 7: Verificação completa** (Global Constraints), e `git grep -n "SlimeGoals.Sync" -- src/SlimeGoals.Android` vazio.
- [ ] **Step 8: Commit** `feat(desktop): liga a conta e a sincronização no build de desenvolvimento` e `docs(specs): registra as decisões da fase 9.5a`

### Verificação manual do autor (depois da Tarefa 6)

Com `docker compose up -d` e dois apps de desenvolvimento (`XDG_DATA_HOME=/tmp/sg-a` e `/tmp/sg-b`, `dotnet run --project src/SlimeGoals.Desktop`):

1. Criar conta em A: ver o aviso, guardar o código, confirmar com o código do log. A mostra "Conectado como…".
2. Entrar com a mesma conta em B: as metas de A aparecem em B. Editar em B; a edição chega a A em até ~20 s com A aberto.
3. Trocar a senha em B: A passa a "Entrar de novo" no ciclo seguinte. Criar um registro em A antes de entrar de novo; depois de entrar, ele chega a B.
4. Parar o servidor (`docker compose stop server`), editar em A, clicar "Sair": aparece o aviso com as pendências; "Esperar" mantém a conta.
5. Chaveiro do GNOME (obrigação da 9.4): travar o chaveiro ("Senhas e chaves" → Travar), abrir A: aparece `VaultLocked`; "Tentar de novo" abre o diálogo de verdade. Recusar mantém `VaultLocked`; aceitar volta a sincronizar.
6. Recuperar a conta em A com o código: B passa a "Entrar de novo".
7. Apagar a conta em A: as metas ficam em A; B passa a "Entrar de novo" e o login dele falha com "E-mail ou senha incorretos."
