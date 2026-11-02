# Fase 4d — Segundo plano no Linux — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fechar a janela deixa o app rodando em segundo plano, com os lembretes e as ações das notificações funcionando. Abrir o app de novo mostra a janela existente em vez de criar outro processo. O usuário pode pausar e retomar os lembretes e sair do app pela janela ou por um ícone de bandeja opcional.

**Architecture:**
- **Core:** ganha uma porta de configurações (`ISettingsRepository`, sobre a tabela `settings`, que já existe) e o `ReminderPause`, que grava a pausa e avisa o `DataChangeNotifier`. O `ReminderService` consulta a pausa: pausado, não dispara e retira as pendentes; ao retomar, o intervalo recomeça do instante da retomada, como já acontece com a meta criada hoje.
- **Platform.Linux:** ganha a instância única. Um arquivo de trava com `flock` garante um processo por banco de dados, e um socket Unix leva o pedido "mostrar janela" do segundo processo ao primeiro.
- **App:** fechar oculta a janela; "Sair" passa por uma porta `IAppLifetime`; a tela Hoje ganha pausar/retomar; a bandeja usa o `TrayIcon` do Avalonia, com um ViewModel próprio.
- **Desktop:** o head decide no `Main` se é a primeira instância e liga tudo.

**Tech Stack:** as mesmas versões da Fase 4 (Avalonia 12.1.4, CommunityToolkit.Mvvm, Dapper, Tmds.DBus.Protocol 0.95.2, xUnit v3, AwesomeAssertions, FakeTimeProvider). Nenhum pacote novo. O ícone da bandeja é gerado com `rsvg-convert` a partir de um ícone do Lucide (ISC).

**Spec:** `specs/Planejamento_v3.md` v1.4: RF07 (bandeja: abrir, pausar lembretes, sair), RF08 (fechar = ocultar, padrão), §9.7–9.8 (segundo plano no GNOME, instância única, notificação reabre o app), §12 linha 4d, §14 (bandeja no GNOME), §15 "Decisões da Fase 4".

**Fora deste plano:** a configuração "ao fechar: ocultar ou encerrar" e a tela de Configurações (Fase 6); iniciar com o sistema (Fase 6); o arquivo `.desktop` instalado e o ícone definitivo do app (Fase 6); suspensão do computador (Fase 6).

## Decisões deste plano (o autor confirma na revisão)

1. **Fechar a janela sempre oculta** nesta fase (RF08: ocultar é o padrão). Para encerrar, use **Sair**: item no rodapé da barra lateral, atalho `Ctrl+Q` e item da bandeja. A opção "encerrar ao fechar" vem com a tela de Configurações (Fase 6).
2. **Instância única por banco de dados:**
   - o nome da trava e do socket inclui os 8 primeiros hexadecimais do SHA-256 do caminho completo do banco: `slimegoals-<hash>.lock` e `slimegoals-<hash>.sock`;
   - assim, um app de teste com `XDG_DATA_HOME` temporário não "acorda" o app com os dados reais;
   - os dois arquivos ficam em `$XDG_RUNTIME_DIR`, ou, sem ele, em `Path.GetTempPath()/slimegoals-<usuário>/`;
   - o segundo processo envia a linha `show`, recebe a resposta `ok` e encerra com código 0;
   - se o socket existir sem ninguém ouvindo (o processo anterior travou), ele é apagado e recriado.
3. **Pausar lembretes:**
   - a pausa fica gravada em `settings` (`reminders.paused` = `"1"`) e sobrevive ao reinício;
   - pausado, nenhum lembrete dispara e as notificações pendentes saem da central como "ignoradas";
   - a tela Hoje mostra a faixa **"Lembretes pausados"** com o botão **Retomar**, e a seção "Próximos lembretes" some;
   - ao retomar, `reminders.resumed_at` guarda o instante (ISO 8601 UTC), e o intervalo de cada meta recomeça dele naquele dia: não chega uma rajada de lembretes atrasados.
4. **Bandeja opcional:**
   - o menu tem **Abrir**, **Pausar lembretes** / **Retomar lembretes** e **Sair**; clicar no ícone também abre a janela;
   - no GNOME, ela só aparece com a extensão AppIndicator; sem ela, nada quebra;
   - o ícone é provisório até a Fase 6: o "target" do Lucide em `#9B7BEA` (token `Primary`), 64 px, em `src/SlimeGoals.App/Assets/tray.png`.
5. **Trazer a janela para a frente a partir da segunda instância** usa o mesmo `MainWindowActivator` da notificação. No Wayland, sem o token de ativação do lançador, o GNOME pode mostrar o aviso "SlimeGoals está pronto" em vez de trazer a janela. Isso fica aceito nesta fase e é conferido à mão.
6. **Desvio da spec §5:** `IReminderScheduler` não ganha `Pause`/`Resume`. A pausa fica no Core (`ReminderPause`) e chega ao agendador pelo `DataChangeNotifier`, como as outras mudanças. Assim, a mesma regra vale para o Android (Fase 7b).

## Global Constraints

- Tudo das Fases 1–4 continua valendo: `net10.0`, `Nullable`, `TreatWarningsAsErrors`, versões centralizadas, Core sem dependências (`ArchitectureTests`), Platform.Linux só referencia o Core, App sem referência a Platform, relógio via `TimeProvider`, instantes em UTC com `SqlText.Instant`, textos em pt-BR, identificadores em inglês, ViewModels sem tipos visuais do Avalonia, bindings compilados, cores e raios só por `DynamicResource`, alvos de toque ≥ 44 px.
- Toda exceção vinda de evento, socket ou bandeja é capturada e registrada em `Trace`: um sinal nunca derruba o app.
- Textos exatos:
  - itens de menu e botões `"Abrir"`, `"Pausar lembretes"`, `"Retomar lembretes"`, `"Sair"`, `"Retomar"`;
  - faixa da tela Hoje `"Lembretes pausados"`;
  - dica da bandeja `"SlimeGoals"`.
- Chaves de configuração: `reminders.paused`, `reminders.resumed_at`.
- Branch `feat/fase-4d-segundo-plano`; Conventional Commits (`core`, `infra`, `linux`, `app`, `desktop`, `specs`); commits terminam com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; integração por PR.
- Comando de testes: `dotnet test --solution SlimeGoals.slnx`. Nunca rodar o app contra o banco real nos testes de fumaça: use `XDG_DATA_HOME` temporário.

## Review Focus

1. **Duas instâncias abertas quase juntas** (duplo clique no atalho): só uma fica; a outra mostra a janela da primeira e sai com código 0. Nenhuma das duas trava esperando a outra. Coberto na Task 3.
2. **App anterior que travou** (trava liberada pelo sistema, socket órfão no disco): o próximo app abre normalmente e recria o socket. Coberto na Task 3.
3. **Retomar depois de uma pausa longa** (pausado às 09:00, retomado às 15:00, cinco metas com intervalo de 1 h): nenhuma notificação na hora da retomada; a primeira de cada meta vem às 16:00. Coberto na Task 2.
4. **Notificação respondida com a janela fechada** (Feito, +1, Iniciar, Abrir): a ação é registrada; Abrir e o caso "outro cronômetro rodando" trazem a janela de volta. Coberto nas Tasks 4 e 6.
5. **Sair com notificações pendentes ou cronômetro rodando:** as pendentes saem da central, como na 4c, e o cronômetro continua gravado no banco; nada se perde. Coberto nas Tasks 4 e 6.

---

## Mapa de arquivos

```
src/SlimeGoals.Core/
  Abstractions/ISettingsRepository.cs     (novo)
  Services/ReminderPause.cs               (novo: PauseState, ReminderPause)
  Services/ReminderService.cs             (alterado: consulta a pausa; anchor da retomada)
  Services/ReminderPlanner.cs             (alterado: CreatedTodayAt → IntervalStartAt)
src/SlimeGoals.Infrastructure/
  Data/SettingsRepository.cs              (novo)
  InfrastructureServiceCollectionExtensions.cs (alterado)
src/SlimeGoals.Platform.Linux/
  XdgPaths.cs                             (alterado: RuntimeDirectory)
  SingleInstance/SingleInstanceGuard.cs   (novo)
src/SlimeGoals.App/
  Hosting/IAppLifetime.cs, Hosting/DesktopAppLifetime.cs (novos)
  ViewModels/ShellViewModel.cs            (alterado: QuitCommand)
  ViewModels/TodayPageViewModel.cs, Views/TodayView.axaml (alterados: pausa)
  ViewModels/TrayMenuViewModel.cs         (novo)
  Views/MainWindow.axaml(.cs), Views/ShellView.axaml (alterados: fechar = ocultar, Sair, Ctrl+Q)
  App.axaml.cs                            (alterado: ShutdownMode, bandeja, lifetime)
  Assets/tray.png, Assets/tray.svg        (novos)
src/SlimeGoals.Desktop/
  Program.cs, CompositionRoot.cs          (alterados: instância única, novos serviços)
tests/SlimeGoals.Testing/InMemorySettingsRepository.cs (novo)
tests/…                                   (testes novos por task)
THIRD-PARTY-NOTICES.md                    (ícone da bandeja: Lucide "target")
specs/Planejamento_v3.md                  (§9.8, §12 linha 4d, §15 "Decisões da Fase 4d")
```

---

## Task 1: Configurações persistidas

**Files:**
- Create: `src/SlimeGoals.Core/Abstractions/ISettingsRepository.cs`, `src/SlimeGoals.Infrastructure/Data/SettingsRepository.cs`, `tests/SlimeGoals.Testing/InMemorySettingsRepository.cs`
- Modify: `src/SlimeGoals.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (registra `ISettingsRepository`), `tests/SlimeGoals.Infrastructure.Tests/InfrastructureServiceCollectionExtensionsTests.cs`
- Test: `tests/SlimeGoals.Infrastructure.Tests/Data/SettingsRepositoryTests.cs`

**Interfaces:**

```csharp
public interface ISettingsRepository
{
    /// Valor da chave, ou null se não existe.
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    /// Grava (insere ou substitui).
    Task SetAsync(string key, string value, CancellationToken ct = default);
    /// Apaga a chave (sem erro se não existe).
    Task RemoveAsync(string key, CancellationToken ct = default);
}
```

Tabela existente (`001`): `settings(key TEXT PRIMARY KEY, value TEXT NOT NULL)`. Sem migração.

- [ ] **Step 1: Testes que falham:** `Missing_key_returns_null`; `Set_then_get_round_trips`; `Set_twice_replaces_value`; `Remove_deletes_and_missing_remove_is_silent`.
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar no padrão do `TimerRepository` (`INSERT OR REPLACE`) e o fake em memória.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(core,infra): repositório de configurações`

---

## Task 2: Pausar e retomar lembretes (Core)

**Files:**
- Create: `src/SlimeGoals.Core/Services/ReminderPause.cs`
- Modify: `src/SlimeGoals.Core/Services/ReminderService.cs`, `src/SlimeGoals.Core/Services/ReminderPlanner.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/ReminderPauseTests.cs`, `tests/SlimeGoals.Core.Tests/Services/ReminderServiceTests.cs`, `tests/SlimeGoals.Core.Tests/Services/ReminderPlannerTests.cs`

**Interfaces:**

```csharp
public sealed record PauseState(bool IsPaused, DateTimeOffset? ResumedAt);

public sealed class ReminderPause(ISettingsRepository settings, TimeProvider time, DataChangeNotifier? changes = null)
{
    public const string PausedKey = "reminders.paused";
    public const string ResumedAtKey = "reminders.resumed_at";
    public Task<PauseState> GetAsync(CancellationToken ct = default);
    /// Grava paused = "1" e notifica. Idempotente: pausar já pausado não notifica de novo.
    public Task PauseAsync(CancellationToken ct = default);
    /// Remove paused, grava resumed_at = agora (UTC, ToString("O", InvariantCulture)) e notifica. Sem pausa → nada.
    public Task ResumeAsync(CancellationToken ct = default);
}

// ReminderService: último parâmetro novo, opcional (as construções existentes continuam compilando)
public sealed class ReminderService(..., TimeProvider time, ReminderPause? pause = null);

// ReminderState: o campo CreatedTodayAt passa a se chamar IntervalStartAt
// ("instante de hoje a partir do qual o intervalo conta: criação da meta ou retomada dos lembretes")
```

Regras:
- `SyncAsync` pausado: cada pendente vira `Ignore` e sai da central; nada dispara; o retorno é `null`. O agendador então arma só a meia-noite.
- `UpcomingTodayAsync` pausado → lista vazia.
- `IntervalStartAt` = o mais recente, entre os que caem hoje (dia local), de `goal.CreatedAt` e `ResumedAt`. O planejador usa esse valor onde hoje usa `CreatedTodayAt`; o comportamento da criação não muda.
- `ResumedAt` ilegível no banco → tratado como `null`, registrado em `Trace`.

- [ ] **Step 1: Testes que falham.** Use relógio e fuso como nos testes de lembrete (quarta, 04/11/2026, São Paulo; meta diária 08:00–20:00, intervalo 60 min, criada em outubro):
  - `Paused_sync_fires_nothing_withdraws_pending_and_returns_null` (com uma pendente das 09:00);
  - `Paused_upcoming_is_empty`;
  - `Resume_restarts_interval_from_resume_time` (pausa às 09:00, retoma às 15:00, sincroniza às 15:00 → nenhuma notificação e próximo = 16:00);
  - `Pause_survives_new_instances` (outro `ReminderPause` com o mesmo repositório lê pausado);
  - `Pause_and_resume_notify_once_each` (pausar duas vezes = 1 aviso);
  - `Unreadable_resumed_at_is_ignored`.

  No planejador, renomear os testes de `CreatedTodayAt` para `IntervalStartAt`, sem mudar as asserções.
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(core): pausar e retomar lembretes`

---

## Task 3: Instância única no Linux

**Files:**
- Create: `src/SlimeGoals.Platform.Linux/SingleInstance/SingleInstanceGuard.cs`
- Modify: `src/SlimeGoals.Platform.Linux/XdgPaths.cs`
- Test: `tests/SlimeGoals.Platform.Linux.Tests/SingleInstanceGuardTests.cs`, `tests/SlimeGoals.Platform.Linux.Tests/XdgPathsTests.cs`

**Interfaces:**

```csharp
// XdgPaths
/// $XDG_RUNTIME_DIR, ou Path.Combine(Path.GetTempPath(), $"slimegoals-{userName}") se vazio.
public static string RuntimeDirectory(Func<string, string?> getEnv, string userName, string tempPath);

public sealed class SingleInstanceGuard : IDisposable
{
    /// Primeira instância: devolve o guarda (trava presa, socket ouvindo). Já existe outra: null.
    /// <paramref name="key"/> = caminho completo do banco (vira o hash do nome, decisão 2).
    public static SingleInstanceGuard? TryAcquire(string runtimeDirectory, string key);
    /// Pede à instância existente para mostrar a janela; true se ela respondeu "ok" em até 2 s.
    public static bool SignalExisting(string runtimeDirectory, string key);
    /// Disparado (em thread do pool) a cada pedido "show" recebido.
    public event EventHandler? ShowRequested;
    /// Para de ouvir, apaga o socket e libera a trava.
    public void Dispose();
}
```

Regras:
- A trava é um `FileStream` com `FileShare.None` no `.lock`, que no Linux usa `flock`. Ela fica aberta enquanto o guarda viver.
- O socket é um `Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)` com `UnixDomainSocketEndPoint`. Se o `.sock` já existir com a trava livre, é órfão: apagar antes do `Bind`.
- Protocolo: o cliente envia `show\n` e lê `ok\n`; qualquer outra linha é ignorada sem resposta. Cada conexão é tratada e fechada; exceções vão para `Trace`.
- Nome: `slimegoals-{hash}.lock` / `.sock`, com `hash` = `Convert.ToHexStringLower(SHA256(UTF-8 de key))[..8]`. A pasta é criada se faltar, com permissão `0700` em `UnixFileMode`.

- [ ] **Step 1: Testes que falham** (pasta temporária por teste; chave = um caminho fictício):
  - `First_acquire_succeeds_second_returns_null`;
  - `Signal_reaches_first_instance_and_returns_true` (`ShowRequested` disparado uma vez);
  - `Signal_without_instance_returns_false_quickly` (menos de 2 s);
  - `Dispose_releases_lock_and_next_acquire_succeeds`;
  - `Orphan_socket_file_is_replaced` (cria um `.sock` vazio sem trava → o `TryAcquire` funciona e responde a sinais);
  - `Different_keys_do_not_conflict`;
  - `Unknown_line_gets_no_reply`.

  Em `XdgPathsTests`: `$XDG_RUNTIME_DIR` presente, vazio e ausente.
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Suíte inteira passa.
- [ ] **Step 5: Commit** — `feat(linux): instância única por banco com trava e socket Unix`

---

## Task 4: Fechar oculta, Sair e pausa na tela Hoje (App)

**Files:**
- Create: `src/SlimeGoals.App/Hosting/IAppLifetime.cs`, `src/SlimeGoals.App/Hosting/DesktopAppLifetime.cs`
- Modify: `src/SlimeGoals.App/ViewModels/ShellViewModel.cs`, `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`, `src/SlimeGoals.App/Views/TodayView.axaml`, `src/SlimeGoals.App/Views/ShellView.axaml`, `src/SlimeGoals.App/Views/MainWindow.axaml`, `src/SlimeGoals.App/Views/MainWindow.axaml.cs`, `src/SlimeGoals.App/App.axaml.cs`
- Test: `tests/SlimeGoals.App.Tests/ViewModels/ShellViewModelTests.cs`, `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**

```csharp
public interface IAppLifetime { void Quit(); }
/// Implementação desktop: Dispatcher.UIThread.Post(() => desktop.Shutdown()); recebe o lifetime pela App.
public sealed class DesktopAppLifetime : IAppLifetime { public IClassicDesktopStyleApplicationLifetime? Desktop { get; set; } }

// ShellViewModel: novo parâmetro opcional no fim
public ShellViewModel(IEnumerable<PageViewModel> pages, IAppLifetime? lifetime = null);
public IRelayCommand QuitCommand { get; }          // CanExecute = lifetime != null

// TodayPageViewModel: novo parâmetro no fim (ReminderPause? pause = null)
public bool IsPaused { get; }                      // observável
public IAsyncRelayCommand TogglePauseCommand { get; }
public string PauseButtonText { get; }             // "Pausar lembretes" | "Retomar lembretes"
```

Regras:
- `MainWindow`: no `Closing`, se o fechamento não veio do `Shutdown`, cancelar e chamar `Hide()`. A flag é ligada pela `App` no `ShutdownRequested`. Atalho `Ctrl+Q` → `QuitCommand`.
- `ShellView`: item "Sair" no rodapé da barra lateral, estilo `ghost`, ligado a `QuitCommand`.
- `App`:
  - `desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown`;
  - entrega o `desktop` ao `DesktopAppLifetime`, quando registrado, como já faz com o `MainWindowActivator`.
- `TodayPageViewModel`:
  - `LoadAsync` lê `pause.GetAsync()` e preenche `IsPaused`;
  - com pausa, `Upcoming` fica vazio;
  - `TogglePauseCommand` chama `PauseAsync`/`ResumeAsync`, e a recarga vem pelo `Changed`.
- `TodayView`:
  - com `IsPaused`, mostra no topo da lista uma faixa no estilo da faixa de cronômetro: texto "Lembretes pausados" e botão "Retomar" (`TogglePauseCommand`);
  - sem pausa, o cabeçalho de "Próximos lembretes" ganha o botão `ghost` "Pausar lembretes".

- [ ] **Step 1: Testes que falham:**
  - `Quit_command_calls_lifetime` e `Quit_without_lifetime_cannot_execute`;
  - `Today_shows_paused_state_and_hides_upcoming`;
  - `Toggle_pause_pauses_then_resumes` (`IsPaused` e `PauseButtonText` mudam após a recarga).
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Suíte inteira passa; build sem avisos.
- [ ] **Step 5: Commit** — `feat(app): fechar oculta a janela, Sair e pausa dos lembretes na tela Hoje`

---

## Task 5: Bandeja e ligação no head Desktop

**Files:**
- Create: `src/SlimeGoals.App/ViewModels/TrayMenuViewModel.cs`, `src/SlimeGoals.App/Assets/tray.svg`, `src/SlimeGoals.App/Assets/tray.png`
- Modify: `src/SlimeGoals.App/App.axaml.cs` (cria o `TrayIcon` se houver `TrayMenuViewModel` no container), `src/SlimeGoals.App/SlimeGoals.App.csproj` (`AvaloniaResource` do PNG, se preciso), `src/SlimeGoals.Desktop/Program.cs`, `src/SlimeGoals.Desktop/CompositionRoot.cs`, `THIRD-PARTY-NOTICES.md`
- Test: `tests/SlimeGoals.App.Tests/ViewModels/TrayMenuViewModelTests.cs`

**Interfaces:**

```csharp
public sealed partial class TrayMenuViewModel(IWindowActivator window, ReminderPause pause, IAppLifetime lifetime,
    DataChangeNotifier changes) : ObservableObject
{
    public IRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand TogglePauseCommand { get; }
    public IRelayCommand QuitCommand { get; }
    public string PauseText { get; }        // "Pausar lembretes" | "Retomar lembretes"; atualizado no Changed
    public Task RefreshAsync();             // lê a pausa (chamado no início e a cada Changed)
}
```

Regras:
- `App`, quando o container tem `TrayMenuViewModel`:
  - cria em código um `TrayIcon` com `Icon` carregado de `avares://SlimeGoals.App/Assets/tray.png`, `ToolTipText` "SlimeGoals" e `Clicked` → `OpenCommand`;
  - o `NativeMenu` tem "Abrir", o item de pausa (cabeçalho ligado a `PauseText`) e "Sair";
  - registra o ícone com `TrayIcon.SetIcons(this, …)`;
  - erros ao criar a bandeja vão para `Trace`, e o app segue sem ela.
- `tray.svg`: o "target" do Lucide (três círculos, `stroke="#9B7BEA"`, `stroke-width="2"`, `fill="none"`, `viewBox="0 0 24 24"`); `tray.png` = `rsvg-convert -w 64 -h 64`.
- `Program.Main`, depois de calcular o `databasePath` e antes do container:
  - `var runtime = XdgPaths.RuntimeDirectory(Environment.GetEnvironmentVariable, Environment.UserName, Path.GetTempPath());`
  - `using var guard = SingleInstanceGuard.TryAcquire(runtime, Path.GetFullPath(databasePath));`
  - `guard is null` → `SingleInstanceGuard.SignalExisting(...)` e `return` com código 0 (sem abrir o banco);
  - senão, `guard.ShowRequested += (_, _) => activator.Activate()`, com o `MainWindowActivator` resolvido do container.
- `CompositionRoot`: singletons `ISettingsRepository` (já vem da Infrastructure), `ReminderPause`, `DesktopAppLifetime` (também como `IAppLifetime`) e `TrayMenuViewModel`. `ValidateOnBuild` continua ligado.
- `THIRD-PARTY-NOTICES.md`: o ícone "target" do Lucide também é usado na bandeja.

- [ ] **Step 1: Testes que falham:**
  - `Open_activates_window`;
  - `Toggle_pause_switches_text_after_change` (`PauseText` passa a "Retomar lembretes");
  - `Quit_calls_lifetime`.
- [ ] **Step 2:** Ver falhar.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Suíte inteira passa; build sem avisos. Teste de fumaça com `XDG_DATA_HOME` temporário:
  - abrir o app em segundo plano e, com ele aberto, rodar `dotnet run` de novo com o mesmo `XDG_DATA_HOME`;
  - o segundo processo deve sair com código 0 em menos de 5 s e o primeiro continuar vivo;
  - encerrar o primeiro com `kill`.
- [ ] **Step 5: Commit** — `feat(app,desktop): bandeja e instância única ligadas ao app`

---

## Task 6: Verificação manual no GNOME e spec

**Files:**
- Modify: `specs/Planejamento_v3.md` (§9.8: fechar oculta e "Sair"; §12 linha 4d: critério cumprido; §15: bloco "Decisões da Fase 4d" com as decisões 1–6)

- [ ] **Step 1:** Com `XDG_DATA_HOME` temporário:
  - fechar a janela: o processo continua, e os lembretes chegam;
  - Feito, +1, Iniciar e Adiar funcionam com a janela fechada;
  - Abrir traz a janela de volta.
- [ ] **Step 2:** Instância única e Sair:
  - rodar o app de novo com o mesmo `XDG_DATA_HOME`: a janela existente aparece e não surge outro processo (`pgrep -c slimegoals` = 1);
  - um app com outro `XDG_DATA_HOME` abre separado;
  - Sair (barra lateral e `Ctrl+Q`) encerra o app, e as pendentes saem da central.
- [ ] **Step 3:** Pausa e bandeja:
  - pausar na tela Hoje: nenhum lembrete chega, e a faixa aparece;
  - reiniciar o app: continua pausado;
  - retomar: nenhuma rajada, e o próximo lembrete vem depois de um intervalo;
  - com a extensão AppIndicator, o menu da bandeja (Abrir, Pausar/Retomar, Sair) funciona; sem ela, nada quebra.
- [ ] **Step 4:** Atualizar a spec. Suíte inteira passa.
- [ ] **Step 5: Commit** — `docs(specs): decisões da Fase 4d (segundo plano no Linux)`
