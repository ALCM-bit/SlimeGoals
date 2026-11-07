# Fase 9.4 — Criptografia e conta — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O aparelho cria conta, confirma o e-mail, entra, troca a senha, recupera, sai e apaga a conta, com chaves derivadas da senha e guardadas no cofre do sistema. Os registros vão ao servidor cifrados com AES-256-GCM, e o banco do servidor não contém título, unidade nem nota legíveis.

**Architecture:**
- **`src/SlimeGoals.Sync/Crypto`:** Argon2id (Konscious), HKDF, embrulho da chave de dados e `AesGcmSyncCrypto`, que implementa o `ISyncCrypto` da 9.2.
- **`src/SlimeGoals.Sync/Account`:** `HttpAccountApi`, o cliente das rotas `/auth`, `/devices` e `/account`, e `AccountService`, que orquestra cripto, API, cofre e `ISyncStateStore`.
- **`ISecretVault`** em `SlimeGoals.Core/Abstractions`. Implementação real só no Linux: `SecretServiceVault`, em `SlimeGoals.Platform.Linux`, pelo `Tmds.DBus.Protocol`. Nos testes, `InMemorySecretVault`, em `SlimeGoals.Testing`.
- **Testes de conta e de ponta a ponta** em `tests/SlimeGoals.Server.Tests`, contra o servidor real em contêiner.

**Tech Stack:** .NET 10, `System.Security.Cryptography` (`AesGcm`, `HKDF`, `HMACSHA256`), Konscious.Security.Cryptography.Argon2 1.3.1, Tmds.DBus.Protocol 0.95.2.

**Spec:** `specs/Sincronizacao_v1.md`: §2 (RS01, RS04, RS06, RS07), §5 "Decisões da fase 9.3", §6, §7 (só o que não é tela), §9 (Criptografia e Privacidade) e §10 (linha 9.4). Spike: `specs/spikes/sincronizacao-cripto.md`.

**Fora deste plano (9.5):**
- todas as telas;
- ligar o motor e o `AccountService` ao app, e a permissão `INTERNET`;
- endereço do servidor configurável e a regra do TLS (RS10);
- enviar a fila e avisar antes de sair, e o `DisableAsync` cancelando o ciclo em andamento;
- o cofre do Android (Keystore);
- vetores de cripto rodando no Android.

O app continua sem rede: nenhum projeto do app referencia `SlimeGoals.Sync`.

## Decisões deste plano (o autor confirma na revisão)

1. **Fronteira com a 9.5 (decidida pelo autor):** a 9.4 entrega a cripto, o cofre do Linux e o serviço de conta, sem telas.
2. **Cadastro pendente só na memória.** `RegisterAsync` devolve um `PendingRegistration` com as chaves. Fechar o app antes de confirmar significa cadastrar de novo: o servidor substitui a conta pendente e manda outro código. Derivar de novo pela senha não funciona, porque o `prelogin` esconde a conta não confirmada e devolve o sal falso. Guardar a `auth_key` no cofre só para isso não compensa.
3. **Cofre do Android fica para a 9.5.** Sem o app ligado, ele só seria exercitado por um teste manual, e o spike já provou o Keystore em API 26, API 36 e no celular real.
4. **`kdf_params` que vêm do servidor têm piso e teto.** Aceita só `v` igual a 1, `m` de 65536 a 262144 KiB, `t` de 3 a 10 e `p` de 1 a 4. Fora disso, o cliente recusa antes de derivar.
   - O piso impede que um servidor malicioso peça parâmetros fracos. Com eles, a `auth_key` enviada ficaria barata de testar contra senhas.
   - O teto impede que um servidor peça 4 GiB de memória ao celular.
5. **Senha normalizada em NFKC** antes de virar UTF-8. Um "ç" digitado com tecla morta (composto ou decomposto) dá a mesma chave nas duas plataformas. A senha tem de 10 a 1024 caracteres, contados depois da normalização.
6. **Derivação:**
   - `mestre = Argon2id(senha, kdf_salt, m, t, p)`, com 32 bytes.
   - `auth_key = HKDF-SHA256(mestre, info "slimegoals/v1/auth")` e `wrap_key = HKDF-SHA256(mestre, info "slimegoals/v1/wrap")`, ambas com 32 bytes e sem sal no HKDF.
   - O sal do cadastro tem 16 bytes aleatórios.
7. **Código de recuperação:** 128 bits aleatórios, mostrados em Base32 de Crockford, com 26 caracteres em grupos de 4 separados por hífen (o último com 2).
   - A leitura ignora caixa, espaços e hífens, e troca `O` por `0` e `I`/`L` por `1`.
   - `recovery_key = HKDF(código, "slimegoals/v1/recovery-auth")` e `recovery_wrap = HKDF(código, "slimegoals/v1/recovery-wrap")`. Não passa pelo Argon2id, porque o código já tem 128 bits.
8. **Embrulho da chave de dados:** `versão 1 (1 byte) ‖ nonce (12) ‖ cifra (32) ‖ tag (16)`, total de 61 bytes. É AES-256-GCM com a `wrap_key` ou a `recovery_wrap`. O dado autenticado é `"slimegoals/v1/wrap-password"` ou `"slimegoals/v1/wrap-recovery"`, o que impede trocar um embrulho pelo outro.
9. **Blob do registro:** `versão 1 (1 byte) ‖ nonce (12) ‖ cifra ‖ tag (16)`.
   - A cifra guarda o `SyncEnvelope` em JSON.
   - O dado autenticado é `"slimegoals/v1/record" ‖ record_key` em UTF-8, então um blob trocado de chave falha já no GCM. `Open` também confere `RecordKey(envelope.RecordId)`, como diz a §6.
   - Byte de versão desconhecido e versão do envelope desconhecida lançam `SyncFormatException`. Tag inválida lança `CryptographicException`.
   - As subchaves vêm da chave de dados: `enc = HKDF(dados, "slimegoals/v1/enc")` e `rk = HKDF(dados, "slimegoals/v1/record-key")`.
   - `record_key = base64url(HMAC-SHA256(rk, id em 16 bytes, ordem de Guid.TryWriteBytes com bigEndian: true))`, sem preenchimento, com 43 caracteres.
10. **O que vai ao cofre.** Um único segredo, de nome `sync-session`, guarda um JSON com `server`, `email`, `device_id` (o do servidor), `token` e `data_key` (base64). Um item só é gravado e apagado de uma vez.
    - No Secret Service, o item é criado na coleção `default`, com os atributos `application=slimegoals` e `name=sync-session`.
    - Coleção travada chama `Unlock` e espera o `Prompt`. Recusa ou ausência do serviço lançam `SecretVaultUnavailableException`.
11. **Cofre antes da rede.** `RegisterAsync`, `LoginAsync` e `RecoverAsync` conferem o cofre (`IsAvailableAsync`) antes do primeiro pedido. Sem cofre, a sincronização não liga, e nada é gravado em arquivo (§6).
12. **Ordem de entrada:**
    1. `login`;
    2. abrir a chave de dados;
    3. gravar no cofre;
    4. `EnableAsync`.

    Se o passo 2 ou o 3 falhar, o aparelho tenta `logout` no servidor, sem esperar o resultado, e não liga nada. Entrar com a sincronização já ligada é recusado: é preciso sair antes.
13. **Trocar a senha** mantém este aparelho e revoga os outros, como o servidor já faz. A chave de dados não muda, então ninguém baixa tudo de novo. O token deste aparelho continua valendo, e o cofre não muda.
14. **Sair (`SignOutAsync`):**
    1. tenta `POST /auth/logout`;
    2. apaga o segredo do cofre;
    3. chama `DisableAsync`.

    Falha de rede no passo 1 não impede os outros dois. Enviar a fila antes de sair, avisar do que se perde e cancelar o ciclo do motor ficam para a 9.5, que tem o diálogo.
15. **Apagar a conta** pede a senha. O app faz o `prelogin`, deriva a `auth_key` e chama `DELETE /account`. Depois disso, faz a mesma limpeza local de sair, e metas e registros ficam no aparelho (RS06).
16. **Erros da API:** `AccountApiException` com `Kind`, que pode ser:
    - `Unauthorized`;
    - `InvalidToken`;
    - `InvalidRequest`;
    - `RateLimited`, com `RetryAfter`;
    - `TooLarge`;
    - `NotFound`;
    - `Unavailable`, para rede, 5xx e resposta ilegível.

    Os textos para a pessoa ficam na 9.5.
17. **`device_id` local e do servidor seguem separados.** `sync_state.device_id` continua sendo o do envelope (9.2). O do servidor fica no cofre, para `IsCurrent` e para revogar.

## Global Constraints

- Identificadores em inglês; textos e comentários em pt-BR. `TreatWarningsAsErrors` ligado.
- "O servidor nunca recebe a senha, a chave de dados nem conteúdo legível" (RS04): nenhum DTO enviado contém senha, `mestre`, `wrap_key`, `recovery_wrap` ou chave de dados aberta.
- "Sem cofre disponível, a sincronização não liga: nunca em arquivo sem proteção" (§6).
- Senha com no mínimo 10 caracteres (§6).
- Core continua sem I/O e sem pacote novo (`ArchitectureTests`). Konscious só em `SlimeGoals.Sync`.
- `SlimeGoals.Sync` não entra em `SlimeGoals.Android.slnx`, e nenhum projeto do app o referencia (`git grep -n "SlimeGoals.Sync" -- src/SlimeGoals.App src/SlimeGoals.Android src/SlimeGoals.Desktop` vazio).
- Testes nunca usam o banco real, o app instalado nem o chaveiro de verdade do usuário.
- Comparações de segredo com `CryptographicOperations.FixedTimeEquals`; buffers de chave zerados com `CryptographicOperations.ZeroMemory` quando saem de uso.
- **Verificação:** `dotnet test --solution SlimeGoals.slnx` (Docker ligado) e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, sem avisos.
- **Commits:** Conventional Commits em português, escopos `sync`, `linux`, `specs`, `build`, com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Branch `feat/fase-9-4-cripto-e-conta`, a partir da `main`.

## Review Focus

1. Servidor malicioso ou com defeito manda `kdf_params` fracos, enormes ou malformados: o cliente recusa antes de derivar e não envia nada (Tarefa 1, Tarefa 5).
2. A mesma senha digitada com acento composto ou decomposto gera as mesmas chaves (Tarefa 1).
3. Blob trocado de chave, adulterado ou com versão nova; embrulho de senha usado como de recuperação: tudo recusado com o erro certo, sem derrubar o motor (Tarefas 1 e 2).
4. Falha no meio da entrada (cofre recusa depois do `login`): nada fica ligado, nada fica no cofre, e o token emitido é revogado (Tarefa 5).
5. Chaveiro ausente ou travado com o pedido recusado: `SecretVaultUnavailableException`, sem gravar nada em arquivo (Tarefa 3).

---

### Task 1: Derivação, código de recuperação e embrulho

**Files:**
- Create: `src/SlimeGoals.Sync/Crypto/KdfParams.cs`, `PasswordKeys.cs`, `RecoveryCode.cs`, `KeyWrap.cs`, `SyncKdf.cs`
- Modify: `Directory.Packages.props`, `src/SlimeGoals.Sync/SlimeGoals.Sync.csproj`
- Create: `tests/SlimeGoals.Sync.Tests/SlimeGoals.Sync.Tests.csproj`, `Crypto/KdfParamsTests.cs`, `PasswordKeysTests.cs`, `RecoveryCodeTests.cs`, `KeyWrapTests.cs`; incluir o projeto em `SlimeGoals.slnx`

**Interfaces:**
- Produces:
  - `KdfParams(int Version, int MemoryKiB, int Iterations, int Parallelism)`, com `static KdfParams Default` (1, 65536, 3, 1), `string ToJson()` (`{"v":1,"m":65536,"t":3,"p":1}`, sem espaços) e `static KdfParams Parse(string json)`, que lança `KdfParamsException` fora dos limites da decisão 4 ou com JSON inválido.
  - `PasswordKeys.Derive(string password, ReadOnlySpan<byte> salt, KdfParams p): PasswordKeys`, com `byte[] AuthKey` e `byte[] WrapKey`, e `IDisposable`, que zera as chaves. `PasswordKeys.Validate(string password)` lança `ArgumentException` com a mensagem "A senha deve ter de 10 a 1024 caracteres.".
  - `RecoveryCode`: `Generate(): RecoveryCode`, `Parse(string): RecoveryCode` (lança `FormatException`), `string Display` e `RecoveryKeys Keys()`, com `RecoveryKey` e `WrapKey`.
  - `KeyWrap.Wrap(ReadOnlySpan<byte> dataKey, ReadOnlySpan<byte> wrapKey, WrapPurpose purpose): byte[]` e `KeyWrap.Unwrap(byte[] wrapped, ReadOnlySpan<byte> wrapKey, WrapPurpose purpose): byte[]`, que lança `CryptographicException`. `WrapPurpose` é `Password` ou `Recovery`.

- [ ] **Step 1: Escrever os testes**
  - `Default_params_serialize_exactly`: `{"v":1,"m":65536,"t":3,"p":1}`.
  - `Parse_rejects_weak_huge_or_malformed_params` (Theory): `m` de 8 e de 300000, `t` de 1 e de 11, `p` de 0 e de 5, `v` igual a 2, JSON quebrado, campo ausente.
  - `Derive_matches_the_fixed_vector`: senha `"correct horse battery"`, sal `00 01 … 0f`, `Default`. Os bytes esperados de `AuthKey` e `WrapKey` são gerados uma vez nesta tarefa, conferidos à mão contra o Konscious com HKDF do .NET e gravados no teste. A tarefa 6 os reaproveita na spec.
  - `Composed_and_decomposed_accents_give_the_same_keys`: `"maçã-verde-10"` em NFC e em NFD.
  - `Different_salt_or_password_gives_different_keys`.
  - `Validate_rejects_short_and_long_passwords`: 9 e 1025 caracteres; 10 passa.
  - `Recovery_code_round_trips_and_tolerates_typing`: `Parse(Display)`; minúsculas, sem hífens, com `O`/`I`/`L`.
  - `Recovery_code_rejects_wrong_length_or_alphabet`.
  - `Recovery_keys_match_the_fixed_vector`: código fixo de 16 bytes.
  - `Wrap_round_trips_and_is_61_bytes`.
  - `Unwrap_rejects_wrong_key_tampering_and_swapped_purpose`.
- [ ] **Step 2: Rodar e ver falhar.** Comando: `dotnet test --project tests/SlimeGoals.Sync.Tests`. Esperado: falha de compilação nos tipos ausentes.
- [ ] **Step 3: Implementar.** Adicionar `Konscious.Security.Cryptography.Argon2` 1.3.1 ao `Directory.Packages.props`. `SyncKdf` concentra os `info` da decisão 6 e o HKDF. As chaves intermediárias (`mestre`) são zeradas.
- [ ] **Step 4: Rodar:** passa. Os testes de Argon2id usam `Default` e levam menos de 1 s cada; só o vetor e o de acentos usam Argon2id.
- [ ] **Step 5: Commit** `feat(sync): deriva chaves da senha e do código de recuperação`

### Task 2: `AesGcmSyncCrypto`

**Files:**
- Create: `src/SlimeGoals.Sync/Crypto/AesGcmSyncCrypto.cs`
- Test: `tests/SlimeGoals.Sync.Tests/Crypto/AesGcmSyncCryptoTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Sync/ConvergenceTests.cs` (um caso com a cripto real)

**Interfaces:**
- Consumes: `ISyncCrypto`, `SyncEnvelope`, `SyncCodec.FormatVersion`, `SyncFormatException`.
- Produces: `sealed class AesGcmSyncCrypto(ReadOnlySpan<byte> dataKey) : ISyncCrypto, IDisposable`; `static byte[] NewDataKey()`, com 32 bytes aleatórios.

- [ ] **Step 1: Escrever os testes**
  - `Seal_then_open_returns_the_envelope`.
  - `Record_key_matches_the_fixed_vector_and_is_43_chars`: chave de dados `00…1f` e id `00112233-4455-6677-8899-aabbccddeeff`.
  - `Same_envelope_sealed_twice_gives_different_blobs`.
  - `Blob_opened_under_another_record_key_is_rejected`: `CryptographicException`.
  - `Tampered_blob_is_rejected`: um bit trocado no nonce, na cifra e na tag.
  - `Blob_from_another_data_key_is_rejected`.
  - `Unknown_blob_version_byte_throws_format_exception`.
  - `Unknown_envelope_version_throws_format_exception`: selar à mão um envelope com `Version = 2`.
  - `Blob_does_not_contain_the_payload_in_clear`: o título `"Beber água"` não aparece nos bytes, nem em UTF-8 nem em base64.
  - Em `ConvergenceTests`: `Devices_converge_with_real_crypto`, com dois `SimulatedDevice` que dividem uma chave de dados.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** a decisão 9. O JSON do envelope usa um contexto gerado próprio, dentro de `SlimeGoals.Sync`.
- [ ] **Step 4: Rodar:** `tests/SlimeGoals.Sync.Tests` e `tests/SlimeGoals.Infrastructure.Tests` passam.
- [ ] **Step 5: Commit** `feat(sync): cifra os registros com AES-256-GCM`

### Task 3: Cofre de segredos

**Files:**
- Create: `src/SlimeGoals.Core/Abstractions/ISecretVault.cs`, `tests/SlimeGoals.Testing/InMemorySecretVault.cs`
- Create: `src/SlimeGoals.Platform.Linux/Secrets/SecretServiceVault.cs`
- Test: `tests/SlimeGoals.Platform.Linux.Tests/SecretServiceVaultTests.cs`, `IsolatedKeyring.cs`

**Interfaces:**
- Produces:
  - `interface ISecretVault`, com `Task<bool> IsAvailableAsync(ct)`, `Task<byte[]?> GetAsync(string name, ct)`, `Task SetAsync(string name, byte[] secret, ct)` (substitui) e `Task DeleteAsync(string name, ct)` (sem erro se não existir). Todos lançam `SecretVaultUnavailableException` quando o serviço falta ou o desbloqueio é recusado.
  - `sealed class SecretVaultUnavailableException(string message, Exception? inner = null)`, em `SlimeGoals.Core.Abstractions`.
  - `InMemorySecretVault`, com `bool Available` e `Exception? FailWritesWith`.
  - `SecretServiceVault(Func<Task<DBusConnection>> connect)`, mais uma fábrica `ForSessionBus()`.

- [ ] **Step 1: Escrever os testes.** `IsolatedKeyring` sobe um `dbus-daemon --session` privado e um `gnome-keyring-daemon --unlock --components=secrets`, com senha vazia pela entrada padrão, e devolve o endereço. Se faltar algum binário, o teste chama `Assert.Skip`. Nunca usar o barramento da sessão real.
  - `Set_get_and_replace_round_trip`.
  - `Delete_removes_and_tolerates_missing`.
  - `Get_of_missing_name_returns_null`.
  - `Items_are_tagged_with_application_and_name`, conferindo os atributos pela API `SearchItems`.
  - `Locked_collection_is_unlocked_through_the_prompt`: tranca a coleção com `Lock`. O `gnome-keyring-daemon` de teste aceita o desbloqueio sem diálogo, e o teste confirma que `Unlock` e `Prompt` foram usados. Se o daemon não permitir, cobrir o fluxo com um barramento falso que devolve um caminho de prompt e emite `Completed(dismissed: true)`, e então esperar `SecretVaultUnavailableException`.
  - `Missing_service_is_unavailable`: barramento sem `org.freedesktop.secrets`. `IsAvailableAsync` dá `false`, e `SetAsync` lança.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** com sessão `plain`, como no spike, e `MessageWriter` passado por `ref` (ver a Armadilha do spike). O segredo vai com `content_type` `application/octet-stream`, e o rótulo do item é "SlimeGoals: sincronização".
- [ ] **Step 4: Rodar:** passa. `ArchitectureTests` continua verde.
- [ ] **Step 5: Commit** `feat(linux): guarda segredos no Secret Service`

### Task 4: Cliente da API de conta

**Files:**
- Create: `src/SlimeGoals.Sync/Account/HttpAccountApi.cs`, `AccountApiException.cs`
- Test: `tests/SlimeGoals.Server.Tests/Account/HttpAccountApiTests.cs`

**Interfaces:**
- Consumes: os DTOs de `AuthContracts` e `SyncJsonContext`.
- Produces: `sealed class HttpAccountApi(HttpClient http)`, com os métodos abaixo. Toda falha lança `AccountApiException(AccountErrorKind Kind, TimeSpan? RetryAfter)`, nos tipos da decisão 16.
  - `PreloginAsync(email)`;
  - `RegisterAsync(RegisterRequest)`;
  - `ConfirmAsync(token, authKey)`;
  - `LoginAsync(email, authKey, deviceName)`;
  - `LogoutAsync(deviceToken)`;
  - `ChangePasswordAsync(deviceToken, ChangePasswordRequest)`;
  - `RecoverBeginAsync(email, recoveryKey)`;
  - `RecoverAsync(RecoverRequest)`;
  - `ListDevicesAsync(deviceToken)`;
  - `RevokeDeviceAsync(deviceToken, id)`;
  - `DeleteAccountAsync(deviceToken, authKey)`.

- [ ] **Step 1: Escrever os testes**, contra `ServerFactory` com chaves de teste fixas, sem cripto real:
  - `Each_route_round_trips`, um caso por método;
  - `Errors_map_to_kinds`: 401, `400 invalid_token`, `400 invalid_request`, 404, 413, e 429 com `Retry-After`;
  - `Unreadable_body_and_server_error_are_unavailable`, com um `HttpMessageHandler` falso;
  - `Network_failure_is_unavailable`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar,** reaproveitando o padrão de `HttpSyncTransport` para ler JSON e conferir campos nulos.
- [ ] **Step 4: Rodar:** passa.
- [ ] **Step 5: Commit** `feat(sync): adiciona o cliente da API de conta`

### Task 5: `AccountService`

**Files:**
- Create: `src/SlimeGoals.Sync/Account/AccountService.cs`, `PendingRegistration.cs`, `SyncSession.cs`, `SessionSecret.cs`
- Test: `tests/SlimeGoals.Server.Tests/Account/AccountServiceTests.cs`

**Interfaces:**
- Consumes: as tarefas 1 a 4 e `ISyncStateStore`.
- Produces: `sealed class AccountService(HttpAccountApi api, ISecretVault vault, ISyncStateStore state, Uri server)`, com:
  - `Task<PendingRegistration> RegisterAsync(string email, string password, ct)`. `PendingRegistration` tem `Email` e `RecoveryCode`, com as chaves internas.
  - `Task ConfirmAsync(PendingRegistration pending, string code, string deviceName, ct)`: confirma e entra.
  - `Task LoginAsync(string email, string password, string deviceName, ct)`.
  - `Task ChangePasswordAsync(string currentPassword, string newPassword, ct)`.
  - `Task<string> RecoverAsync(string email, string recoveryCode, string newPassword, string deviceName, ct)`: devolve o mesmo código, para a 9.5 lembrar a pessoa de guardá-lo.
  - `Task SignOutAsync(ct)`.
  - `Task DeleteAccountAsync(string password, ct)`.
  - `Task<IReadOnlyList<DeviceDto>> ListDevicesAsync(ct)` e `Task RevokeDeviceAsync(Guid id, ct)`.
  - `Task<SyncSession?> OpenSessionAsync(ct)`: devolve `ISyncCrypto` e um provedor de token para o `HttpSyncTransport`, ou nulo sem sessão no cofre.
- Erros: `AccountApiException`, `SecretVaultUnavailableException`, `KdfParamsException`, `ArgumentException` (senha e e-mail), e `InvalidOperationException` com "Saia da conta antes de entrar em outra." (decisão 12).

- [ ] **Step 1: Escrever os testes**, com `InMemorySecretVault`, `FakeEmailSender` (que dá o código) e um `ISyncStateStore` sobre um `TestDatabase`:
  - `Register_confirm_and_login_store_the_session_and_enable_sync`: cofre com `server`, `email`, `device_id`, `token` e `data_key`; `state.Enabled`.
  - `Register_sends_only_derived_material`: um `DelegatingHandler` grava os corpos enviados; nenhum contém a senha, a chave de dados nem o código de recuperação em claro ou em base64.
  - `Second_device_logs_in_and_opens_the_same_data_key`: blob selado no primeiro abre no segundo.
  - `Login_with_wrong_password_is_unauthorized_and_stores_nothing`.
  - `Login_refuses_weak_params_from_the_server_before_sending_anything`: um handler troca a resposta do `prelogin`, e nenhum `login` sai.
  - `Vault_unavailable_stops_before_any_request`: zero pedidos.
  - `Vault_failure_after_login_logs_out_and_enables_nothing`: `FailWritesWith`; o token emitido recebe `401` depois.
  - `Login_while_enabled_is_refused`.
  - `Change_password_keeps_this_device_and_revokes_the_others`: a senha velha dá `401`, e a nova entra e abre a mesma chave de dados.
  - `Recover_on_a_new_device_opens_the_data_and_revokes_everything`.
  - `Wrong_recovery_code_is_unauthorized`.
  - `Sign_out_clears_vault_and_state_even_offline`: o servidor fica fora no `logout`.
  - `Delete_account_removes_the_cloud_and_keeps_local_data`: com metas no `TestDatabase`; depois, `login` dá `401` e as metas continuam.
  - `Open_session_returns_null_without_a_session`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar** as decisões 2 e 10 a 15. O `SessionSecret` é o JSON da decisão 10, num contexto gerado. Chaves derivadas e abertas são zeradas no `finally`.
- [ ] **Step 4: Rodar:** passa.
- [ ] **Step 5: Commit** `feat(sync): adiciona o serviço de conta`

### Task 6: Ponta a ponta, privacidade e documentos

**Files:**
- Test: `tests/SlimeGoals.Server.Tests/Account/EncryptedSyncEndToEndTests.cs`
- Modify: `specs/Sincronizacao_v1.md`, `README.md`, `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: `AccountService.OpenSessionAsync`, `SyncEngine`, `HttpSyncTransport`, `SimulatedDevice` ou equivalente com `TestDatabase` real.

- [ ] **Step 1: Escrever os testes**
  - `Two_devices_sync_encrypted_through_the_real_server`: cadastro no primeiro, entrada no segundo, metas e registros nas duas pontas, e os dois iguais depois de sincronizar.
  - `Database_dump_has_no_readable_content`: título, unidade e nota conhecidos, com acento, e a senha. Varre todas as colunas de texto e `bytea` de `accounts`, `devices` e `records` em UTF-8, UTF-16 e base64, e não acha nada (§9 Privacidade).
  - `Device_revoked_by_password_change_signs_out_and_back_in_without_duplicates`: o aparelho revogado recebe `401` no ciclo; depois de sair e entrar com a senha nova, baixa tudo de novo e termina com as mesmas metas e registros, sem duplicatas.
  - `Recovered_device_reads_everything`.
- [ ] **Step 2: Rodar.** O código já existe, então uma falha aqui é divergência real: achar a causa e registrar.
- [ ] **Step 3: Atualizar a spec**, numa seção "Decisões da fase 9.4":
  - decisões 2, 4 a 12 e 14 a 17, com os vetores fixos da Tarefa 1 e da Tarefa 2 para a 9.5 conferir no Android;
  - as obrigações da 9.5: cofre do Android, vetores no Android, fluxo de sair com envio da fila e aviso, `DisableAsync` com cancelamento do ciclo, endereço configurável e TLS.
- [ ] **Step 4: Atualizar o `README.md`** (o que a 9.4 entrega e o chaveiro do GNOME nos testes) e o `THIRD-PARTY-NOTICES.md`, com o Konscious e o Blake2 dele, ambos MIT.
- [ ] **Step 5: Rodar a verificação completa** da seção Global Constraints e o `git grep`.
- [ ] **Step 6: Commit** `test(sync): prova a sincronização cifrada de ponta a ponta` e `docs(specs): registra as decisões da fase 9.4`
