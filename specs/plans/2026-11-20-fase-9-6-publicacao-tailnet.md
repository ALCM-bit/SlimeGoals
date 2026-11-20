# Fase 9.6: publicação na tailnet. Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** o app de Release, no Linux e no Android, sincroniza com o servidor numa VM própria, alcançável só pela tailnet, por HTTPS com o certificado `.ts.net`.

**Architecture:** o `compose.yaml` endurecido continua como está. Um `compose.tailnet.yaml` por cima troca a entrada do Caddy: só a porta 443 do IP da tailnet, com o certificado lido de arquivos que um script com timer do systemd pede ao `tailscale cert` na VM. No app, a conta deixa de depender de `SYNC_DEV`, que fica só para os extras de desenvolvimento do Android. O motor ganha a recusa de sessão `http://` fora do loopback, e a tela Hoje ganha o aviso de sincronização parada.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit v3 (Microsoft Testing Platform), AwesomeAssertions, `FakeTimeProvider`, Docker Compose 2.24+ (`!override`), Caddy 2, Tailscale, systemd, bash.

**Spec:** `specs/Publicacao_na_tailnet_v1.md` (aprovada em 20/11/2026). Vale junto com `specs/Endurecimento_do_servidor_v1.md` e `specs/Sincronizacao_v1.md`.

## Global Constraints

- `TreatWarningsAsErrors` ligado: `dotnet build SlimeGoals.slnx` sem nenhum aviso. Android compila por `SlimeGoals.Android.slnx`.
- Testes: `dotnet test --solution SlimeGoals.slnx`. Os do servidor (inclusive `SyncCoordinatorTests`) precisam do Docker rodando.
- Conventional Commits em português, com escopo por camada: `core`, `infra`, `app`, `linux`, `desktop`, `android`, `sync`, `server`, `specs`, `plans`, `build`.
- TDD: comportamento novo começa por um teste que falha.
- **Repositório público:** nenhum nome de tailnet, IP `100.x`, serial de aparelho, e-mail ou segredo real em código, teste, documento ou commit. Exemplos usam `servidor.exemplo.ts.net` e `100.64.0.10`.
- Segurança primeiro: nada de segredo, token, chave, e-mail completo ou dado interno do servidor em respostas, logs, mensagens de erro ou telas; mínimo privilégio em cada peça; erro genérico para a pessoa, detalhe só no Trace.
- O contêiner do Caddy nunca monta o socket do `tailscaled` e monta o certificado só para leitura (spec §2).
- Nada escuta em `0.0.0.0` no `compose.tailnet.yaml`; não existe porta 80 publicada nele.
- Nada do Compose de produção muda: redes `edge`/`data`, IP fixo do Caddy `10.89.0.2`, `Server__TrustedProxyNetworks__0: 10.89.0.2/32`, segredos em arquivo, `x-hardening`.
- `Email:Mode=log` continua recusado fora de `Development` (T9).
- Aviso de sincronização parada: texto exato `Não sincroniza há 3 dias. Confira a conexão com o servidor.`; prazo de 3 dias numa constante (T15).
- Dica do campo do servidor no Release: `https://servidor.exemplo.ts.net` (T11).
- Mudança visual é conferida na Render (`dotnet run --project tools/SlimeGoals.Render -- /tmp/render all`) em várias larguras, lendo os PNGs.
- Não abrir, apagar nem migrar `~/.local/share/SlimeGoals/`. Rodar o app só com `XDG_DATA_HOME` e `XDG_CONFIG_HOME` em `/tmp/slimegoals-dev`.
- `adb` sempre com `-s <serial>`. A chave de assinatura (`e88d…`) não muda; atualização do Galaxy só com `install -r`.
- `.env` e `secrets/` não são exibidos nem copiados. `docker compose down -v` só com pedido explícito.

## Review Focus

1. **Sair de uma sessão `http://` fora do loopback não faz nenhuma chamada de rede**: o token iria em texto puro. Teste na Tarefa 1 (`Sign_out_from_an_insecure_session_makes_no_request`).
2. **Conta ligada e `LastSyncAt` nulo** (acabou de entrar, primeiro ciclo ainda não terminou): sem aviso, e nada quebra. Teste na Tarefa 3 (`No_warning_before_the_first_successful_sync`).
3. **Relógio para trás** (`LastSyncAt` no futuro): sem aviso, sem exceção. Teste na Tarefa 3 (`No_warning_when_the_last_sync_is_in_the_future`).
4. **`tailscale cert` falha** (VM fora da tailnet, sem login): os arquivos atuais ficam intactos e o Caddy não recarrega. Teste na Tarefa 6 (`cert_failure_keeps_files_and_does_not_reload`).
5. **O `!override` não pega** e o Compose soma as portas do `compose.yaml` (80 e 443 em `0.0.0.0`): o CI falha. Conferência na Tarefa 5.

---

### Task 1: O motor recusa sessão gravada com `http://` fora do loopback (T12)

**Files:**
- Modify: `src/SlimeGoals.Sync/ISyncAccount.cs` (enum `AccountState`)
- Modify: `src/SlimeGoals.Sync/SyncCoordinator.cs` (`OpenAsync`, `SignOutAsync`)
- Test: `tests/SlimeGoals.Server.Tests/Account/SyncCoordinatorTests.cs`

**Interfaces:**
- Produces: `AccountState.InsecureSession`, depois de `SessionUnreadable`, com o doc "A sessão gravada aponta para http:// fora do loopback: o motor não liga, e só sair funciona, sem rede." Em `InsecureSession`, `AccountStatus.Email` e `AccountStatus.Server` vêm da sessão lida.

- [ ] **Step 1: Escrever os testes que falham**

Num helper do próprio arquivo de teste, `RewriteSessionServerAsync(Device d, Uri server)`: lê `sync-session` do cofre falso, desserializa com `SessionSecret.FromBytes` (ou JSON, se o tipo não for acessível), troca `Server` e grava de volta.

```csharp
[Fact]
public async Task Start_with_an_http_session_outside_loopback_is_InsecureSession_and_does_not_sync()
{
    var a = NewDevice();
    await SignUpAsync(a);
    await a.ReplaceAccountAsync();
    await RewriteSessionServerAsync(a, new Uri("http://192.0.2.10:8080/"));
    var clientsBefore = a.Net.ClientsCreated;

    await a.Account.StartAsync();
    a.Account.SyncNow();

    var status = await a.Account.GetStatusAsync();
    status.State.Should().Be(AccountState.InsecureSession);
    status.Server.Should().Be(new Uri("http://192.0.2.10:8080/"));
    a.Net.ClientsCreated.Should().Be(clientsBefore);
}

[Fact]
public async Task Sign_out_from_an_insecure_session_makes_no_request()
{
    // mesmo preparo do teste acima, com uma meta na fila
    await a.Account.SignOutAsync();
    (await a.Account.GetStatusAsync()).State.Should().Be(AccountState.SignedOut);
    a.Vault.Names.Should().BeEmpty();
    (await a.State.GetAsync()).Enabled.Should().BeFalse();
    a.Net.ClientsCreated.Should().Be(clientsBefore, "o token iria em texto puro");
}

[Fact]
public async Task Start_with_an_http_loopback_session_still_syncs()
// sessão gravada com http://127.0.0.1 (o servidor de teste): State == SignedIn e LastSyncAt preenchido.
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/SlimeGoals.Server.Tests -- --filter-method "*InsecureSession*" --filter-method "*insecure_session*" --filter-method "*loopback_session*"`
Expected: FAIL, porque `AccountState.InsecureSession` não existe (erro de compilação).

- [ ] **Step 3: Implementar**

Em `OpenAsync`, depois de ler uma `session` não nula: se `ServerUrlRules.Check(session.Server)` não for nulo, guardar e-mail e servidor, descartar a sessão (`Dispose`) e `SetState(AccountState.InsecureSession, email, server)`, sem `StartRun`. Em `SignOutAsync`, um `server` lido que não passe em `ServerUrlRules.Check` segue o caminho sem rede (apagar o segredo do cofre e `state.DisableAsync`), o mesmo do segredo ilegível.

- [ ] **Step 4: Rodar e ver passar**

Run: o comando do Step 2, e depois `dotnet test --project tests/SlimeGoals.Server.Tests`
Expected: PASS, e os outros testes do coordenador continuam passando.

- [ ] **Step 5: Commit**

`git commit -m "feat(sync): sessão gravada com http fora do loopback não sincroniza"`

---

### Task 2: Tela de conta pronta para o Release (T11, T12)

**Files:**
- Modify: `src/SlimeGoals.Core/Services/AppSettings.cs` (`GetSyncServerAsync`, remover `DefaultSyncServer`)
- Modify: `src/SlimeGoals.App/Account/AccountViewModel.cs`, `src/SlimeGoals.App/Account/AccountDefaults.cs`
- Modify: `src/SlimeGoals.App/Views/AccountSectionView.axaml`, `src/SlimeGoals.App/Views/AccountView.axaml`
- Test: `tests/SlimeGoals.App.Tests/Account/AccountViewModelTests.cs`, `tests/SlimeGoals.Core.Tests` (teste de `AppSettings` existente)

**Interfaces:**
- Consumes: `AccountState.InsecureSession` (Tarefa 1).
- Produces:
  - `Task<Uri?> AppSettings.GetSyncServerAsync(CancellationToken ct = default)`: `null` quando ausente ou inválido.
  - `sealed record AccountDefaults(string DeviceName, Uri? DefaultServer = null)`: o padrão do campo do servidor, que o head escolhe (Tarefa 4).
  - `AccountViewModel.IsInsecureSession`, `AccountViewModel.InsecureSessionText`, `AccountViewModel.ServerHint = "https://servidor.exemplo.ts.net"`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task Server_field_starts_empty_without_a_default_and_without_a_saved_server()
// AccountDefaults("pc") e nada gravado: depois de LoadCommand, ServerText == "".

[Fact] public async Task Server_field_starts_with_the_head_default()
// AccountDefaults("pc", new Uri("http://127.0.0.1:8080")): ServerText == "http://127.0.0.1:8080".

[Fact] public async Task Saved_server_wins_over_the_default()

[Fact] public async Task Insecure_session_offers_only_sign_out()
// FakeSyncAccount em InsecureSession: IsInsecureSession, !IsSessionExpired, SummaryText == "Sessão sem https",
// InsecureSessionText == "Esta sessão usa um endereço http://, que não é mais aceito. Saia da conta e entre de novo com https://. O que está na fila deste aparelho não será enviado."

[Fact] public void Confirm_text_does_not_mention_the_development_server()
// ConfirmText == "Enviamos um código para a@exemplo.com. Se não chegar em alguns minutos, confira o spam."
```

E no teste de `AppSettings`: ausente e inválido devolvem `null`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/SlimeGoals.App.Tests` e `dotnet test --project tests/SlimeGoals.Core.Tests`
Expected: FAIL (membros novos não existem; `ConfirmText` diferente).

- [ ] **Step 3: Implementar**

`AccountViewModel` usa `AccountDefaults.DefaultServer` quando `GetSyncServerAsync` devolve `null`, e `""` quando os dois são nulos. Bloco de `InsecureSession` em `AccountSectionView.axaml` igual ao de `SessionUnreadable` (texto + "Sair"). `Watermark="{x:Static acc:AccountViewModel.ServerHint}"` nos três `TextBox` do servidor em `AccountView.axaml`. O `ConfirmText` novo vale em todos os builds. **Chamada de quem escreveu o plano:** o servidor de desenvolvimento continua mostrando o código no log, o que `docs/sincronizacao.md` já diz.

- [ ] **Step 4: Rodar e ver passar; conferir na Render**

Run: os comandos do Step 2, e `dotnet run --project tools/SlimeGoals.Render -- /tmp/render all`
Expected: PASS; os PNGs da conta mostram a dica no campo vazio e o bloco de sessão sem https, alinhado como o de sessão ilegível.

- [ ] **Step 5: Commit**

`git commit -m "feat(app): conta com servidor vazio por padrão e sessão sem https só com Sair"`

---

### Task 3: Aviso de sincronização parada na tela Hoje (T15)

**Files:**
- Create: `src/SlimeGoals.App/Account/SyncStaleWarning.cs`
- Modify: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`, `src/SlimeGoals.App/Views/TodayView.axaml`
- Modify: `src/SlimeGoals.App/Hosting/PageNavigator.cs`, `src/SlimeGoals.App/ViewModels/SettingsPageViewModel.cs`
- Test: `tests/SlimeGoals.App.Tests/Account/SyncStaleWarningTests.cs`, `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**
- Produces:
  - `public static class SyncStaleWarning { public static readonly TimeSpan After = TimeSpan.FromDays(3); public const string Text = "Não sincroniza há 3 dias. Confira a conexão com o servidor."; public static bool IsStale(AccountStatus status, DateTimeOffset now); }`
  - `TodayPageViewModel`: novo parâmetro opcional `ISyncAccount? account = null` no fim do construtor; `string? SyncWarning`, `bool HasSyncWarning`, `IRelayCommand OpenSyncCommand`.
  - `PageNavigator.ShowSettings(SettingsSection section)`: dispara `event Action<SettingsSection>? SettingsRequested` e depois `Show<SettingsPageViewModel>()`. `SettingsPageViewModel` ganha `PageNavigator? navigator = null` no construtor e, ao receber o pedido, faz o mesmo que `OpenSection(section)`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
// SyncStaleWarningTests: now = 2026-11-20T12:00Z
[Fact] public void Stale_after_more_than_three_days_signed_in()            // LastSyncAt = now - 3d - 1s -> true
[Fact] public void Not_stale_at_exactly_three_days()                       // now - 3d -> false
[Fact] public void Not_stale_when_signed_out()                             // SignedOut, LastSyncAt antigo -> false
[Fact] public void Stale_when_the_session_expired_long_ago()               // SessionExpired, now - 4d -> true
[Fact] public void No_warning_before_the_first_successful_sync()           // SignedIn, LastSyncAt null -> false
[Fact] public void No_warning_when_the_last_sync_is_in_the_future()        // LastSyncAt = now + 1d -> false

// TodayPageViewModel (FakeTimeProvider, FakeSyncAccount da pasta Account dos testes)
[Fact] public async Task Today_shows_the_sync_warning_when_stale()        // SyncWarning == SyncStaleWarning.Text
[Fact] public async Task Today_hides_the_warning_after_a_successful_sync() // status novo + StatusChanged -> HasSyncWarning false
[Fact] public async Task Open_sync_shows_the_sync_settings()               // OpenSyncCommand -> navigator pediu SettingsSection.Sync e SettingsPageViewModel
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/SlimeGoals.App.Tests`
Expected: FAIL (tipos e membros novos não existem).

- [ ] **Step 3: Implementar**

`IsStale`: estado diferente de `SignedOut`, `LastSyncAt` não nulo e `now - LastSyncAt > After`. O Hoje recalcula o aviso em `LoadAsync` e no `StatusChanged` da conta, que chega de qualquer thread e volta pela mesma `OnUi` que a tela já usa. Na view, um `Border` `inline-panel` logo abaixo do aviso de permissões, com o texto e um botão `ghost` "Abrir", ligado a `OpenSyncCommand`. `LastSyncAt` já tem o sentido da T15: o `SyncEngine` só grava a hora num ciclo completo, mesmo sem nada para trocar (`RunExclusiveAsync`, depois do push).

- [ ] **Step 4: Rodar e ver passar; conferir na Render**

Run: `dotnet test --project tests/SlimeGoals.App.Tests`, depois a Render (`phone` e `desktop`) com um cenário em que o aviso aparece (acrescentar o cenário na ferramenta, se ela não tiver como montar a conta parada)
Expected: PASS; o aviso tem a mesma margem e altura de botão do aviso de permissões, sem quebrar em largura de celular.

- [ ] **Step 5: Commit**

`git commit -m "feat(app): aviso na tela Hoje quando não sincroniza há 3 dias"`

---

### Task 4: A conta no Release dos dois heads (T10, T11)

**Files:**
- Modify: `src/SlimeGoals.Desktop/SlimeGoals.Desktop.csproj`, `src/SlimeGoals.Desktop/CompositionRoot.cs`
- Modify: `src/SlimeGoals.Android/SlimeGoals.Android.csproj`, `src/SlimeGoals.Android/AndroidCompositionRoot.cs`, `src/SlimeGoals.Android/AndroidApplication.cs`, `src/SlimeGoals.Android/MainActivity.cs`
- Modify: `src/SlimeGoals.Android/Sync/SyncDevManifest.cs` (a `INTERNET` sai de dentro do `#if`), `AndroidDeviceName.cs`, `AndroidKeystoreVault.cs`, `AndroidSensitiveClipboard.cs` (saem do `#if SYNC_DEV`)
- Modify: `src/SlimeGoals.App/Hosting/AppServiceCollectionExtensions.cs` (`AddSlimeGoalsSync` recebe o padrão do servidor)
- Test: `tests/SlimeGoals.App.Tests/Hosting/AppServiceCollectionExtensionsTests.cs` (nenhum projeto de teste referencia os heads; eles são conferidos pelos builds do Step 4)

**Interfaces:**
- Consumes: `AccountDefaults(string DeviceName, Uri? DefaultServer)` (Tarefa 2); `TodayPageViewModel(..., ISyncAccount? account)` (Tarefa 3).
- Produces: `AddSlimeGoalsSync(this IServiceCollection, Func<IServiceProvider, ISecretVault> vault, string deviceName, Uri? defaultServer)`. Os dois heads chamam sempre; `PlatformCapabilities` sempre com `Sync = true`.

- [ ] **Step 1: Escrever os testes que falham**

Em `AppServiceCollectionExtensionsTests`:
- `Sync_passes_the_head_default_server_to_the_account_screen`: `AddSlimeGoalsSync(_ => new InMemorySecretVault(), "pc da Ana", new Uri("http://127.0.0.1:8080"))` resolve `AccountDefaults` com esse `DefaultServer`.
- `Sync_without_a_default_server_leaves_the_field_empty`: com `null`, `AccountDefaults.DefaultServer` é nulo.

O helper existente da linha 72 passa a mandar o terceiro argumento.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test --project tests/SlimeGoals.App.Tests`
Expected: FAIL (a sobrecarga com `defaultServer` não existe).

- [ ] **Step 3: Implementar**

- Desktop: `AddSync` e `Sync = true` sem `#if`. `SYNC_DEV` (Debug ou `-p:SyncDev=true`) passa a decidir só o padrão do servidor: `http://127.0.0.1:8080` no dev, `null` no Release. Comentário do `.csproj` atualizado.
- Android: `INTERNET`, `AddSync`, `Sync = true` e o `SyncNow()` do `OnResume` valem em todos os builds. Continuam só sob `SYNC_DEV`: o `network_security_config_dev` (o Release fica com `network_security_config`, sem texto puro), o `CryptoSelfCheckReceiver` e o padrão `http://127.0.0.1:8080`.
- Comentários e o doc de `AddSlimeGoalsSync` deixam de dizer "só os builds de desenvolvimento".

- [ ] **Step 4: Conferir os quatro builds**

Run:
- `dotnet build SlimeGoals.slnx` e `dotnet test --solution SlimeGoals.slnx`.
- `dotnet build SlimeGoals.Android.slnx -c Release -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk` e `aapt2 dump xmltree --file AndroidManifest.xml` no APK do Release: tem `android.permission.INTERNET`, aponta para `@xml/network_security_config` e não tem o `CryptoSelfCheckReceiver`.
- O mesmo com `-p:SyncDev=true`: aponta para `network_security_config_dev` e tem o receiver.
- `XDG_DATA_HOME=/tmp/slimegoals-dev/data XDG_CONFIG_HOME=/tmp/slimegoals-dev/config dotnet run --project src/SlimeGoals.Desktop -c Release`: Configurações mostra Sincronização, com o campo do servidor vazio e a dica.

Expected: tudo compila sem aviso, os testes passam e os manifestos batem com a lista acima.

- [ ] **Step 5: Commit**

`git commit -m "feat(desktop,android): conta e sincronização no build de Release"`

(Se o hook de escopos pedir um só: dois commits, `feat(desktop): …` e `feat(android): …`.)

---

### Task 5: Compose e Caddy na tailnet (T1, T2, T3)

**Files:**
- Create: `compose.tailnet.yaml`, `packaging/server/Caddyfile.tailnet`
- Modify: `.env.example`, `packaging/server/init-env.sh` (o `.env` gerado ganha `TAILNET_IP=` e `TAILNET_CERT_DIR=` comentados), `.github/workflows/ci.yml`
- Create: `packaging/server/check-tailnet-compose.sh` (a conferência que o CI roda)

**Interfaces:**
- Produces: variáveis `TAILNET_IP` (obrigatória: `${TAILNET_IP:?defina TAILNET_IP no .env}`) e `TAILNET_CERT_DIR` (padrão `/etc/slimegoals/tls`), montada no Caddy em `/etc/slimegoals/tls:ro`. Arquivos dentro dela: `tailnet.crt` e `tailnet.key` (as Tarefas 6 e 8 usam esses nomes).

- [ ] **Step 1: Escrever a conferência que falha**

`check-tailnet-compose.sh`: com `TAILNET_IP=100.64.0.10`, `SLIMEGOALS_DOMAIN=servidor.exemplo.ts.net` e SMTP fictício no ambiente, roda `docker compose -f compose.yaml -f compose.tailnet.yaml config --format json` e, com `jq`, sai com erro se:
- o Caddy publica algo diferente de exatamente uma porta, `100.64.0.10:443 -> 443`;
- algum serviço publica em `0.0.0.0`, ou a porta 80;
- o volume do certificado não é somente leitura, ou algum volume contém `tailscaled.sock`;
- o Caddyfile montado não é `Caddyfile.tailnet`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `bash packaging/server/check-tailnet-compose.sh`
Expected: FAIL (`compose.tailnet.yaml` não existe).

- [ ] **Step 3: Implementar**

`compose.tailnet.yaml` só com `services.caddy`: `ports: !override` e `volumes: !override` (o Caddyfile da tailnet, a pasta do certificado `:ro`, `caddy_data`, `caddy_config`). O `environment` do Caddy no `compose.yaml` traz `ACME_EMAIL`; o `Caddyfile.tailnet` não o usa. `Caddyfile.tailnet`: global `auto_https disable_redirects` e `skip_install_trust`; site `{$SLIMEGOALS_DOMAIN}` com `tls /etc/slimegoals/tls/tailnet.crt /etc/slimegoals/tls/tailnet.key`, `header Strict-Transport-Security "max-age=31536000"`, `header -Server` e `reverse_proxy server:8080`, sem diretiva `log`. `.env.example` ganha uma seção "Na tailnet" com `TAILNET_IP=100.64.0.10` e `TAILNET_CERT_DIR=/etc/slimegoals/tls` como exemplo, dizendo que `SLIMEGOALS_DOMAIN` é o nome `.ts.net` completo. No CI, um passo "Confere o Compose da tailnet" roda o script.

- [ ] **Step 4: Rodar e ver passar; subir localmente**

Run: `bash packaging/server/check-tailnet-compose.sh` → PASS. Depois, num teste local descartável: certificado autoassinado para `localhost` (`openssl req -x509 … -subj /CN=localhost -addext subjectAltName=DNS:localhost`) numa pasta de rascunho como `TAILNET_CERT_DIR`, com `TAILNET_IP=127.0.0.1` e `SLIMEGOALS_DOMAIN=localhost`. Subir com `-p slimegoals-tailnet-teste` (projeto separado, para não tocar no volume do servidor de dev), com o SMTP fictício do §5 da spec de endurecimento.
Expected: `curl -sk https://localhost/health` → 200; `curl -sI http://localhost/` não conecta; `docker inspect` do Caddy mostra o certificado montado `ro`. Derrubar com `docker compose -p slimegoals-tailnet-teste … down -v`: o `-v` aqui é só do projeto de teste, criado nesta etapa.

- [ ] **Step 5: Commit**

`git commit -m "build(server): Compose e Caddy só no IP da tailnet, com certificado em arquivo"`

---

### Task 6: Certificado, partida e firewall na VM (T4, T5, T6, T7, T7a)

**Files:**
- Create: `packaging/server/tailnet-cert.sh`, `packaging/server/tailnet-firewall.sh`, `packaging/server/install-systemd.sh`
- Create: `packaging/server/systemd/slimegoals-tls.service`, `slimegoals-tls.timer`, `slimegoals-firewall.service`, `packaging/server/systemd/60-slimegoals.conf` (sysctl)
- Create: `packaging/server/tests/tailnet-cert.test.sh`
- Modify: `.github/workflows/ci.yml` (roda o teste do script)

**Interfaces:**
- Consumes: `TAILNET_CERT_DIR`, `tailnet.crt`/`tailnet.key`, `compose.tailnet.yaml` (Tarefa 5).
- Produces: `tailnet-cert.sh` lê `SLIMEGOALS_DOMAIN` e `TAILNET_CERT_DIR` do `.env` da raiz do repositório (raiz descoberta pelo caminho do script, como no `init-env.sh`), procurando as linhas `^NOME=` sem dar `source` no arquivo. `TAILSCALE_BIN` e `DOCKER_BIN` (padrão `tailscale` e `docker`) existem para o teste trocar por falsos. Saída 0 sem mudança ou com renovação; diferente de zero quando o `tailscale cert` falha.

- [ ] **Step 1: Escrever o teste que falha**

`tailnet-cert.test.sh`, em bash puro com pastas temporárias, um `.env` falso e binários falsos que registram as chamadas num arquivo:
- `first_run_writes_files_600_and_reloads`: grava `tailnet.crt` e `tailnet.key` com permissão 600, pasta 700, e chama `caddy reload` uma vez.
- `same_cert_does_not_reload`: segunda execução com o mesmo conteúdo não chama o docker.
- `new_cert_replaces_and_reloads`: o falso devolve outro conteúdo, os arquivos mudam e o reload acontece.
- `cert_failure_keeps_files_and_does_not_reload`: o falso sai com 1, os arquivos anteriores ficam iguais (byte a byte), nada de reload e saída diferente de zero.
- `key_is_never_printed`: a saída do script (stdout e stderr) não contém o conteúdo da chave falsa.

- [ ] **Step 2: Rodar e ver falhar**

Run: `bash packaging/server/tests/tailnet-cert.test.sh`
Expected: FAIL (o script não existe).

- [ ] **Step 3: Implementar**

- `tailnet-cert.sh`: `umask 077`; `tailscale cert --cert-file "$tmp/tailnet.crt" --key-file "$tmp/tailnet.key" "$domain"` numa pasta temporária dentro de `TAILNET_CERT_DIR`. Se os dois arquivos forem iguais aos atuais (`cmp`), apaga o temporário e sai 0. Se não, faz `mv` atômico e, se o contêiner do Caddy estiver rodando, `docker compose -f compose.yaml -f compose.tailnet.yaml --project-directory "$root" exec -T caddy caddy reload --config /etc/caddy/Caddyfile --force` (sem `--force` o Caddy não recarrega quando o texto do Caddyfile não mudou). Antes da troca grava o marcador `$TAILNET_CERT_DIR/.recarregar` (0600), que só some depois de um reload bem-sucedido: toda execução, mesmo sem mudança, recarrega enquanto o marcador existir e o Caddy estiver rodando; falha do `docker compose ps` ou do reload sai diferente de zero e mantém o marcador. `--ctdir ORIGINAL` na regra do firewall impede que a resposta do Caddy (que sai por `br-*`) também caia no DROP. `TAILNET_CERT_DIR` precisa ser caminho absoluto de caracteres seguros, sem `..`; pasta existente deve ser do usuário e sem escrita de grupo ou outros. Em falha, apaga o temporário e não toca nos atuais.
- `slimegoals-tls.service`: `Type=oneshot`, `After=tailscaled.service network-online.target`, `Wants=tailscaled.service network-online.target`, `Before=docker.service`, `TimeoutStartSec=90s`, `[Install] WantedBy=docker.service` (para rodar na partida), `ExecStart=@ROOT@/packaging/server/tailnet-cert.sh`. Na partida, uma falha não impede o Docker de subir com o certificado que já existe. `slimegoals-tls.timer`: `OnCalendar=daily`, `RandomizedDelaySec=1h`, `Persistent=true`.
- `60-slimegoals.conf`: `net.ipv4.ip_nonlocal_bind = 1`, para o Docker conseguir publicar no IP da tailnet antes de o `tailscaled` recebê-lo (T6). **Chamada de quem escreveu o plano:** a VM é dedicada ao servidor, e esse ajuste global não afeta mais nada nela.
- `tailnet-firewall.sh`: com `TAILNET_IP` lido do `.env`, garante uma única regra em `DOCKER-USER`, `-p tcp -m conntrack --ctdir ORIGINAL --ctorigdst "$TAILNET_IP" --ctorigdstport 443 ! -i tailscale0 -j DROP`: confere com `-C` e insere com `-I` só se faltar. `slimegoals-firewall.service`: `Type=oneshot`, `RemainAfterExit=yes`, `After=docker.service`, `PartOf=docker.service`, `WantedBy=docker.service`, para voltar a cada reinício do Docker.
- `install-systemd.sh` (como root): copia as unidades trocando `@ROOT@` pela raiz do repositório, instala o sysctl e o aplica (`sysctl --system`), e roda `systemctl daemon-reload`, `enable --now slimegoals-tls.timer slimegoals-firewall.service`, `enable slimegoals-tls.service` e `start slimegoals-tls.service`. Se `TAILNET_IP` ou `SLIMEGOALS_DOMAIN` faltarem no `.env`, recusa com mensagem.
- O guia (Tarefa 8) manda desligar o `userland-proxy` só se a conferência 3 da spec mostrar `10.89.0.1` (T7).

- [ ] **Step 4: Rodar e ver passar**

Run: `bash packaging/server/tests/tailnet-cert.test.sh`; `bash -n` nos scripts; `shellcheck packaging/server/*.sh packaging/server/tests/*.sh`, se instalado; `systemd-analyze verify` nas unidades com `@ROOT@` trocado por uma pasta temporária.
Expected: PASS, sem aviso do shellcheck.

- [ ] **Step 5: Commit**

`git commit -m "build(server): certificado da tailnet por timer, partida e firewall na VM"`

---

### Task 7: Backup diário do banco (T8)

**Files:**
- Create: `packaging/server/backup.sh`, `packaging/server/systemd/slimegoals-backup.service`, `slimegoals-backup.timer`
- Modify: `packaging/server/install-systemd.sh` (instala e liga o timer do backup)
- Create: `packaging/server/tests/backup.test.sh`

**Interfaces:**
- Consumes: `install-systemd.sh` e o padrão `DOCKER_BIN` (Tarefa 6).
- Produces: `backup.sh [pasta]`, com padrão `/var/backups/slimegoals`; `COMPOSE_FILES` (padrão `compose.yaml compose.tailnet.yaml`) permite rodar contra o Compose de dev. Arquivos `slimegoals-AAAAMMDD-HHMMSS.dump`.

- [ ] **Step 1: Escrever o teste que falha**

`backup.test.sh`, com um `docker` falso que escreve bytes conhecidos no stdout para o `pg_dump`:
- `writes_dump_600_in_700_dir`
- `keeps_the_14_newest`: com 16 arquivos antigos de nomes ordenáveis, sobram os 14 mais novos, já contando o novo.
- `failed_dump_leaves_no_partial_file`: o falso sai com 1, nenhum `.dump` novo, e os antigos ficam como estavam.

- [ ] **Step 2: Rodar e ver falhar**

Run: `bash packaging/server/tests/backup.test.sh`
Expected: FAIL (o script não existe).

- [ ] **Step 3: Implementar**

`umask 077`; `docker compose … exec -T db pg_dump -U postgres -d slimegoals -Fc` para um temporário na mesma pasta e `mv` ao terminar com sucesso; depois apaga os mais antigos além de 14. O timer roda todo dia (`OnCalendar=daily`, `Persistent=true`). O CI roda o teste, junto com o da Tarefa 6.

- [ ] **Step 4: Rodar e ver passar; backup e restauração de verdade no dev**

Run: o teste; depois, com o servidor de dev no ar, `COMPOSE_FILES="compose.yaml compose.dev.yaml" bash packaging/server/backup.sh /tmp/slimegoals-backup-teste`. Restaurar o `.dump` num PostgreSQL descartável (`docker run --rm postgres:18-alpine …`, `pg_restore --no-owner`) e contar as linhas de `accounts` e `records`.
Expected: PASS; contagens iguais às do servidor de dev. Os passos exatos da restauração vão para o guia da Tarefa 8.

- [ ] **Step 5: Commit**

`git commit -m "build(server): backup diário do banco com 14 cópias"`

---

### Task 8: Documentação e versão 0.12.0 (spec §7, §9, T14)

**Files:**
- Modify: `docs/servidor.md` (seção nova "Na tailnet"), `docs/sincronizacao.md`, `docs/android.md`, `README.md`
- Modify: o texto da tela Sobre, se disser que os dados não saem do aparelho (procure em `src/SlimeGoals.App`)
- Modify: `specs/Sincronizacao_v1.md` (linha da 9.6 e decisões em aberto 1 e 2), `specs/Endurecimento_do_servidor_v1.md` (§8: obrigação 2 resolvida pela T12; a 1 vale só para quem expuser na internet)
- Modify: `Directory.Build.props` (`<Version>0.12.0</Version>`)

- [ ] **Step 1: Escrever o guia "Na tailnet"**

Os passos da spec §7, na ordem:
1. Painel do Tailscale (MagicDNS e HTTPS) e Tailscale e Docker na VM.
2. Clonar o repositório, `init-env.sh` e o `.env`: `SLIMEGOALS_DOMAIN`, `TAILNET_IP`, SMTP (Gmail com senha de app e Resend, com a ressalva do Resend sem domínio) e a senha em `secrets/Email__Smtp__Password`.
3. `sudo packaging/server/install-systemd.sh`, depois `docker compose -f compose.yaml -f compose.tailnet.yaml up -d --build --wait`.
4. Criar a conta com o convite.
5. Atualizar com `git pull` + o mesmo `up --build`.
6. Restaurar o backup (Tarefa 7).
7. Desligar o repasse da porta 40000 do Umbrel; opcionalmente, desligar o vencimento da chave da VM.
8. Conferir que a VM não está compartilhada.
9. Desligar o `userland-proxy` se os IPs não aparecerem.
10. Corrigir o MagicDNS com systemd-resolved e NetworkManager (link da spec §7).
11. Manter o Tailscale ligado no celular.

- [ ] **Step 2: Atualizar os outros documentos**

- `docs/sincronizacao.md`: o Release tem a conta, e o `--dev` só muda o `http` local e o receiver.
- `docs/android.md`: "as metas ficam só neste celular" passa a valer só sem conta, e o APK de distribuição deixa de ser "sem acesso à rede".
- `README.md`: status e roteiro com a 9.6 ✅.
- `Sincronizacao_v1.md` §7 e a tela Sobre: nada promete que os dados não saem do aparelho.
- As duas specs, como listado em Files.

- [ ] **Step 3: Versão**

`<Version>0.12.0</Version>` em `Directory.Build.props`. O versionCode do Android sai 1200 pela fórmula do `.csproj`.

- [ ] **Step 4: Conferir**

Run: `dotnet build SlimeGoals.slnx` e `dotnet test --solution SlimeGoals.slnx`; `grep -rnE "100\.[0-9]+\.[0-9]+\.[0-9]+|tail[0-9a-f]{6}\.ts\.net" docs specs README.md .env.example packaging compose*.yaml`
Expected: tudo passa; o grep só acha os exemplos `100.64.0.10`, `100.x` e `servidor.exemplo.ts.net`.

- [ ] **Step 5: Commit**

Dois commits: `docs: guia do servidor na tailnet e conta no Release` e `build: versão 0.12.0`.

---

### Task 9: Revisão de segurança, implantação na VM e conferências (spec §8, §10)

Feita com o autor: os passos na VM e nos aparelhos são dele ou pedem a aprovação dele.

- [ ] **Step 1: Revisão de segurança do branch inteiro**

Uma revisão nova, de quem não escreveu o código, com o foco da spec §10 e da Review Focus deste plano: nada sensível em respostas, logs, telas ou no repositório; a chave do certificado legível só pelo root no host e pelo Caddy; nada publicado fora do IP da tailnet; o Release Android sem texto puro e sem o receiver de desenvolvimento; sair de sessão insegura sem rede. Corrigir o que vier, cada correção com teste quando for código.

- [ ] **Step 2: Implantar na VM (o autor roda ou aprova cada comando)**

O guia da Tarefa 8, do passo 2 ao 7, na VM `slimegoals`.

- [ ] **Step 3: Conferências da spec §8, de 1 a 12**

Registrar o resultado de cada uma, mais a da spec §6: os dois apps conectam mesmo com o MagicDNS respondendo o IPv6 da VM (o Caddy só escuta no IPv4). A 3 liga o log de acesso do Caddy só durante o teste. A 6 reinicia a VM. A 7 restaura o backup num volume novo. A 11 para a pilha e confere o aviso (ou usa a Render com o cenário da Tarefa 3, se o autor não quiser esperar 3 dias).

- [ ] **Step 4: Pacotes e aparelhos**

`bash packaging/linux/build.sh` e instalação (app fechado pelo Sair); `packaging/android/build.sh`, rodado pelo autor por causa da senha; `adb -s <serial do Galaxy> install -r artifacts/slimegoals-0.12.0-android.apk`. Conferir versionCode 1200, `firstInstallTime` inalterado e certificado `e88d…`.

- [ ] **Step 5: PR**

Um PR para a `main`, com o resumo, as conferências e os minors adiados. Antes do push, conferir que o PR anterior não foi mesclado com commits pendentes.
