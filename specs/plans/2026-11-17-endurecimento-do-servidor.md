# Endurecimento do servidor: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** fechar os 9 itens da auditoria que bloqueiam expor o servidor: TLS, `Production`, proxy, papéis do banco, convite, segredos em arquivo, contêiner endurecido, limites sem travar a vítima e token com validade. Também entra o envio real do e-mail, em segundo plano.

**Architecture:** o servidor ASP.NET Core ganha guardas de partida fora de `Development`, `ForwardedHeaders`, limites novos em `RateLimits`, a fila de e-mail com `SmtpEmailSender` e o convite no cadastro. O banco passa a ter dois papéis sem privilégio especial, criados por um script SQL que os testes também usam. O `compose.yaml` passa a ser o de produção, com Caddy, segredos em arquivo e contêineres endurecidos; o `compose.dev.yaml` refaz o fluxo de desenvolvimento de hoje. No app, só três mudanças: o endereço exige `https`, o cadastro ganha o campo de convite e o texto de sessão encerrada cita os 30 dias.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Npgsql + Dapper, PostgreSQL 18, MailKit, Caddy 2, Docker Compose, xUnit v3 + Testcontainers, Avalonia 12.

**Spec:** `specs/Endurecimento_do_servidor_v1.md` (as decisões D1 a D11 estão na §3). Fundo: `specs/Sincronizacao_v1.md` §5.

## Global Constraints

- `TreatWarningsAsErrors`: o build não pode ter warning. `dotnet build SlimeGoals.slnx` e `dotnet test --solution SlimeGoals.slnx` passam ao fim de cada tarefa. O Docker precisa estar rodando.
- Nenhum segredo, token, hash, chave, convite, senha, string de conexão ou e-mail completo em respostas, logs, mensagens de exceção ou telas. E-mail em log só mascarado, com `RateLimits.MaskEmail`. Toda mensagem de falha de partida é texto fixo e nunca ecoa um valor da configuração.
- As respostas de erro continuam `{"error":"<código>"}`. Erros genéricos para o usuário; o detalhe só no log, sem dado sensível.
- Fora de `Development`, configuração insegura impede a partida (`ValidateOnStart` ou exceção antes do `app.Run()`). Nunca só um aviso no log.
- TDD: cada comportamento novo começa por um teste que falha. Os testes de servidor usam `ServerFactory` e `PostgresFixture`.
- Conventional Commits em português, um por tarefa, escopo `server`, `sync`, `app` ou `build`. Os commits terminam com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Não abrir nem apagar `~/.local/share/SlimeGoals/`. Não rodar `docker compose down -v`. Não exibir o conteúdo de `.env` nem de `secrets/`.
- Pacote novo só o MailKit, na última versão estável 4.x sem vulnerabilidade conhecida (`dotnet list package --vulnerable`). Ele entra no `Directory.Packages.props` e no `THIRD-PARTY-NOTICES.md`.
- Imagens Docker com tag e digest (`postgres:18-alpine@sha256:…`). O digest vem de `docker buildx imagetools inspect <imagem>` no dia.

## Review Focus

1. **Cabeçalho `X-Forwarded-For` forjado direto no servidor:** um cliente fora da rede confiável que manda `X-Forwarded-For: 1.2.3.4` continua contado pelo próprio endereço. Testado na Tarefa 3, em `Spoofed_forwarded_for_from_untrusted_peer_is_ignored`.
2. **Aparelho parado entre 29 e 30 dias:** `last_seen_at` só anda de hora em hora, então o corte precisa ser `now - last_seen_at >= 30 dias`, contado do último registro. 29 dias e 23 h ainda entra, 30 dias não. Testado na Tarefa 5, nos dois lados do limite.
3. **Convite digitado com espaço nas pontas ou colado com quebra de linha:** o app apara as pontas e o servidor também. Fora isso, a comparação é exata (maiúsculas contam). Testado nas Tarefas 6 e 8.
4. **Envio de e-mail que falha ou fila cheia:** a conta pendente some e o log não leva o código nem o e-mail completo. Um novo cadastro do mesmo e-mail funciona depois. Testado na Tarefa 7.
5. **Pedidos legítimos de muitos e-mails atrás do mesmo NAT:** o limite (a) é por par e-mail + IP, então colegas no mesmo IP não se bloqueiam entre si. Testado na Tarefa 4, em `Email_ip_limit_is_per_pair`.

---

### Task 1: Banco sem senha padrão e com papéis sem privilégio (D4, D6)

**Files:**
- Create: `packaging/server/postgres/roles.sql`. Cria os papéis dono e app, passa o banco para o dono, dá ao app `CONNECT`, `USAGE` no schema `public` e `ALTER DEFAULT PRIVILEGES FOR ROLE <dono> IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES` e `USAGE, SELECT ON SEQUENCES`, e tira `CREATE` e `TEMP` de `PUBLIC`. Usa as variáveis do psql `:"owner_role"`, `:"app_role"`, `:"db_name"`, `:'owner_password'` e `:'app_password'`.
- Create: `src/SlimeGoals.Server/Data/DatabaseRoleGuard.cs`
- Modify: `src/SlimeGoals.Server/Data/Db.cs`, `Data/ServerMigrator.cs`, `Program.cs`, `appsettings.json` (sai `ConnectionStrings`)
- Modify: `tests/SlimeGoals.Server.Tests/PostgresFixture.cs`, `ServerFactory.cs` e os testes que leem `factory.ConnectionString`
- Test: `tests/SlimeGoals.Server.Tests/Data/DatabaseRoleTests.cs`

**Interfaces:**
- Produces: `Db.OpenAsync(ct)`, que usa `ConnectionStrings:Postgres` (papel do app), e `Db.OpenForMigrationsAsync(ct)`, que usa `ConnectionStrings:PostgresMigrations` e, se ela estiver vazia, `Postgres`. `ServerMigrator` passa a usar a segunda.
- Produces: `static Task DatabaseRoleGuard.EnsureSafeAsync(Db db, CancellationToken ct)`. Roda antes das migrações, só fora de `Development`. Lança `InvalidOperationException("O papel do banco do servidor não pode ser superusuário, criar papéis ou bancos, nem ser o das migrações.")` quando `rolsuper`, `rolcreaterole` ou `rolcreatedb` do `current_user` forem verdadeiros, ou quando os dois papéis forem o mesmo.
- Produces: `PostgresFixture.NewDatabaseAsync()` devolve `TestDatabase(string App, string Owner)`. Ele cria o banco e aplica `roles.sql` com nomes únicos por teste (`app_<guid>`, `owner_<guid>`; os papéis valem para o cluster inteiro e os testes rodam em paralelo). Para isso, troca as variáveis do psql pelo texto, com as aspas certas. `ServerFactory` liga as duas strings; `factory.ConnectionString` passa a ser a do app, e `factory.OwnerConnectionString` é nova.
- Sem string de conexão, `Program` lança `InvalidOperationException("ConnectionStrings:Postgres não configurada.")` para string ausente **ou vazia**.
- Produces: `static ServerFactory ServerFactory.Production(PostgresFixture postgres, IDictionary<string, string?>? overrides = null)`, um servidor em `Production` com configuração válida. Ele liga `PreloginSecret`, os dois papéis distintos e o `FakeEmailSender` no lugar do envio, também em `Production`. As Tarefas 6 e 7 usam essa fábrica, e a 7 a atualiza para a configuração de e-mail nova. Os testes que esperam uma recusa partem dela e estragam uma chave só. Atenção: ler `IOptions<ServerOptions>.Value` em `Program` roda todos os validadores, então a guarda de papéis precisa vir antes dessa leitura, ou o teste vê a falha de outro validador.

- [ ] **Step 1: Escrever os testes que falham**, em `DatabaseRoleTests`:
  - `App_role_cannot_create_or_drop_tables`: com `App`, `CREATE TABLE x(i int)` e `DROP TABLE accounts` lançam `PostgresException` com `SqlState == "42501"`.
  - `App_role_reads_and_writes_rows_after_migrations`: depois de `ServerMigrator.RunAsync`, `INSERT` e `DELETE` em `accounts` funcionam com `App`.
  - `Production_refuses_superuser_connection`: `ServerFactory` em `Production`, com `ConnectionStrings:Postgres` igual à string de superusuário da fixture, falha ao criar o cliente. A mensagem da exceção não contém a senha nem o host.
  - `Production_refuses_same_role_for_app_and_migrations`.
  - `Missing_connection_string_fails_with_fixed_message`: `ConnectionStrings:Postgres` igual a `""`.
- [ ] **Step 2:** `dotnet test --project tests/SlimeGoals.Server.Tests -- --filter-class "*DatabaseRoleTests"` (sintaxe do MTP: confira em `global.json` e no README). Esperado: FAIL.
- [ ] **Step 3: Implementar** `roles.sql`, `Db.OpenForMigrationsAsync`, `DatabaseRoleGuard` e a fixture nova. Ajustar os testes que fazem DDL direto para `OwnerConnectionString`.
- [ ] **Step 4:** a suíte inteira do servidor passa rodando como papel do app. Esse é o teste de que o app não precisa de mais privilégio do que tem.
- [ ] **Step 5: Commit** `feat(server): separa os papéis do banco e recusa superusuário fora de Development`

### Task 2: Segredos lidos de arquivos (D7, parte do servidor)

**Files:**
- Modify: `src/SlimeGoals.Server/Program.cs`
- Test: `tests/SlimeGoals.Server.Tests/SecretsDirectoryTests.cs`

**Interfaces:**
- Produces: `builder.Configuration.AddKeyPerFile(builder.Configuration["SecretsDirectory"] ?? "/run/secrets", optional: true)`, adicionado por último, para o arquivo valer mais que `environment:`. O `KeyPerFile` já vem no framework compartilhado do ASP.NET Core; não precisa de pacote. Nome de arquivo com `__` vira `:`.

- [ ] **Step 1:** teste `Secret_file_overrides_configuration`. Numa pasta temporária, um arquivo `Server__PreloginSecret`; `SecretsDirectory` aponta para ela; o sal falso do `prelogin` para `x@exemplo.com` é igual ao `HMAC-SHA-256` calculado com o conteúdo do arquivo (os 16 primeiros bytes). Se `UseSetting` não chegar a `builder.Configuration` antes do `Build()`, use `WebApplicationFactory.WithWebHostBuilder` com `ConfigureAppConfiguration`, mas a leitura em produção fica igual.
- [ ] **Step 2:** rodar. Esperado: FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** rodar a suíte do servidor. Esperado: PASS.
- [ ] **Step 5: Commit** `feat(server): lê segredos de /run/secrets`

### Task 3: Proxy confiável e limites gerais (D3, D10)

**Files:**
- Rename: `src/SlimeGoals.Server/Limits/AuthRateLimits.cs` → `RateLimits.cs` (classe `RateLimits`), atualizando quem usa
- Create: `src/SlimeGoals.Server/Limits/ClientIp.cs`
- Modify: `Limits/LimitResults.cs`, `Program.cs`, `ServerOptions.cs`, `appsettings.json`, `Sync/SyncEndpoints.cs`
- Test: `tests/SlimeGoals.Server.Tests/Limits/ForwardedHeadersTests.cs`, `Limits/RateLimitTests.cs`

**Interfaces:**
- Produces: `Counter(TimeProvider time, TimeSpan window)`. A janela passa a ser do contador, porque a Tarefa 4 precisa de uma janela de uma hora.
- Produces: `RateLimits.HitRequest(string ip)`, com `Limits:RequestsPerIpPerMinute = 600`, e `RateLimits.HitDevice(Guid deviceId)`, com `Limits:SyncPerDevicePerMinute = 300`. As duas devolvem `TimeSpan?`, como `HitIp`.
- Produces: `static string ClientIp.Of(HttpContext context)`, que devolve `RemoteIpAddress?.ToString() ?? "desconhecido"`. É a única leitura do IP no servidor.
- Produces: `ServerSection.TrustedProxyNetworks`, um `string[]` em CIDR. `UseForwardedHeaders` é o primeiro middleware, com `ForwardedHeaders.XForwardedFor | XForwardedProto`, `ForwardLimit = 1`, `KnownProxies` e `KnownIPNetworks` limpos e depois preenchidos só com essa lista. CIDR inválido impede a partida (`ServerOptionsValidator`).
- O limite por IP de todas as rotas é um middleware depois dos limites de corpo; `/health` fica de fora. O limite por aparelho é um filtro no grupo `/sync`, depois da autorização, e lê `CurrentDevice`.

- [ ] **Step 1: Testes que falham.** Um `IStartupFilter` de teste fixa `Connection.RemoteIpAddress` (por exemplo, `10.9.0.2`):
  - `Trusted_proxy_forwarded_for_separates_clients`: com `TrustedProxyNetworks = ["10.9.0.0/24"]` e `AuthPerIpPerMinute = 2`, dois clientes com `X-Forwarded-For` diferentes fazem 2 `prelogin` cada, sem 429.
  - `Spoofed_forwarded_for_from_untrusted_peer_is_ignored`: sem rede confiável, com o mesmo limite, o terceiro pedido do mesmo `RemoteIpAddress` recebe 429, mesmo trocando o `X-Forwarded-For`.
  - `All_routes_answer_429_past_the_request_ip_limit`: com `RequestsPerIpPerMinute = 3`, o quarto `GET /devices` recebe 429 com `Retry-After`. Depois disso, `/health` ainda responde 200.
  - `Sync_answers_429_past_the_device_limit`: com `SyncPerDevicePerMinute = 2`, o terceiro `GET /sync/pull` recebe 429. Um segundo aparelho da mesma conta continua com 200.
  - `Invalid_trusted_proxy_network_fails_startup`.
- [ ] **Step 2:** rodar. Esperado: FAIL.
- [ ] **Step 3:** implementar. O `ServerFactory` sobe os limites novos para `100000`, como os que já existem.
- [ ] **Step 4:** rodar a suíte do servidor. Esperado: PASS.
- [ ] **Step 5: Commit** `feat(server): confia só no proxy configurado e limita pedidos por IP e por aparelho`

### Task 4: Limites por e-mail que não travam a vítima (D8)

**Files:**
- Modify: `Limits/RateLimits.cs`, `Accounts/AccountEndpoints.cs`, `ServerOptions.cs`, `appsettings.json`
- Modify: `tests/SlimeGoals.Server.Tests/Limits/RateLimitTests.cs`, onde `Login_answers_429_past_the_email_limit_from_any_ip` sai e entram os testes abaixo

**Interfaces:**
- Consumes: `Counter(TimeProvider, TimeSpan)` e `ClientIp.Of` (Tarefa 3).
- Produces:
  - `RateLimits.HitEmailFromIp(string email, string ip)`, com `Limits:AuthPerEmailPerIpPerMinute = 10`. A chave é o SHA-256 de `email + "\n" + ip`. Vale em `prelogin`, `register`, `login`, `recover/begin` e `recover`, no lugar de `HitEmail`, que sai.
  - `RateLimits.ReserveLoginAttempt(string email)` e `RefundLoginAttempt(string email)`, com `Limits:LoginFailuresPerEmailPerHour = 50` e janela de 1 hora, no padrão de `ReserveKeyAttempt`. Só em `login` e `recover`: a reserva vem antes de comparar a chave, e o acerto devolve a reserva. E-mail desconhecido também gasta, para a resposta não revelar quem tem conta.
  - `RateLimits.HitConfirmationEmail(string email)`, com `Limits:ConfirmationEmailsPerAddressPerHour = 3`. Só quando o cadastro vai mandar e-mail. Passou do limite: a resposta continua `202`, nenhum e-mail sai e a conta pendente fica como estava, para a resposta não mudar.
- `Limits:AuthPerEmailPerMinute` sai de `ServerOptions`, do `appsettings.json` e do `ServerFactory`.

- [ ] **Step 1: Testes que falham:**
  - Os IPs diferentes vêm pelo proxy confiável da Tarefa 3: o mesmo `IStartupFilter`, com `TrustedProxyNetworks`.
  - `Prelogin_flood_from_other_ips_does_not_block_victim_login`: 20 `prelogin` do e-mail da vítima, de 20 `X-Forwarded-For` diferentes por um proxy confiável. Depois, o login da vítima, de outro IP, recebe 200.
  - `Email_ip_limit_is_per_pair`: com o limite em 2, o mesmo IP faz 2 `prelogin` de `a@` e 2 de `b@` sem 429. O terceiro de `a@` recebe 429.
  - `Login_failures_per_email_lock_after_limit_from_any_ip`: com o limite em 3, 3 logins errados de IPs diferentes e depois a chave certa recebe 429. Na hora seguinte (`factory.Time.Advance(1h)`), a chave certa recebe 200.
  - `Successful_login_does_not_spend_failure_budget`: com o limite em 2, 5 logins certos seguidos recebem 200.
  - `Confirmation_email_capped_per_address_per_hour`: 4 cadastros pendentes do mesmo e-mail, de IPs diferentes, recebem 4 vezes `202`, mas `factory.Email.Count == 3`.
- [ ] **Step 2:** rodar. Esperado: FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** rodar a suíte do servidor. Esperado: PASS.
- [ ] **Step 5: Commit** `fix(server): limita por e-mail e IP para não travar o login da vítima`

### Task 5: Token vence depois de 30 dias parado (D9)

**Files:**
- Modify: `Accounts/AccountStore.cs` (`FindActiveDeviceAsync` e um `RevokeIdleDevicesAsync` novo), `Sync/MaintenanceService.cs`, `ServerOptions.cs`, `appsettings.json`
- Modify: `src/SlimeGoals.App/Account/AccountViewModel.cs` (`SessionExpiredText`, com o texto exato da spec §4)
- Test: `tests/SlimeGoals.Server.Tests/Accounts/DeviceIdleTests.cs`, e o teste de texto do app que já existir para `SessionExpiredText` (ou um novo em `tests/SlimeGoals.App.Tests`)

**Interfaces:**
- Produces: `AccountStore.FindActiveDeviceAsync(tokenHash, now, ct)` só devolve o aparelho com `now - last_seen_at < DeviceIdleDays`.
- Produces: `Task<int> AccountStore.RevokeIdleDevicesAsync(DateTimeOffset now, CancellationToken ct)` grava `revoked_at = now` em quem passou do prazo e devolve quantos. A manutenção diária chama e registra só o número.

- [ ] **Step 1: Testes que falham:**
  - `Device_idle_just_under_30_days_still_authenticates`: com `last_seen_at` em `now - 30d + 1h`, a rota protegida responde 200.
  - `Device_idle_30_days_is_rejected`: com `now - 30d`, responde 401 `unauthorized`.
  - `Active_device_never_expires`: a cada 25 dias, uma sincronização. Depois de 100 dias, ainda responde 200.
  - `Maintenance_revokes_idle_devices`: `RevokeIdleDevicesAsync` devolve 1, e `GET /devices` de outro aparelho ativo não lista mais o vencido.
- [ ] **Step 2:** rodar. Esperado: FAIL.
- [ ] **Step 3:** implementar. Mudar o texto do app.
- [ ] **Step 4:** rodar a suíte inteira. Esperado: PASS.
- [ ] **Step 5: Commit** `feat(server): token do aparelho vence depois de 30 dias sem uso`

### Task 6: Cadastro só com convite e cota ligada (D5, servidor e contrato)

**Files:**
- Modify: `src/SlimeGoals.Sync.Contracts/AuthContracts.cs` (`RegisterRequest` ganha `string? InviteCode`, no fim, que vira `invite_code` pelo snake_case)
- Modify: `Accounts/AccountEndpoints.cs`, `ServerOptions.cs`, `appsettings.json` (`MaxRecordsPerAccount: 100000`)
- Test: `tests/SlimeGoals.Server.Tests/Accounts/InviteTests.cs`, `Limits/QuotaTests.cs`

**Interfaces:**
- Produces: `ServerSection.InviteCode` (string, vazio por padrão). A regra: em `Development` com convite vazio, o cadastro fica aberto. Com convite configurado, em qualquer ambiente, exige `InviteCode.Trim()` igual ao configurado, pelos bytes UTF-8, com `CryptographicOperations.FixedTimeEquals`; tamanhos diferentes também falham. Fora de `Development` com convite vazio, o cadastro fica fechado.
- A conferência vem logo depois do limite por IP e antes da validação do corpo: convite errado não chega no banco nem no limite por e-mail. A falha responde `403 {"error":"registration_closed"}`.

- [ ] **Step 1: Testes que falham:**
  - `Register_without_invite_is_closed_when_invite_configured`
  - `Register_with_wrong_invite_is_closed` (o mesmo corpo de erro do caso anterior)
  - `Register_with_invite_padded_by_whitespace_is_accepted` (`" código\n"`)
  - `Register_invite_is_case_sensitive`
  - `Production_without_invite_closes_registration` (`ServerFactory` em `Production` com o resto da configuração válida; a fábrica de `Production` vem da Tarefa 1)
  - `Development_without_invite_keeps_registration_open`
  - `Default_quota_is_100000`: `IOptions<ServerOptions>` sem sobrescrita devolve `100000`.
- [ ] **Step 2:** rodar. Esperado: FAIL.
- [ ] **Step 3:** implementar. Os testes que já existem continuam em `Development` sem convite.
- [ ] **Step 4:** rodar a suíte do servidor. Esperado: PASS.
- [ ] **Step 5: Commit** `feat(server): cadastro só com convite e cota ligada por padrão`

### Task 7: E-mail real por SMTP, em segundo plano (D11, e D2 no servidor)

**Files:**
- Create: `src/SlimeGoals.Server/Accounts/ConfirmationEmailQueue.cs`, `Accounts/EmailDispatchService.cs`, `Accounts/SmtpEmailSender.cs`
- Modify: `Accounts/IEmailSender.cs` (o `EmailSenderValidator` passa a olhar `Email:Mode`), `Accounts/AccountEndpoints.cs`, `Program.cs`, `ServerOptions.cs`, `appsettings.json`, `SlimeGoals.Server.csproj`, `Directory.Packages.props`, `THIRD-PARTY-NOTICES.md`
- Modify: `tests/SlimeGoals.Server.Tests/FakeEmailSender.cs`, que ganha `Task<string> WaitForTokenAsync(string email, TimeSpan timeout)`. `TestAccount` e os testes que leem `LastToken` ou `Count` logo depois do cadastro passam a esperar com ele, inclusive `Confirmation_email_capped_per_address_per_hour`, da Tarefa 4. `ServerFactory.Production` passa a ligar `Email:Mode = smtp`, `Host` e `From` e continua trocando o envio pelo `FakeEmailSender`.
- Test: `tests/SlimeGoals.Server.Tests/Accounts/EmailDispatchTests.cs`, `Accounts/SmtpEmailSenderTests.cs` (Mailpit pelo Testcontainers genérico, `axllent/mailpit` fixado por digest), `ProductionErrorTests.cs`

**Interfaces:**
- Produces: `sealed record ConfirmationEmail(string Email, string Token, byte[] TokenHash)`. `ConfirmationEmailQueue` é um `Channel` limitado a 100, com `bool TryEnqueue(ConfirmationEmail)` e `IAsyncEnumerable<ConfirmationEmail> ReadAllAsync(ct)`.
- Produces: `EmailDispatchService : BackgroundService` chama `IEmailSender.SendConfirmationAsync` em até 3 tentativas. As esperas entre elas (`Email:RetryDelaySeconds`, padrão `[2, 10]`, com 0 nos testes) usam o `TimeProvider`. Se as 3 falharem: `AccountStore.DeleteUnconfirmedAsync(TokenHash)` e um aviso no log com o e-mail mascarado e sem o token.
- O cadastro, quando vai mandar e-mail, chama `TryEnqueue`. Fila cheia: apaga a conta pendente na hora. Nos dois casos responde `202`. O `try/catch` síncrono de hoje sai.
- Produces: `SmtpEmailSender(IOptions<ServerOptions>, TimeProvider)`, com MailKit. Assunto: `Código de confirmação do SlimeGoals`. Corpo em texto puro, em português: o código, "vale por 24 horas" e "Se não foi você, ignore este e-mail."
- Produces: `ServerOptions.Email`, com `Mode` (`log` | `smtp`) e `Smtp` (`Host`, `Port = 587`, `Username`, `Password`, `From`, `Security = StartTls`). O `Security` aceita `StartTls`, `SslOnConnect` e `None`. `Program` registra `SmtpEmailSender` quando `Mode = smtp` e `LogEmailSender` quando `log`.
- O validador impede a partida fora de `Development` quando: `Mode = log`; `Mode = smtp` sem `Host` ou sem `From`; `Security = None`; ou `Username` sem `Password`.

- [ ] **Step 1: Testes que falham:**
  - `Register_responds_before_email_is_sent`: com um `IEmailSender` que só termina quando o teste solta um `TaskCompletionSource`, o cadastro responde `202` antes de o envio terminar.
  - `Failed_send_after_retries_deletes_pending_account`: o envio sempre lança; depois das 3 tentativas, a conta pendente não existe. O log não contém o e-mail completo nem o token. Um novo cadastro do mesmo e-mail é aceito.
  - `Full_queue_deletes_pending_account_and_still_answers_202`: com capacidade 1 e o envio travado, o segundo cadastro (outro e-mail) responde `202`, e a conta dele não existe.
  - `Smtp_sender_delivers_code_through_mailpit`: em `Development`, com `Security = None` contra o Mailpit, a mensagem chega a `/api/v1/messages` com o assunto exato e o código no corpo.
  - `Production_rejects_log_mode`, `Production_rejects_smtp_without_tls`, `Production_rejects_username_without_password`
  - `Production_error_has_no_stack_trace`: uma rota de teste que lança, em `Production` válido, responde 500, e o corpo não contém o nome da exceção nem `at `.
- [ ] **Step 2:** rodar. Esperado: FAIL.
- [ ] **Step 3:** implementar. Adicionar o MailKit e o aviso de licença.
- [ ] **Step 4:** rodar a suíte inteira. Esperado: PASS. `dotnet list package --vulnerable` sem nada.
- [ ] **Step 5: Commit** `feat(server): envia a confirmação por SMTP em segundo plano`

### Task 8: App exige https e manda o convite (D1 e D5 no app)

**Files:**
- Modify: `src/SlimeGoals.App/Account/ServerAddress.cs`, `Account/AccountViewModel.cs`, a view do cadastro em `src/SlimeGoals.App/Views` (procure `SubmitRegister`), `Account/AccountMessages.cs`
- Modify: `src/SlimeGoals.Sync/Account/AccountService.cs`, `Account/HttpAccountApi.cs`, `Account/AccountApiException.cs`, `ISyncAccount.cs`, `SyncCoordinator.cs`
- Test: `tests/SlimeGoals.App.Tests` (ServerAddress e AccountViewModel), `tests/SlimeGoals.Sync.Tests` ou `tests/SlimeGoals.Server.Tests/Account/HttpAccountApiTests.cs`

**Interfaces:**
- Consumes: `RegisterRequest.InviteCode` e `403 registration_closed` (Tarefa 6).
- Produces: `ServerAddress.HttpsRequiredText = "Use https://. O http:// só vale para 127.0.0.1."`. `TryParse` aceita `http` só com `Uri.IsLoopback`; fora disso, dá esse erro.
- Produces: `AccountErrorKind.RegistrationClosed`. O `HttpAccountApi` lê o código do corpo nos 403 e mapeia `registration_closed` para ele.
- Produces: `AccountService.RegisterAsync(string email, string password, string? inviteCode, CancellationToken ct)` e `ISyncAccount.RegisterAsync(Uri server, string email, string password, string? inviteCode, CancellationToken ct)`. O convite vai aparado; vazio vai como `null`.
- Produces: `AccountViewModel.InviteCode`, ligado ao campo "Código de convite", com a dica "Peça a quem cuida do servidor.", abaixo da confirmação da senha. A mensagem de `RegistrationClosed` é o texto exato da spec §3.

- [ ] **Step 1: Testes que falham:**
  - `Http_on_loopback_is_accepted`: `http://127.0.0.1:8080`, `http://localhost:8080` e `http://[::1]:8080`.
  - `Http_elsewhere_requires_https`: `http://exemplo.com` dá `HttpsRequiredText`.
  - `Https_anywhere_is_accepted`.
  - `Register_sends_trimmed_invite_or_null`: `"  abc \n"` vai como `"abc"`, e `"   "` vai como `null`.
  - `Registration_closed_maps_to_message`: o 403 `registration_closed` mostra o texto da spec.
- [ ] **Step 2:** rodar. Esperado: FAIL.
- [ ] **Step 3:** implementar. Conferir o campo na Render a 320, 360 e 1280 px: `dotnet run --project tools/SlimeGoals.Render -- /tmp/render all`, e ler os PNGs da página de conta.
- [ ] **Step 4:** rodar a suíte inteira, e também `dotnet build SlimeGoals.Android.slnx` (só o XA0033 de sempre).
- [ ] **Step 5: Commit** `feat(app): exige https fora do 127.0.0.1 e manda o código de convite`

### Task 9: Compose de produção, Caddy e contêineres endurecidos (D1, D2, D3, D7)

**Files:**
- Modify: `compose.yaml` (produção), `src/SlimeGoals.Server/Dockerfile`, `packaging/server/init-env.sh`, `.gitignore` (`secrets/`), `.env.example`
- Create: `compose.dev.yaml`, `packaging/server/Caddyfile`, `packaging/server/postgres/10-roles.sh`, `packaging/server/upgrade-roles.sh`, `.github/dependabot.yml`

**Decisões que o implementador não toma sozinho:**
- **Redes:** `edge` tem a sub-rede fixa `10.89.0.0/24` e liga o Caddy ao servidor; o servidor recebe `Server__TrustedProxyNetworks__0: 10.89.0.0/24`. `data` tem `internal: true` e liga o servidor ao banco. O banco só está em `data`.
- **Portas:** só o Caddy publica: `${HTTP_PORT:-80}:80` e `${HTTPS_PORT:-443}:443`. O servidor não publica porta nenhuma.
- **Caddyfile:** `{$SLIMEGOALS_DOMAIN}`, com `reverse_proxy server:8080`, `header Strict-Transport-Security "max-age=31536000"` e `header -Server`. O `email` do ACME vem de `{$ACME_EMAIL}`, opcional.
- **Servidor:** `ASPNETCORE_ENVIRONMENT: Production`, `AllowedHosts: ${SLIMEGOALS_DOMAIN}`, `Email__Mode: smtp`, `Email__Smtp__Host`, `Port`, `Username` e `From` vindos do `.env`, e `DOTNET_EnableDiagnostics: 0`. Os segredos ficam em `secrets:`, montados em `/run/secrets/<chave>`: `ConnectionStrings__Postgres`, `ConnectionStrings__PostgresMigrations`, `Server__PreloginSecret`, `Server__InviteCode` e `Email__Smtp__Password`.
- **Banco:** `POSTGRES_PASSWORD_FILE` e o `10-roles.sh` em `/docker-entrypoint-initdb.d/`. O script lê `owner_password` e `app_password` de `/run/secrets` e roda `roles.sql` com `psql -v`. O `healthcheck` continua com `pg_isready`.
- **Endurecimento, nos três serviços:** `read_only: true`, `security_opt: [no-new-privileges:true]`, `cap_drop: [ALL]`, `pids_limit: 200`, e `tmpfs` em `/tmp`. O banco também tem `tmpfs` em `/var/run/postgresql` e `cap_add: [CHOWN, DAC_OVERRIDE, FOWNER, SETGID, SETUID]`. O Caddy tem `cap_add: [NET_BIND_SERVICE]` e volumes `caddy_data` e `caddy_config`.
- **Imagens:** `postgres:18-alpine`, `caddy:2-alpine`, `mcr.microsoft.com/dotnet/sdk:10.0` e `aspnet:10.0-alpine`, todas com `@sha256:`.
- **`init-env.sh`:** cria `secrets/` com `umask 077` e a pasta em 700. Depois de escrever, os arquivos ficam em 644 (o motivo está na spec D7). São eles: `postgres_password`, `owner_password`, `app_password`, as duas strings de conexão já montadas (`Host=db;…;Username=slimegoals_owner|slimegoals_app;…;GSS Encryption Mode=Disable`), `Server__PreloginSecret`, `Server__InviteCode` (32 hex) e `Email__Smtp__Password` (vazio, para preencher). Cria também o `.env` só com `SLIMEGOALS_DOMAIN=localhost` e as variáveis de SMTP vazias. Não sobrescreve nada que já exista. Ao fim, diz onde está o convite, sem mostrá-lo.
- **`compose.dev.yaml`:** no servidor, `ASPNETCORE_ENVIRONMENT: Development`, `Email__Mode: log`, `AllowedHosts: "*"` e porta `127.0.0.1:8080:8080`. O Caddy ganha `profiles: [proxy]` e não sobe.
- **`upgrade-roles.sh`:** para o volume de desenvolvimento que já existe. Roda `roles.sql` com `docker compose exec db psql -U "${PGSUPERUSER:-slimegoals}"`, passa todas as tabelas, sequências e o `schema_version` de `public` para `slimegoals_owner` e dá ao app os mesmos privilégios. Pode rodar de novo sem estragar nada.
- **`dependabot.yml`:** `nuget` (/), `github-actions` (/), `docker` (/src/SlimeGoals.Server) e `docker-compose` (/), semanais.

- [ ] **Step 1:** escrever os arquivos.
- [ ] **Step 2:** conferir a produção local, numa cópia do repositório com `.env` e `secrets/` novos (`init-env.sh <pasta>`), sem tocar no volume de desenvolvimento do autor. Use `COMPOSE_PROJECT_NAME=slimegoals-prodtest HTTP_PORT=8081 HTTPS_PORT=8443 SLIMEGOALS_DOMAIN=localhost docker compose up -d --build --wait`. Esperado: os três saudáveis.
- [ ] **Step 3:** os passos 2 a 4 da spec §5, com as portas 8081 e 8443. `docker inspect` não mostra senha, segredo, convite nem string de conexão em `Env`. Ao fim, `docker compose -p slimegoals-prodtest down -v`: esse volume foi criado só para o teste, não é o do autor.
- [ ] **Step 4:** o fluxo de desenvolvimento, `docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait`. Cadastrar com o convite pelo `curl` e conferir o código em `docker compose logs server`.
- [ ] **Step 5: Commit** `build(server): Compose de produção com Caddy, segredos em arquivo e contêineres endurecidos`

### Task 10: Documentação

**Files:**
- Modify: `README.md` (a seção "Servidor de sincronização" se divide em "desenvolvimento" e "produção". A produção traz: DNS apontando para a máquina, o `init-env.sh`, o SMTP com o Resend como exemplo (`smtp.resend.com`, 587, usuário `resend`, a chave da API em `secrets/Email__Smtp__Password`, o domínio verificado no Resend), `docker compose up -d --build --wait`, onde ler o convite e o `upgrade-roles.sh`)
- Modify: `CLAUDE.md` (o comando de desenvolvimento com `compose.dev.yaml`, e `secrets/` na regra do `.env`)
- Modify: `specs/Sincronizacao_v1.md` (o token vence em 30 dias; o parágrafo do Docker Compose; as obrigações da 9.6 resolvidas, apontando para a spec nova; o endereço exige `https`; o cadastro tem convite; a tabela de valores atualizada), `specs/Endurecimento_do_servidor_v1.md` (o estado passa a "construído")

- [ ] **Step 1:** escrever.
- [ ] **Step 2:** seguir o README do zero, numa pasta nova, e conferir que cada comando funciona como está escrito.
- [ ] **Step 3: Commit** `docs: servidor em produção, convite e segredos em arquivo`
