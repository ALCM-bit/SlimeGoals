# Fase 9.5b — Conta e sincronização no Android — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No APK de desenvolvimento do Android, Configurações ganha a mesma seção "Conta e sincronização" do Linux, com o segredo da sessão guardado sobre o Android Keystore. O celular sincroniza com o servidor local pelo cabo (`adb reverse`), e o APK normal continua sem rede.

**Architecture:**
- **`src/SlimeGoals.Sync`:** `SealedSecretFormat` (formato do arquivo cifrado do cofre, testável fora do Android) e `CryptoSelfCheck` (os 5 vetores fixos da spec, para conferir no aparelho).
- **Core:** a documentação de `ISecretVault` passa a dizer que `GetAsync` pode lançar `InvalidDataException` quando o segredo existe mas não abre. O coordenador já trata essa exceção como `SessionUnreadable`; um teste fixa isso para o caso em que é o cofre que lança.
- **App:** `AccountViewModel` aceita uma área de transferência da plataforma (`ITextClipboard`), usada pelo "Copiar" do código no lugar da do Avalonia.
- **Android, só com `SYNC_DEV`** (Debug ou `-p:SyncDev=true`): `AndroidKeystoreVault`, `AndroidSensitiveClipboard`, permissão `INTERNET`, `network_security_config` que libera texto puro só para `127.0.0.1` e `localhost`, registro do coordenador e do `SyncHost`, `SyncNow` no `OnResume`, o nome do aparelho e um receptor que roda o `CryptoSelfCheck` quando o `adb` pede.
- **Empacotamento:** `packaging/android/build.sh --dev` gera `artifacts/slimegoals-<versão>-android-dev.apk`, assinado com a mesma chave.

**Tech Stack:** as versões atuais, sem pacote novo. .NET 10 para Android (API 26 a 36), Avalonia 12, Konscious (Argon2id), `Javax.Crypto` e `Java.Security` do binding do Android, xUnit v3, AwesomeAssertions.

**Spec:** `specs/Sincronizacao_v1.md`: §6 (cofre "Android Keystore"), "Obrigações para a 9.5" de "Decisões da fase 9.4" (cofre do Android e vetores), "Decisões da fase 9.5a" (build de desenvolvimento, endereço do servidor, cliente HTTP), §7 (Android) e §4 ("ao voltar ao primeiro plano"). Spike: `specs/spikes/sincronizacao-cripto.md`. Plano anterior: `2026-11-07-fase-9-5a-conta-linux.md`. Desenho aprovado pelo autor na conversa de 15/11/2026.

## Global Constraints

- O APK de distribuição (`packaging/android/build.sh` sem `--dev`) não declara `INTERNET`, não registra cofre, coordenador nem a seção, e não tem o receptor dos vetores.
- `SYNC_DEV` no Android: definido em Debug ou com `-p:SyncDev=true`, como no Linux.
- O cofre nunca grava o segredo em claro: sem Keystore, `SecretVaultUnavailableException` e a sincronização não liga (§6).
- O nome do segredo continua `sync-session`, com o mesmo JSON do Linux.
- Rede: tráfego em texto puro só para `127.0.0.1` e `localhost`, e só no build de desenvolvimento. O servidor do Compose continua preso a `127.0.0.1:8080`, e o celular chega a ele por `adb reverse tcp:8080 tcp:8080`.
- Cliente HTTP: um por servidor, 30 s de prazo, sem seguir redirecionamento (igual ao Linux).
- `Trace` e `Log` nunca recebem segredo, token, código de recuperação nem mensagem de exceção; use `TraceText.Of(e)` ou só o tipo.
- Sem sincronização em segundo plano (§1, fora do escopo).
- Conventional Commits em português, com escopo por camada; TDD onde o código roda no xUnit.

## Review Focus

1. **O Keystore perdeu a chave, mas o arquivo continua** (backup restaurado em outro aparelho, dados do Keystore apagados): espera-se `SessionUnreadable`, com "Sair" funcionando, e nunca um travamento ou um laço de "Tentar de novo". Coberto pelo teste do coordenador na Tarefa 1 e pelo passo manual 6 da Tarefa 6.
2. **Gravação interrompida** (app morto no meio do `SetAsync`): o arquivo antigo ou o novo, inteiro, nunca pela metade. Coberto pela gravação em arquivo temporário seguida de `File.Move` com sobrescrita (Tarefa 4) e pelo `TryUnpack`, que recusa arquivo curto (Tarefa 1).
3. **O APK de distribuição ganhar rede sem querer:** a Tarefa 4 confere com `aapt2` que o Release sem `SyncDev` não tem `INTERNET` nem o receptor, e que o de desenvolvimento tem os dois.
4. **Release com trimming quebrar Konscious ou JSON no aparelho:** o APK de desenvolvimento é Release. O `CryptoSelfCheck` roda nele, no emulador e no celular (Tarefa 6), e o teste de ponta a ponta usa o mesmo APK.
5. **Servidor apontado para um host remoto em `http`:** deve falhar com o texto de erro de conexão, sem sair nada em claro. Coberto pelo passo manual 7 da Tarefa 6.

---

### Tarefa 1: Formato do arquivo do cofre e contrato do segredo ilegível

**Files:**
- Create: `src/SlimeGoals.Sync/Vault/SealedSecretFormat.cs`
- Create: `tests/SlimeGoals.Sync.Tests/Vault/SealedSecretFormatTests.cs`
- Modify: `src/SlimeGoals.Core/Abstractions/ISecretVault.cs` (só a documentação de `GetAsync`)
- Modify: `tests/SlimeGoals.Testing/InMemorySecretVault.cs` (propriedade `FailReadsWith`)
- Modify: `tests/SlimeGoals.Server.Tests/Account/SyncCoordinatorTests.cs`

**Interfaces:**
- Produces:
  - `public static class SealedSecretFormat`
  - `public const byte Version = 1;` `public const int NonceSize = 12;` `public const int TagSize = 16;`
  - `public static bool IsValidName(string name)`: verdadeiro só para `^[a-z0-9-]{1,64}$`.
  - `public static byte[] AssociatedData(string name)`: UTF-8 de `"slimegoals/v1/vault/" + name`; `ArgumentException` para nome inválido.
  - `public static byte[] Pack(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipherAndTag)`: `Version ‖ nonce ‖ cipherAndTag`; `ArgumentException` se `nonce` não tiver 12 bytes ou `cipherAndTag` tiver menos de 16.
  - `public static bool TryUnpack(ReadOnlySpan<byte> blob, out byte[] nonce, out byte[] cipherAndTag)`: falso para versão desconhecida ou tamanho menor que `1 + 12 + 16`.
  - `InMemorySecretVault.FailReadsWith` (`Exception?`): quando definido, `GetAsync` lança essa exceção.

O GCM do Java devolve cifra e tag juntas. Por isso o formato guarda `cipherAndTag` como um bloco só.

- [ ] **Passo 1: Escrever os testes que falham** em `SealedSecretFormatTests`:
  - `Pack_then_unpack_returns_the_same_parts`: nonce `0..11`, cifra+tag de 20 bytes; `TryUnpack` devolve verdadeiro e os mesmos bytes; `blob[0] == 1`.
  - `Unpack_refuses_an_unknown_version`: o mesmo blob com `blob[0] = 2` devolve falso.
  - `Unpack_refuses_a_truncated_blob`: blobs de 0, 1 e 28 bytes devolvem falso; 29 bytes com versão 1 devolve verdadeiro.
  - `Pack_refuses_a_nonce_of_the_wrong_size`: nonces de 11 e 13 bytes lançam `ArgumentException`.
  - `Associated_data_depends_on_the_name`: `AssociatedData("sync-session")` é igual ao UTF-8 de `"slimegoals/v1/vault/sync-session"` e diferente de `AssociatedData("outro")`.
  - `Only_simple_names_are_valid` (Theory): `"sync-session"` e `"a1"` são válidos; `""`, `"../x"`, `"Sync"`, `"a b"`, `"a/b"` e 65 vezes `"a"` não são.
- [ ] **Passo 2: Rodar e ver falhar.** `dotnet test --project tests/SlimeGoals.Sync.Tests -- --filter-class "*SealedSecretFormatTests"`. Esperado: erro de compilação, porque `SealedSecretFormat` não existe.
- [ ] **Passo 3: Implementar `SealedSecretFormat`** com as assinaturas acima.
- [ ] **Passo 4: Rodar.** Mesmo comando. Esperado: todos passam.
- [ ] **Passo 5: Teste do contrato no coordenador.** Em `SyncCoordinatorTests`, escrever `Start_with_a_vault_that_cannot_open_the_secret_is_SessionUnreadable_and_sign_out_clears`, espelhando `Start_with_unreadable_secret_is_SessionUnreadable_and_sign_out_clears`, mas com o segredo válido gravado e `a.Vault.FailReadsWith = new InvalidDataException()`:
  - depois de `StartAsync`, o estado é `SessionUnreadable`;
  - depois de `SignOutAsync`, o estado é `SignedOut` e a sincronização fica desligada.

  Adicionar `FailReadsWith` ao `InMemorySecretVault`. O `DeleteAsync` dele não é afetado, porque é ele que limpa. Se o teste passar logo de primeira, isso só confirma o contrato que já existe; registre no ledger que ele não teve RED. Atualizar a doc de `ISecretVault.GetAsync`: `<exception cref="InvalidDataException">O segredo existe mas não pode ser aberto (chave do cofre perdida ou arquivo corrompido).</exception>`.
- [ ] **Passo 6: Rodar** `dotnet test --project tests/SlimeGoals.Server.Tests -- --filter-class "*SyncCoordinatorTests"` (precisa do Docker). Esperado: todos passam.
- [ ] **Passo 7: Commit.** `feat(core): formato do arquivo do cofre e segredo ilegível vindo do cofre`. O escopo `core` cobre a mudança de contrato; o formato fica em `Sync`.

### Tarefa 2: Conferência dos vetores fixos

**Files:**
- Create: `src/SlimeGoals.Sync/Crypto/CryptoSelfCheck.cs`
- Create: `tests/SlimeGoals.Sync.Tests/Crypto/CryptoSelfCheckTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record CryptoCheckResult(string Name, bool Passed);`
  - `public static class CryptoSelfCheck { public static IReadOnlyList<CryptoCheckResult> Run(); }`
  - Nomes, nesta ordem: `"auth_key"`, `"wrap_key"`, `"recovery_key"`, `"recovery_wrap"`, `"record_key"`.

Os valores esperados são os da tabela "Vetores fixos" da spec. `Run` usa as classes de produção: `PasswordKeys.Derive` com `KdfParams.Default`, `RecoveryCode.Parse("000G-40R4-0M30-E209-185G-R38E-1W")` com as derivações de recuperação, e `AesGcmSyncCrypto` com a chave `000102…1f` para o `record_key` do id `00112233-4455-6677-8899-aabbccddeeff`. Cada item é calculado num `try` próprio: uma exceção vira `Passed = false` e não interrompe os outros.

- [ ] **Passo 1: Teste que falha.** `All_fixed_vectors_pass`: `Run()` devolve os 5 nomes, na ordem, todos com `Passed` verdadeiro.
- [ ] **Passo 2: Rodar e ver falhar** (`--filter-class "*CryptoSelfCheckTests"`). Esperado: erro de compilação.
- [ ] **Passo 3: Implementar.** Comparar com `CryptographicOperations.FixedTimeEquals` não é necessário, porque os vetores são públicos; use `SequenceEqual`.
- [ ] **Passo 4: Mostrar que o teste pega erro.** Troque temporariamente um caractere do `auth_key` esperado e veja o teste falhar. Depois desfaça.
- [ ] **Passo 5: Rodar.** Esperado: passa.
- [ ] **Passo 6: Commit.** `feat(core): conferência dos vetores fixos da criptografia`, com os arquivos de `Sync`.

### Tarefa 3: Área de transferência da plataforma no "Copiar"

**Files:**
- Modify: `src/SlimeGoals.App/Account/AccountViewModel.cs`
- Modify: `src/SlimeGoals.App/Views/AccountView.axaml.cs`
- Test: `tests/SlimeGoals.App.Tests/Account/AccountViewModelTests.cs`

**Interfaces:**
- Produces: o construtor de `AccountViewModel` ganha um último parâmetro opcional `ITextClipboard? platformClipboard = null`, exposto em `public ITextClipboard? PlatformClipboard { get; }`. A `AccountView` usa `_vm.PlatformClipboard ?? new AvaloniaTextClipboard(clipboard)`. Com a da plataforma, a View não precisa do `TopLevel.Clipboard`.

- [ ] **Passo 1: Teste que falha.** `The_platform_clipboard_is_kept_for_the_view`: com um `ITextClipboard` falso no construtor, `PlatformClipboard` é a mesma instância; sem ele, `null`.
- [ ] **Passo 2: Rodar e ver falhar** (`--filter-class "*AccountViewModelTests"`). Esperado: erro de compilação.
- [ ] **Passo 3: Implementar** o parâmetro, a propriedade e a escolha na View. A Render e o Desktop continuam sem passar nada.
- [ ] **Passo 4: Rodar.** Esperado: passa. Rodar também `dotnet build SlimeGoals.slnx`, que precisa terminar sem avisos.
- [ ] **Passo 5: Commit.** `feat(app): área de transferência da plataforma no código de recuperação`.

### Tarefa 4: Head Android com a conta (só `SYNC_DEV`)

Nada aqui roda no xUnit. A prova é o build, a inspeção do APK com `aapt2` e a Tarefa 6.

**Files:**
- Modify: `src/SlimeGoals.Android/SlimeGoals.Android.csproj` (`SYNC_DEV` em Debug ou com `SyncDev=true`)
- Create: `src/SlimeGoals.Android/Sync/AndroidKeystoreVault.cs`
- Create: `src/SlimeGoals.Android/Sync/AndroidSensitiveClipboard.cs`
- Create: `src/SlimeGoals.Android/Sync/AndroidDeviceName.cs`
- Create: `src/SlimeGoals.Android/Sync/CryptoSelfCheckReceiver.cs`
- Create: `src/SlimeGoals.Android/Sync/SyncDevManifest.cs` (`[assembly: UsesPermission(Manifest.Permission.Internet)]` dentro de `#if SYNC_DEV`)
- Create: `src/SlimeGoals.Android/Resources/xml/network_security_config.xml` e `network_security_config_dev.xml`
- Modify: `src/SlimeGoals.Android/AndroidApplication.cs` (`[Application(NetworkSecurityConfig = …)]`, escolhido por `#if SYNC_DEV`)
- Modify: `src/SlimeGoals.Android/AndroidCompositionRoot.cs` (`AddSync` dentro de `#if SYNC_DEV`)
- Modify: `src/SlimeGoals.Android/MainActivity.cs` (`OnResume`)

**Interfaces:**
- Consumes: `SealedSecretFormat` (Tarefa 1), `CryptoSelfCheck` (Tarefa 2), o parâmetro `platformClipboard` (Tarefa 3), `SyncCoordinator`, `ISyncAccount.SyncNow()`, `SyncHost`, `AccountDefaults`, `AccountViewModel`.
- Produces: `internal sealed class AndroidKeystoreVault(Context context) : ISecretVault`.

**Decisões fixas:**
- **`AndroidKeystoreVault`:**
  - **Chave:** alias `slimegoals-cofre` no provedor `AndroidKeyStore`. AES-256, GCM, `NoPadding`, para cifrar e decifrar, sem exigir autenticação da pessoa. A chave é criada no primeiro `SetAsync`, não no `GetAsync`.
  - **Arquivo:** `Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "cofre", name + ".bin")`. O nome passa por `SealedSecretFormat.IsValidName` (`ArgumentException` se não passar). A gravação vai para `.bin.tmp` e depois `File.Move(tmp, final, overwrite: true)`.
  - **Cifra:** `Cipher.GetInstance("AES/GCM/NoPadding")`. Ao cifrar, o próprio Keystore gera o nonce (`cipher.GetIV()`), e o dado autenticado vem de `SealedSecretFormat.AssociatedData(name)`. Ao decifrar, `GCMParameterSpec(128, nonce)`.
  - **`GetAsync`:**
    - sem arquivo: `null`;
    - `TryUnpack` falso, chave ausente ou qualquer exceção de cripto do Java (`AEADBadTagException`, `KeyPermanentlyInvalidatedException`, `GeneralSecurityException`): `InvalidDataException`, sem a mensagem original.
  - **Falha ao carregar o `KeyStore`** (`KeyStoreException`, `IOException`): `SecretVaultUnavailableException`. `IsAvailableAsync` devolve se o `KeyStore` carrega.
  - **`DeleteAsync`:** apaga o arquivo e mantém a chave. Não falha se o arquivo não existir.
  - **Concorrência e thread:** tudo sob um `Lock` e dentro de `Task.Run`, fora da thread da interface.
- **`AndroidSensitiveClipboard : ITextClipboard`** usa o `ClipboardManager`:
  - **`SetTextAsync`:** `ClipData.NewPlainText("SlimeGoals", text)`, com `ClipDescription.Extras` contendo `android.content.extra.IS_SENSITIVE = true` (constante `ClipDescription.ExtraIsSensitive` no API 33+, ou a string literal).
  - **`TryGetTextAsync`:** o texto do clipe primário, ou `null`. No Android 10+, o sistema só deixa ler com o app em foco.
  - **`ClearAsync`:** `ClearPrimaryClip()` no API 28+; abaixo disso, grava um clipe vazio.
  - Tudo na thread principal, que é a da interface do Avalonia.
- **`AndroidDeviceName.Get(Context)`:** `Settings.Global.GetString(resolver, Settings.Global.DeviceName)`. Se vier vazio, usa `Build.Model`; se ainda vazio, `"Android"`. Corta em `AccountRules` (100 caracteres).
- **`network_security_config.xml`:** `<base-config cleartextTrafficPermitted="false"/>`. **`_dev`:** a mesma base, mais `<domain-config cleartextTrafficPermitted="true">` com `<domain includeSubdomains="false">127.0.0.1</domain>` e `localhost`.
- **`CryptoSelfCheckReceiver`:**
  - Declarado com `[BroadcastReceiver(Exported = true, Permission = "android.permission.DUMP")]` e `[IntentFilter(new[] { "dev.alcm.slimegoals.VERIFICAR_CRIPTO" })]`, inteiro dentro de `#if SYNC_DEV`. Só o shell do `adb` (e o sistema) tem `DUMP`.
  - Usa `GoAsync()` e roda `CryptoSelfCheck.Run()` em `Task.Run`.
  - Registra em `Android.Util.Log` com a tag `SlimeGoals`: `cripto: N/5 vetores conferem` e, para cada falha, `cripto: falhou <nome>`. Nunca registra valores.
- **Composição (`AddSync`):** espelha a do Desktop, com estas diferenças:
  - o cofre é o `AndroidKeystoreVault(context)`;
  - `AccountDefaults(AndroidDeviceName.Get(context))`;
  - o `AccountViewModel` recebe `new AndroidSensitiveClipboard(context)`;
  - `PlatformCapabilities` passa a `new(true, false, false, false, Sync: true)`;
  - o `CreateSyncHttp` é igual ao do Desktop.
- **`OnResume`:** `AndroidApplication.Services?.GetService<ISyncAccount>()?.SyncNow()`. Sem `SYNC_DEV`, o serviço não existe e nada acontece.

- [ ] **Passo 1: Implementar** os arquivos acima.
- [ ] **Passo 2: Build de desenvolvimento.** `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`. Esperado: 0 erros e nenhum aviso além do XA0033.
- [ ] **Passo 3: Inspecionar os dois APKs Release.**
  - Gerar com e sem `-p:SyncDev=true`: `dotnet publish src/SlimeGoals.Android -c Release -f net10.0-android …`, sem assinatura de distribuição; basta a de debug do SDK.
  - Rodar `~/Android/Sdk/build-tools/<versão>/aapt2 dump xmltree <apk> --file AndroidManifest.xml`.
  - Esperado: sem `SyncDev`, nenhuma linha com `android.permission.INTERNET` nem com `CryptoSelfCheckReceiver`, e `networkSecurityConfig` apontando para o arquivo sem exceções. Com `SyncDev`, as duas linhas aparecem e a config é a `_dev`.
- [ ] **Passo 4: Suite e builds do Linux.** `dotnet test --solution SlimeGoals.slnx` e `dotnet build SlimeGoals.slnx -c Release`. Esperado: tudo verde, 0 avisos.
- [ ] **Passo 5: Commit.** `feat(android): conta e sincronização no build de desenvolvimento`.

### Tarefa 5: APK de desenvolvimento no `build.sh` e README

**Files:**
- Modify: `packaging/android/build.sh`
- Modify: `README.md` (seções "Rodar no Android (desenvolvimento)" e "Sincronização (em desenvolvimento)")

**Decisões fixas:**
- `build.sh --dev` acrescenta `-p:SyncDev=true` e grava `artifacts/slimegoals-<versão>-android-dev.apk`. Sem a opção, o script não muda. Qualquer outro argumento faz o script sair com `Uso: packaging/android/build.sh [--dev]` e código 2.
- O README explica:
  - instalar o APK de desenvolvimento por cima (mesma chave, os dados ficam);
  - voltar ao normal instalando o APK sem `-dev`;
  - `adb reverse tcp:8080 tcp:8080` antes de entrar na conta, e de novo cada vez que o cabo for reconectado;
  - o endereço `http://127.0.0.1:8080`;
  - a verificação dos vetores: `adb shell am broadcast -a dev.alcm.slimegoals.VERIFICAR_CRIPTO -n dev.alcm.slimegoals/<nome do receptor>` e `adb logcat -s SlimeGoals`.

- [ ] **Passo 1: Implementar.** Para conferir o argumento sem a senha: `packaging/android/build.sh --xyz` precisa sair com o texto de uso e código 2 **antes** de pedir a senha.
- [ ] **Passo 2: Commit.** `build(android): APK de desenvolvimento com a sincronização`.

### Tarefa 6: Verificação no emulador e no celular, e decisões na spec

**Files:**
- Modify: `specs/Sincronizacao_v1.md` (nova seção "Decisões da fase 9.5b", depois de "Decisões depois do teste manual da 9.5a")

Ambiente: servidor com `packaging/server/init-env.sh` e `docker compose up -d --build --wait`; app Linux de desenvolvimento com `XDG_DATA_HOME=/tmp/slimegoals-dev/data XDG_CONFIG_HOME=/tmp/slimegoals-dev/config`. Se o servidor de desenvolvimento já tiver contas de testes antigos, use outro e-mail. **Nunca `docker compose down -v`.**

- [ ] **Passo 1: Emulador.** Instalar o Debug com `-t:Install` e `-p:AdbTarget=-s emulator-5554`, rodar `adb -s emulator-5554 reverse tcp:8080 tcp:8080` e disparar o receptor. Esperado no logcat: `cripto: 5/5 vetores conferem`.
- [ ] **Passo 2: Ponta a ponta no emulador.**
  1. Criar a conta no Linux, com o código vindo de `docker compose logs server`.
  2. Entrar no emulador.
  3. Uma meta criada no Linux aparece no emulador e vice-versa.
  4. Um registro feito no emulador com ele fora do app aparece no Linux depois de voltar ao app (`SyncNow` do `OnResume`).
- [ ] **Passo 3: Reinício.** Esperado: depois de `adb -s emulator-5554 reboot`, o app abre já na conta (Keystore persistiu) e sincroniza.
- [ ] **Passo 4: Celular, só com o autor presente e de acordo.**
  1. O autor gera `build.sh --dev`; a senha é dele.
  2. Instalar com `adb -s <serial> install -r`.
  3. Rodar `adb reverse`.
  4. Disparar o receptor. Esperado: `5/5`, que é a prova do Konscious no arm64 em Release.
  5. O autor entra na conta de teste e confere uma meta indo e voltando.
  6. No fim, ele decide se volta ao APK normal.
- [ ] **Passo 5: Copiar o código de recuperação** no Android 13+ (emulador API 36). Esperado: a prévia da área de transferência mostra o conteúdo oculto.
- [ ] **Passo 6: Segredo que não abre.**
  1. No emulador, com o Debug (que aceita `run-as`) e o app parado, trocar o conteúdo de `no_backup/cofre/sync-session.bin` pelo byte `01` seguido de 63 bytes aleatórios. Como o arquivo continua com a versão 1, ele passa pelo formato e falha na tag do GCM, o mesmo caminho de uma chave perdida.
  2. Abrir o app.
  3. Esperado: estado "sessão ilegível", "Sair" funciona, e depois dá para entrar de novo.
- [ ] **Passo 7: Host remoto em `http`.** Com a conta desligada, trocar o servidor para `http://example.com:8080`. Esperado: "Entrar" falha com o texto de erro de conexão, e no logcat aparece a recusa de texto puro do Android, só com o tipo da exceção no Trace.
- [ ] **Passo 8: Spec.** Escrever "Decisões da fase 9.5b" com:
  - o formato do arquivo do cofre e onde ele fica;
  - o alias da chave;
  - `InvalidDataException` no `GetAsync`;
  - `adb reverse` e a config de rede;
  - o receptor dos vetores e quem pode dispará-lo;
  - a área de transferência sensível e o limite dela (no Android 10+, a limpeza depois de 60 s só acontece com o app em foco);
  - o nome do aparelho;
  - os resultados dos passos acima, com data.

  Atualizar também a linha do Android em "Obrigações para a 9.5", marcando o que foi cumprido.
- [ ] **Passo 9: Commit.** `docs(specs): decisões da fase 9.5b`.

## Decisões deste plano (o autor confirma na revisão)

1. **O formato do arquivo do cofre fica em `SlimeGoals.Sync/Vault`,** e não no head Android, para ser testado no xUnit. O código que fala com o Keystore fica no head, o mínimo possível.
2. **O cofre do Android lança `InvalidDataException` quando o segredo não abre,** em vez de `SecretVaultUnavailableException`. Assim a pessoa cai em "sessão ilegível", com "Sair", e não num "Tentar de novo" que nunca funcionaria.
3. **Os textos de "chaveiro" ficam como estão.** "Chaveiro do sistema" serve também para o Android. O estado travado lá só acontece se o Keystore não carregar, o que é raro.
4. **Ao sair do app, o motor continua enquanto o processo viver.** O Android corta a rede em segundo plano por conta própria, e a spec deixa o segundo plano de fora. Não há pausa explícita.
5. **A verificação dos vetores é um receptor protegido pela permissão `DUMP`,** só no APK de desenvolvimento. Nenhum outro app consegue dispará-la, e ela só registra "passou" ou "falhou".
