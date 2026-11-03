# Fase 6 — Linux: acabamento — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deixar o SlimeGoals instalável e estável no GNOME (CachyOS):
- tela de Configurações com iniciar com o sistema, duração do "adiar" e o que fazer ao fechar a janela;
- lembretes recalculados ao acordar da suspensão;
- ícone próprio do app, também na bandeja;
- arquivo `.desktop`, para o nome e o ícone aparecerem certos no GNOME;
- pacote *self-contained* com script de instalação e os textos de licença das dependências.

**Architecture:**
- **Core:** `AppSettings` lê e grava as duas configurações novas na tabela `settings`, que já existe. O `ReminderService` passa a ler dela o tempo do adiar. Ganha também a porta `IAutostart`.
- **Platform.Linux:** implementa `IAutostart` com um arquivo `.desktop` em `$XDG_CONFIG_HOME/autostart`. O `SleepResumeWatcher` ouve o sinal `PrepareForSleep` do systemd-logind no barramento do sistema e avisa o `DataChangeNotifier` ao acordar.
- **App:**
  - `WindowCloseBehavior` guarda a escolha "ao fechar" e a aplica sem reiniciar;
  - `SettingsPageViewModel` e `SettingsView` formam a tela;
  - `--background` abre o app sem mostrar a janela (uso do autostart).
- **Desktop:** composição, `WmClass = "slimegoals"` e o adaptador do watcher.
- **`packaging/linux/`:** ícones, `.desktop`, scripts de build, instalação e desinstalação, e licenças.

**Tech Stack:**
- as mesmas versões da Fase 5 (Avalonia 12.1.4, CommunityToolkit.Mvvm, Dapper, Tmds.DBus.Protocol 0.95.2, xUnit v3, AwesomeAssertions, FakeTimeProvider);
- nenhum pacote novo;
- ícones gerados com `rsvg-convert` a partir de um SVG;
- pacote com `dotnet publish -r linux-x64 --self-contained` e `tar`.

**Spec:** `specs/Planejamento_v3.md` v1.4:
- RF08 (configurações: iniciar com o sistema, duração do adiar, ao fechar a janela);
- §4 (empacotamento);
- §9 itens 2, 8 e 10 (adiar configurável, ao fechar, suspensão via systemd-logind);
- §12 linha 6;
- §14 (bandeja no GNOME);
- `specs/spikes/gnome-notificacoes.md` item 4 (nome e ícone na Fase 6).

**Fora deste plano:**
- fallback de notificação em janela própria: o D-Bus funciona no GNOME, e a spec diz "se preciso";
- AppImage e PKGBUILD (ver decisão 6);
- telas e configurações do Android (Fase 7);
- tema claro, exportação e sincronização.

## Decisões deste plano (o autor confirma na revisão)

1. **Configurações gravadas em `settings`:**
   - `reminders.snooze_minutes`: opções 5, 10, 15, 30 e 60 min; padrão 10. Valor ausente ou inválido vale 10.
   - `window.close_action`: `"hide"` (padrão) ou `"quit"`.
   - Mudar qualquer configuração vale na hora, sem reiniciar.
   - O adiar novo vale na hora, inclusive para um adiar já respondido e ainda não disparado: mudar de 10 para 30 min às 10:05 leva o disparo de 10:10 para 10:30. Guardar a duração de cada adiar exigiria migrar o `reminder_log` só para esse caso raro (decisão revista na execução).
2. **Iniciar com o sistema:**
   - a fonte da verdade é o arquivo `~/.config/autostart/slimegoals.desktop` (ou `$XDG_CONFIG_HOME/autostart`), não a tabela `settings`;
   - ligar grava o arquivo com o caminho do executável atual (`Environment.ProcessPath`) e `--background`; desligar apaga o arquivo;
   - se o arquivo foi criado por uma instalação antiga, ligar de novo reescreve o caminho.
3. **`--background`:** o app abre sem mostrar a janela e segue com lembretes e bandeja. Abrir pelo atalho, pela notificação ou pela bandeja mostra a janela, pela instância única. Sem o argumento, nada muda.
4. **Ao fechar = "Encerrar o app":** fechar a janela faz o mesmo que "Sair". As notificações pendentes saem da central, e os lembretes param até abrir de novo. A tela avisa isso.
5. **Suspensão:**
   - ao acordar, o app avisa o `DataChangeNotifier`;
   - o agendador recalcula, e a tela Hoje recarrega, mesmo que tenha passado da meia-noite;
   - os lembretes vencidos durante o sono seguem a regra de disparo atrasado da spec §9.2: disparam uma vez cada, na hora;
   - sem barramento do sistema ou sem logind, o app segue sem isso e registra em `Trace`.
6. **Pacote:**
   - um `.tar.gz` *self-contained* para linux-x64, com `install.sh` e `uninstall.sh` que instalam no usuário (`~/.local`), sem root;
   - a versão vai em `Directory.Build.props` e começa em `0.6.0`;
   - o AppImage fica fora: o `appimagetool` não está instalado e não acrescenta nada ao uso pessoal no CachyOS;
   - o PKGBUILD fica como melhoria futura (a spec diz "opcional").
7. **Ícone do app:**
   - é o slime aprovado no design `specs/design/Slime — mascote do SlimeGoals.html`, na pose parada (corpo, brilhos, bochechas, olhos abertos e boca "idle"), sem sombra nem confete;
   - substitui também o ícone provisório da bandeja;
   - o "target" do Lucide continua como ícone de meta.
8. **Nome e ícone no GNOME:**
   - o `.desktop` instalado tem `StartupWMClass=slimegoals`;
   - o app passa a definir `WM_CLASS` como `slimegoals` (`X11PlatformOptions.WmClass`; o app roda via XWayland);
   - o cabeçalho das notificações e o painel são conferidos à mão depois da instalação.

## Global Constraints

- Tudo das Fases 1–5 continua valendo:
  - `net10.0`, `Nullable`, `TreatWarningsAsErrors`, versões centralizadas;
  - Core sem dependências (`ArchitectureTests`); Platform.Linux só referencia o Core; App sem referência a Platform;
  - relógio via `TimeProvider`;
  - textos em pt-BR e identificadores em inglês;
  - ViewModels sem tipos visuais do Avalonia, bindings compilados, cores e raios só por `DynamicResource`, alvos de toque ≥ 44 px.
- Toda exceção vinda de evento, D-Bus ou arquivo é capturada e registrada em `Trace`: uma falha nunca derruba o app.
- Textos exatos da tela de Configurações:
  - **Seções:** `"Geral"`, `"Lembretes"`, `"Janela"`.
  - **Iniciar com o sistema:**
    - interruptor `"Iniciar com o sistema"`, com a dica `"Abre em segundo plano ao entrar na sessão."`;
    - indisponível: `"Indisponível neste sistema."`.
  - **Adiar:**
    - rótulo `"Adiar lembretes por"`;
    - opções `"5 min"`, `"10 min"`, `"15 min"`, `"30 min"`, `"1 h"`.
  - **Ao fechar a janela:**
    - rótulo `"Ao fechar a janela"`;
    - segmentos `"Manter em segundo plano"` / `"Encerrar o app"`;
    - dicas `"Os lembretes continuam com a janela fechada."` / `"Com o app fechado, os lembretes param."`.
  - **Erros:**
    - `"Não foi possível salvar a configuração."`;
    - `"Não foi possível alterar o início com o sistema."`.
- Chaves: `reminders.snooze_minutes`, `window.close_action`.
- Argumento: `--background`.
- `WM_CLASS` e nome de arquivo: `slimegoals`.
- Caminhos da instalação:
  - `~/.local/opt/slimegoals/` (aplicativo);
  - `~/.local/bin/slimegoals` (link);
  - `~/.local/share/applications/slimegoals.desktop`;
  - `~/.local/share/icons/hicolor/<N>x<N>/apps/slimegoals.png` (N = 16, 32, 48, 64, 128, 256, 512) e `~/.local/share/icons/hicolor/scalable/apps/slimegoals.svg`.
- Branch `feat/fase-6-acabamento-linux`.
  - Commits: Conventional Commits com escopo `core`, `infra`, `linux`, `app`, `desktop`, `build` ou `specs`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  - Integração por PR.
- Comando de testes: `dotnet test --solution SlimeGoals.slnx`.
- Teste de fumaça só com `XDG_DATA_HOME`, `XDG_CONFIG_HOME` e `HOME` temporários. Nunca contra o banco ou o `~/.config` reais.

## Review Focus

1. **Caminho do executável com espaço ou aspas** (ex.: `/home/x/Meus Apps/slimegoals`): a linha `Exec=` do autostart fica entre aspas e com escape (`\"`, `` \` ``, `\$`, `\\`), e o GNOME a executa. Coberto na Task 2.
2. **Valor estranho gravado em `reminders.snooze_minutes`** (`"abc"`, `"7"`, `"-5"`): vale 10, sem exceção. Coberto na Task 1.
3. **Máquina sem logind** (container, outro init) **ou que acorda depois da meia-noite:**
   - sem logind, o app abre normalmente;
   - ao acordar, a tela Hoje mostra o dia novo e os lembretes são recalculados.

   Coberto na Task 3.
4. **Trocar "Ao fechar a janela" e fechar logo em seguida:** a escolha nova já vale, sem reiniciar. Coberto na Task 4.
5. **Instalar duas vezes** (atualização) **e desinstalar:**
   - a segunda instalação substitui o app sem perder dados;
   - a desinstalação não apaga `~/.local/share/SlimeGoals` nem o autostart do usuário, e avisa sobre o autostart.

   Coberto na Task 7.

---

## Mapa de arquivos

```
Directory.Build.props                                   (alterado: <Version>0.6.0</Version>)
src/SlimeGoals.Core/
  Abstractions/IAutostart.cs                            (novo)
  Services/AppSettings.cs                               (novo: CloseAction, AppSettings)
  Services/ReminderService.cs                           (alterado: adiar vindo do AppSettings)
src/SlimeGoals.Platform.Linux/
  XdgPaths.cs                                           (alterado: AutostartDirectory)
  Autostart/XdgAutostart.cs                             (novo)
  Power/ISleepSignalSource.cs, Power/LogindSleepSignalSource.cs, Power/SleepResumeWatcher.cs (novos)
src/SlimeGoals.App/
  Hosting/AppStartOptions.cs, Hosting/WindowCloseBehavior.cs (novos)
  App.axaml.cs, Views/MainWindow.axaml(.cs)             (alterados: --background, ao fechar, ícone)
  ViewModels/SettingsPageViewModel.cs, Views/SettingsView.axaml (reescritos)
  Assets/icon.svg, Assets/icon-256.png, Assets/tray.svg, Assets/tray.png (novos/substituídos)
src/SlimeGoals.Desktop/
  Program.cs, CompositionRoot.cs, SleepResumeHost.cs    (alterados/novo)
packaging/linux/
  slimegoals.desktop.in, icons/…, build.sh, install.sh, uninstall.sh, licenses/… (novos)
THIRD-PARTY-NOTICES.md, README.md, specs/Planejamento_v3.md (alterados)
```

---

### Task 1: `AppSettings` e adiar configurável

**Files:**
- Create: `src/SlimeGoals.Core/Services/AppSettings.cs`
- Modify: `src/SlimeGoals.Core/Services/ReminderService.cs`, mais os testes que usam `ReminderService.SnoozeMinutes`
- Test: `tests/SlimeGoals.Core.Tests/Services/AppSettingsTests.cs`, `tests/SlimeGoals.Core.Tests/Services/ReminderServiceTests.cs`

**Interfaces:**
- Consumes: `ISettingsRepository`, `DataChangeNotifier`, `ReminderPlanner.Next(goal, state, snoozeMinutes, now, zone)` (já recebe o adiar por parâmetro).
- Produces:

```csharp
namespace SlimeGoals.Core.Services;

public enum CloseAction { Hide, Quit }

/// Configurações do app (spec RF08) na tabela settings; mudar avisa o DataChangeNotifier.
public sealed class AppSettings(ISettingsRepository settings, DataChangeNotifier? changes = null)
{
    public const string SnoozeMinutesKey = "reminders.snooze_minutes";
    public const string CloseActionKey = "window.close_action";
    public const int DefaultSnoozeMinutes = 10;
    public static IReadOnlyList<int> SnoozeOptions { get; } = [5, 10, 15, 30, 60];

    public Task<int> GetSnoozeMinutesAsync(CancellationToken ct = default);   // ausente/inválido → 10 (+ Trace se inválido)
    public Task SetSnoozeMinutesAsync(int minutes, CancellationToken ct = default); // fora de SnoozeOptions → ArgumentOutOfRangeException
    public Task<CloseAction> GetCloseActionAsync(CancellationToken ct = default);   // "quit" → Quit; resto → Hide
    public Task SetCloseActionAsync(CloseAction action, CancellationToken ct = default); // grava "hide"/"quit"
}
```

No `ReminderService`:
- novo parâmetro opcional no fim do construtor: `AppSettings? settings = null`;
- o adiar usado no `Next` vem de `settings?.GetSnoozeMinutesAsync()` (lido uma vez por `SyncAsync` ou `UpcomingTodayAsync`, ou `AppSettings.DefaultSnoozeMinutes` sem settings);
- a constante `ReminderService.SnoozeMinutes` sai, e quem a usava passa a usar `AppSettings.DefaultSnoozeMinutes`.

- [ ] **Step 1: Write the failing tests**
  - **`AppSettingsTests`** (com `InMemorySettingsRepository`):
    - `Snooze_defaults_to_10_and_round_trips`: sem chave, devolve 10; `Set(30)` grava `"30"`, `Get` devolve 30 e o notificador dispara uma vez.
    - `Snooze_invalid_stored_value_falls_back_to_10` (`[Theory]` com `"abc"`, `"7"`, `"-5"`, `""`): devolve 10.
    - `Snooze_rejects_values_outside_options`: `Set(7)` lança `ArgumentOutOfRangeException` e não grava.
    - `Close_action_defaults_to_hide_and_round_trips`: sem chave dá `Hide`; `Set(Quit)` grava `"quit"`; `"xyz"` gravado dá `Hide`.
  - **`ReminderServiceTests`:**
    - `Snooze_uses_configured_minutes`: com `reminders.snooze_minutes = "30"`, um disparo respondido com Adiar às 10:00 locais faz o próximo cair às 10:30. Siga o mesmo cenário do teste de adiar já existente, que esperava 10:10.
- [ ] **Step 2: Run to verify they fail** — falha de compilação.
- [ ] **Step 3: Implement** `AppSettings` e a leitura no `ReminderService`; troque os usos da constante.
- [ ] **Step 4: Run tests** — Expected: PASS (todos).
- [ ] **Step 5: Commit** — `feat(core): configurações do app e adiar configurável`

---

### Task 2: Iniciar com o sistema (autostart XDG)

**Files:**
- Create: `src/SlimeGoals.Core/Abstractions/IAutostart.cs`, `src/SlimeGoals.Platform.Linux/Autostart/XdgAutostart.cs`
- Modify: `src/SlimeGoals.Platform.Linux/XdgPaths.cs`
- Test: `tests/SlimeGoals.Platform.Linux.Tests/XdgAutostartTests.cs`, `tests/SlimeGoals.Platform.Linux.Tests/XdgPathsTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace SlimeGoals.Core.Abstractions;
/// Iniciar o app com a sessão do usuário (spec RF08). Implementado por plataforma.
public interface IAutostart
{
    bool IsEnabled();
    void Enable();   // grava/reescreve a entrada com o executável atual
    void Disable();  // remove a entrada (sem erro se não existe)
}

namespace SlimeGoals.Platform.Linux;
public static string AutostartDirectory(Func<string, string?> getEnv, string home); // $XDG_CONFIG_HOME/autostart ou ~/.config/autostart

namespace SlimeGoals.Platform.Linux.Autostart;
public sealed class XdgAutostart(string autostartDirectory, string executablePath) : IAutostart
{
    public const string FileName = "slimegoals.desktop";
    public static string QuoteExec(string path); // aspas duplas + escape de " ` $ \
}
```

O conteúdo exato do arquivo, com `\n` e terminando em `\n`:

```
[Desktop Entry]
Type=Application
Name=SlimeGoals
Comment=Metas e lembretes
Exec=<QuoteExec(executablePath)> --background
Icon=slimegoals
Terminal=false
X-GNOME-Autostart-enabled=true
```

Regras:
- `IsEnabled` = o arquivo existe e não contém a linha `Hidden=true` nem `X-GNOME-Autostart-enabled=false`.
- `Enable` cria a pasta se faltar.

- [ ] **Step 1: Write the failing tests** (pasta temporária por teste):
  - `Enable_writes_entry_with_quoted_exec_and_background_flag`: com o caminho `/opt/Meus Apps/slimegoals`, o arquivo fica exatamente como acima, com `Exec="/opt/Meus Apps/slimegoals" --background`. `IsEnabled()` é verdadeiro.
  - `QuoteExec_escapes_special_characters`: `a"b$c\`d\e` → `"a\"b\$c\`d\\e"`.
  - `Disable_removes_entry_and_is_idempotent`: depois de `Disable`, o arquivo não existe e `IsEnabled()` é falso; um segundo `Disable` não lança exceção.
  - `Entry_hidden_by_user_counts_as_disabled`: um arquivo com `Hidden=true` ou `X-GNOME-Autostart-enabled=false` leva a `IsEnabled()` falso; `Enable` o reescreve.
  - `XdgPathsTests.AutostartDirectory_uses_xdg_config_home_or_default`: `/x/cfg` → `/x/cfg/autostart`; vazio ou nulo → `<home>/.config/autostart`.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(linux): iniciar com o sistema pelo autostart XDG`

---

### Task 3: Recalcular ao acordar da suspensão (systemd-logind)

**Files:**
- Create: `src/SlimeGoals.Platform.Linux/Power/ISleepSignalSource.cs`, `Power/LogindSleepSignalSource.cs`, `Power/SleepResumeWatcher.cs`, `src/SlimeGoals.Desktop/SleepResumeHost.cs`
- Modify: `src/SlimeGoals.Desktop/CompositionRoot.cs`
- Test: `tests/SlimeGoals.Platform.Linux.Tests/SleepResumeWatcherTests.cs`

**Interfaces:**
- Consumes: `DataChangeNotifier` (o agendador já reagenda a cada aviso e a tela Hoje recarrega); `IAppHostedService` (App/Hosting).
- Produces:

```csharp
namespace SlimeGoals.Platform.Linux.Power;

/// Sinal PrepareForSleep do logind: true ao entrar em suspensão, false ao acordar.
public interface ISleepSignalSource : IAsyncDisposable
{
    Task StartAsync(Action<bool> onPrepareForSleep, CancellationToken ct = default);
}

/// Barramento do sistema (DBusAddress.System), org.freedesktop.login1, /org/freedesktop/login1,
/// interface org.freedesktop.login1.Manager, sinal PrepareForSleep(b). Mesmo padrão de WatchSignalAsync do TmdsNotificationBus.
public sealed class LogindSleepSignalSource : ISleepSignalSource;

/// Ao acordar (false), chama changes.NotifyChanged(). Falha ao iniciar a fonte: Trace e segue sem.
public sealed class SleepResumeWatcher(ISleepSignalSource source, DataChangeNotifier changes) : IAsyncDisposable
{
    public Task StartAsync(CancellationToken ct = default);
}
```

`SleepResumeHost`, no Desktop, é um `IAppHostedService` que só delega ao `SleepResumeWatcher` (início e parada). Ele é registrado no `CompositionRoot` junto com o `ReminderHost`.

- [ ] **Step 1: Write the failing tests** (`SleepResumeWatcherTests`, com uma fonte falsa que guarda o callback):
  - `Waking_up_notifies_data_change`: `onPrepareForSleep(false)` dispara `Changed` uma vez.
  - `Going_to_sleep_does_not_notify`: `onPrepareForSleep(true)` não dispara.
  - `Source_failure_on_start_is_swallowed`: a fonte lança exceção no `StartAsync`; o `StartAsync` do watcher completa sem exceção.
  - `Disposing_stops_notifications`: depois de `DisposeAsync`, o callback não dispara mais `Changed`.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** watcher, fonte logind e host; registre no `CompositionRoot`.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Smoke manual** (no relatório):
  - com o app aberto num banco temporário, rode `systemctl suspend` e acorde a máquina;
  - confirme que não há exceção na saída e que a tela Hoje recarregou;
  - se não puder suspender, escreva isso no relatório. O autor confere na verificação manual.
- [ ] **Step 6: Commit** — `feat(linux): recalcular lembretes ao acordar da suspensão`

---

### Task 4: Ao fechar a janela e `--background`

**Files:**
- Create: `src/SlimeGoals.App/Hosting/AppStartOptions.cs`, `src/SlimeGoals.App/Hosting/WindowCloseBehavior.cs`
- Modify: `src/SlimeGoals.App/App.axaml.cs`, `src/SlimeGoals.App/Views/MainWindow.axaml.cs`, `src/SlimeGoals.Desktop/Program.cs`, `src/SlimeGoals.Desktop/CompositionRoot.cs`
- Test: `tests/SlimeGoals.App.Tests/Hosting/WindowCloseBehaviorTests.cs`, `tests/SlimeGoals.App.Tests/Hosting/AppStartOptionsTests.cs`

**Interfaces:**
- Consumes: `AppSettings` (Task 1), `IAppLifetime.Quit()`, `DataChangeNotifier`.
- Produces:

```csharp
namespace SlimeGoals.App.Hosting;

/// Opções da linha de comando; Background = "--background" presente (abre sem mostrar a janela).
public sealed record AppStartOptions(bool Background)
{
    public static AppStartOptions Parse(IReadOnlyList<string> args);
}

/// Escolha "ao fechar a janela" em cache, para o OnClosing (síncrono) decidir na hora.
/// LoadAsync lê do AppSettings; recarrega a cada DataChangeNotifier.Changed. Falha: Trace e mantém Hide.
public sealed class WindowCloseBehavior(AppSettings settings, DataChangeNotifier changes) : IAppHostedService
{
    public CloseAction Current { get; }
    // StartAsync = carrega + assina; StopAsync = desassina
}
```

Comportamento:
- `MainWindow` recebe, pela `App`, um `Func<bool> QuitOnClose` e um `Action Quit`. No `OnClosing` que não é encerramento:
  - se `QuitOnClose()` for verdadeiro: `e.Cancel = true` e `Quit()` (o mesmo caminho do "Sair");
  - se não: oculta, como hoje.
- A `App`, com `AppStartOptions.Background`:
  - cria a `MainWindow` e a entrega ao `MainWindowActivator`, mas **não** a atribui a `desktop.MainWindow` antes de iniciar, para ela não abrir sozinha;
  - o `MainWindowActivator` a mostra no primeiro pedido.
  - Sem `Background`, nada muda.
- O `Program` faz `AppStartOptions.Parse(args)` e o registra no DI.
  - A segunda instância com `--background` só pede para mostrar a janela se **não** tiver o argumento. Com o argumento, sai sem sinalizar: um autostart duplicado não abre a janela.

- [ ] **Step 1: Write the failing tests:**
  - `AppStartOptions_parses_background_flag`: `["--background"]` → verdadeiro; `[]` e `["--outro"]` → falso.
  - `WindowCloseBehavior_loads_setting_and_follows_changes`:
    - com `window.close_action = "quit"`, depois de `StartAsync`, `Current == Quit`;
    - `AppSettings.SetCloseActionAsync(Hide)`, que avisa o notificador, faz `Current == Hide` sem chamar `StartAsync` de novo.
  - `WindowCloseBehavior_defaults_to_hide_when_settings_fail`: repositório que lança exceção leva a `Current == Hide` e `StartAsync` sem exceção.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** os dois tipos, `MainWindow`, `App`, `Program` e DI (`WindowCloseBehavior` como singleton e também como `IAppHostedService`).
- [ ] **Step 4: Run tests + build** — Expected: PASS, 0 avisos.
- [ ] **Step 5: Smoke run** (temporários de `XDG_DATA_HOME`, `XDG_CONFIG_HOME` e `XDG_RUNTIME_DIR`):
  - `dotnet run --project src/SlimeGoals.Desktop -- --background` não mostra janela;
  - uma segunda execução sem o argumento mostra a janela;
  - feche com `pkill -x slimegoals` **só se** `pgrep -a -x slimegoals` mostrar apenas o processo do teste.
- [ ] **Step 6: Commit** — `feat(app): abrir em segundo plano e encerrar ao fechar a janela`

---

### Task 5: Tela de Configurações

**Files:**
- Modify: `src/SlimeGoals.App/ViewModels/SettingsPageViewModel.cs`, `src/SlimeGoals.App/Views/SettingsView.axaml`, `src/SlimeGoals.Desktop/CompositionRoot.cs`, `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs`
- Test: `tests/SlimeGoals.App.Tests/ViewModels/SettingsPageViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings` (Task 1), `IAutostart?` (Task 2; nulo = indisponível), `WindowCloseBehavior` (Task 4, só leitura indireta via settings).
- Produces:

```csharp
public sealed record SnoozeOptionViewModel(int Minutes, string Label); // "5 min" … "1 h"

public sealed partial class SettingsPageViewModel(AppSettings settings, IAutostart? autostart = null) : PageViewModel
{
    // Title => "Configurações"
    public bool CanAutostart { get; }                 // autostart != null
    public string? AutostartUnavailableText { get; }  // "Indisponível neste sistema." quando !CanAutostart
    [ObservableProperty] bool StartWithSystem;        // muda → Enable/Disable; erro → volta o valor + ErrorMessage
    public IReadOnlyList<SnoozeOptionViewModel> SnoozeOptions { get; }
    [ObservableProperty] SnoozeOptionViewModel? SelectedSnooze; // muda → SetSnoozeMinutesAsync
    [ObservableProperty] bool CloseHides;              // segmentos two-way, como IsWeek/IsMonth nos Relatórios
    public bool CloseQuits { get; set; }
    public string CloseHint { get; }                   // conforme a escolha (textos das Global Constraints)
    [ObservableProperty] string? ErrorMessage;
    public IAsyncRelayCommand LoadCommand { get; }     // Activate() chama; lê tudo sem gravar
}
```

Regras:
- **Carga:** carregar não grava nada. Use uma flag `_loading` para que a atribuição inicial das propriedades não dispare gravação.
- **Gravação:** cada mudança grava na hora.
- **Falha ao gravar:** o valor volta ao anterior e a tela mostra a mensagem das Global Constraints. Os erros de autostart (`IOException`, `UnauthorizedAccessException`) usam a mensagem própria.
- **View:** três cartões (`Border.card`) com os títulos de seção.
  - Geral: `ToggleSwitch` com o texto e a dica.
  - Lembretes: `ComboBox` das opções.
  - Janela: o segmento `Border.segment`/`RadioButton.segment` e a dica embaixo.
  - A faixa `warning-band` mostra o `ErrorMessage`.
- **DI:** registrar `AppSettings` (Core) e `IAutostart` → `XdgAutostart(XdgPaths.AutostartDirectory(...), Environment.ProcessPath!)`.

- [ ] **Step 1: Write the failing tests** (`InMemorySettingsRepository` e um `FakeAutostart` no próprio arquivo de teste):
  - `Load_reads_current_values_without_writing`: com snooze `"15"`, `quit` e autostart ligado, os valores são `(StartWithSystem true, SelectedSnooze.Minutes 15, CloseQuits true, CloseHint "Com o app fechado, os lembretes param.")`, e o repositório não recebe gravação.
  - `Changing_snooze_saves_immediately`: escolher a opção "30 min" grava `"30"`.
  - `Changing_close_action_saves_and_updates_hint`: `CloseHides = true` grava `"hide"`, e a dica vira `"Os lembretes continuam com a janela fechada."`.
  - `Toggling_autostart_calls_enable_and_disable`.
  - `Autostart_failure_reverts_toggle_and_shows_error`: `Enable` lança `IOException`; `StartWithSystem` volta a falso e `ErrorMessage == "Não foi possível alterar o início com o sistema."`.
  - `Without_autostart_toggle_is_unavailable`: sem `IAutostart`, `CanAutostart` é falso e o texto é `"Indisponível neste sistema."`.
  - `Snooze_labels`: os rótulos são `"5 min", "10 min", "15 min", "30 min", "1 h"`.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** VM, view e DI; atualize `ShellViewModelTests`.
- [ ] **Step 4: Run tests + build** — Expected: PASS, 0 avisos.
- [ ] **Step 5: Commit** — `feat(app): tela de Configurações`

---

### Task 6: Ícone do app, bandeja e `WM_CLASS`

**Files:**
- Create: `src/SlimeGoals.App/Assets/icon.svg`, `src/SlimeGoals.App/Assets/icon-256.png`, `packaging/linux/icons/hicolor/<N>x<N>/apps/slimegoals.png` (N = 16, 32, 48, 64, 128, 256, 512), `packaging/linux/icons/hicolor/scalable/apps/slimegoals.svg`, `packaging/linux/make-icons.sh`
- Modify: `src/SlimeGoals.App/Assets/tray.svg`, `src/SlimeGoals.App/Assets/tray.png`, `src/SlimeGoals.App/Views/MainWindow.axaml` (`Icon`), `src/SlimeGoals.Desktop/Program.cs` (`X11PlatformOptions.WmClass`), `THIRD-PARTY-NOTICES.md`

**Interfaces:** nenhuma de código. `make-icons.sh` gera todos os PNG a partir do `icon.svg` com `rsvg-convert -w N -h N`; `tray.png` é o mesmo desenho em 64 px.

Desenho do `icon.svg`:
- copie do `<svg id="slime">` do design os elementos da pose parada:
  - o `radialGradient` `g`;
  - o `path` do corpo;
  - os dois brilhos (`ellipse` com `rotate(-28 …)` e `circle` r 3.5);
  - as bochechas;
  - os olhos abertos (`g.eyes`);
  - a boca `m-idle`;
- tire a sombra, o confete, os olhos fechados, as outras bocas e as classes e animações;
- `viewBox="15 35 170 170"`: um quadrado em volta do corpo, que vai de x 20 a 180 e de y 66 a 174 no desenho original; o corpo fica centrado e ocupa cerca de 95% da largura;
- nos tamanhos de 16 e 32 px, os brilhos podem sumir: confira o resultado e, se precisar, faça um `icon-small.svg` sem os brilhos só para esses dois.

Passos:
- [ ] **Step 1:** Criar o `icon.svg` e o `make-icons.sh`; gerar os PNG. Abra o PNG de 256 e o de 32 e confira à vista: o corpo roxo, os olhos e a boca devem ficar legíveis.
- [ ] **Step 2:** `MainWindow.axaml` passa a usar `Icon="avares://SlimeGoals.App/Assets/icon-256.png"`. A bandeja usa o `tray.png` novo, sem mudança de código.
- [ ] **Step 3:** No `Program`, `.With(new X11PlatformOptions { WmClass = "slimegoals" })`.
  - Confirme com o app aberto (banco temporário): `xprop -name SlimeGoals WM_CLASS` mostra `"slimegoals"`.
  - Se o `xprop` não achar a janela, use `xdotool search --class slimegoals` ou registre no relatório que é preciso conferir à mão.
- [ ] **Step 4:** Atualizar o `THIRD-PARTY-NOTICES.md`: o "target" do Lucide deixa de ser o ícone da bandeja. O ícone do app e da bandeja é o mascote do próprio projeto (código do repositório).
- [ ] **Step 5: Build + tests** — Expected: PASS, 0 avisos.
- [ ] **Step 6: Commit** — `feat(app): ícone do slime para o app e a bandeja`

---

### Task 7: Pacote, instalação e licenças

**Files:**
- Create: `packaging/linux/slimegoals.desktop.in`, `packaging/linux/build.sh`, `packaging/linux/install.sh`, `packaging/linux/uninstall.sh`, `packaging/linux/licenses/*.txt`
- Modify: `Directory.Build.props` (`<Version>0.6.0</Version>`), `THIRD-PARTY-NOTICES.md`, `README.md` (seção "Instalar no Linux")

Conteúdo exato do `slimegoals.desktop.in` (o `install.sh` troca `@EXEC@` por `QuoteExec` do caminho instalado, com a mesma regra da Task 2):

```
[Desktop Entry]
Type=Application
Name=SlimeGoals
GenericName=Metas e lembretes
Comment=Hábitos e metas com lembretes
Exec=@EXEC@
Icon=slimegoals
Terminal=false
Categories=Utility;
Keywords=metas;hábitos;lembretes;
StartupWMClass=slimegoals
StartupNotify=true
```

Scripts (bash, `set -euo pipefail`):
- **`build.sh`:**
  - `dotnet publish src/SlimeGoals.Desktop -c Release -r linux-x64 --self-contained -o artifacts/slimegoals-<versão>/app`;
  - copia `install.sh`, `uninstall.sh`, `slimegoals.desktop.in`, `icons/`, `licenses/`, `LICENSE.md` e `THIRD-PARTY-NOTICES.md` para `artifacts/slimegoals-<versão>/`;
  - gera `artifacts/slimegoals-<versão>-linux-x64.tar.gz`;
  - lê a versão do `Directory.Build.props`.
- **`install.sh`**, rodado de dentro da pasta extraída:
  - instala nos caminhos das Global Constraints, a partir de `$HOME`;
  - substitui uma instalação anterior: apaga `~/.local/opt/slimegoals` antes de copiar e refaz o link;
  - roda `update-desktop-database ~/.local/share/applications` e `gtk-update-icon-cache -q -t ~/.local/share/icons/hicolor`, se existirem;
  - se houver um processo `slimegoals` rodando, avisa: "Feche o SlimeGoals (Sair) e abra de novo para usar a versão nova."
- **`uninstall.sh`:**
  - remove o app, o link, o `.desktop` e os ícones;
  - **não** apaga `~/.local/share/SlimeGoals` nem o autostart;
  - avisa: "Seus dados continuam em ~/.local/share/SlimeGoals."
  - se `~/.config/autostart/slimegoals.desktop` existir, avisa que ele aponta para o app removido e pode ser apagado.
- **Licenças:**
  - liste o que o `publish` copia: assemblies gerenciados e nativos, como `libSkiaSharp.so`, `libHarfBuzzSharp.so` e `libe_sqlite3.so`, além do runtime .NET;
  - em `packaging/linux/licenses/`, ponha o texto de cada licença envolvida: MIT (.NET runtime, Avalonia, SkiaSharp, HarfBuzzSharp, CommunityToolkit, Microsoft.Extensions, Tmds.DBus, Microsoft.Data.Sqlite), Apache-2.0 (Dapper, SQLitePCLRaw), BSD-3-Clause (Skia), licença do HarfBuzz ("Old MIT"), aviso de domínio público do SQLite, OFL (Figtree) e ISC (Lucide);
  - copie o texto do `LICENSE` de cada pacote em `~/.nuget/packages/<pacote>/<versão>/` quando existir;
  - o `THIRD-PARTY-NOTICES.md` ganha a seção "Distribuídos no pacote", uma tabela componente → licença → arquivo.

- [ ] **Step 1:** Versão, `.desktop.in`, scripts e `licenses/`.
- [ ] **Step 2: Build do pacote** — Run: `bash packaging/linux/build.sh`. Expected: o `.tar.gz` existe, e `artifacts/slimegoals-0.6.0/app/slimegoals` é executável.
- [ ] **Step 3: Teste de instalação num HOME temporário:**
  - `HOME=<scratch>/home bash artifacts/slimegoals-0.6.0/install.sh`, duas vezes seguidas;
  - confira os arquivos nos caminhos esperados e o `.desktop` com `Exec="<scratch>/home/.local/opt/slimegoals/slimegoals"`;
  - `desktop-file-validate`, se existir, não aponta erro;
  - depois, `HOME=<scratch>/home bash artifacts/slimegoals-0.6.0/uninstall.sh`: os arquivos do app somem e uma pasta `~/.local/share/SlimeGoals` criada antes continua lá.
- [ ] **Step 4: Teste de fumaça do binário publicado:**
  - `XDG_DATA_HOME=<scratch>/data XDG_RUNTIME_DIR=<scratch>/run timeout 15 artifacts/slimegoals-0.6.0/app/slimegoals`;
  - o app abre sem exceção.
- [ ] **Step 5:** README com a seção "Instalar no Linux": baixar ou gerar o pacote, `./install.sh`, desinstalar, e onde ficam os dados.
- [ ] **Step 6: Commit** — `build: pacote self-contained com instalação no usuário e licenças`

---

### Task 8: Spec

**Files:**
- Modify: `specs/Planejamento_v3.md`

- [ ] **Step 1: Update:**
  - **§4 (Empacotamento):** `.tar.gz` *self-contained* com `install.sh`; AppImage e PKGBUILD como opcionais futuros.
  - **§6:** os arquivos novos (`Autostart/`, `Power/`, `packaging/linux/`).
  - **§7, Configurações:** o que a tela tem.
  - **§9:**
    - item 2: o adiar vira configuração (5, 10, 15, 30 ou 60 min; padrão 10);
    - item 8: "ao fechar" configurável, `--background` no autostart;
    - item 10: suspensão implementada (logind `PrepareForSleep`; ao acordar recalcula; vencidos disparam uma vez).
  - **§12, linha 6:** "Implementado (verificação manual pelo autor pendente)" com o resumo.
  - **§14:** a linha da bandeja menciona o ícone definitivo.
  - **§15:** novo bloco "Decisões da Fase 6 (03/11/2026)" com as decisões 1–8, uma linha cada. Atualize as frases antigas que diziam "vem na Fase 6".
- [ ] **Step 2: Commit** — `docs(specs): acabamento do Linux da Fase 6`

---

## Verificação manual (autor, depois do PR aberto)

1. **Instalar:** `bash packaging/linux/build.sh` e depois `./install.sh` na pasta gerada.
   - O SlimeGoals aparece na grade de aplicativos com o ícone do slime e abre.
   - Seu banco real continua lá: é o mesmo `~/.local/share/SlimeGoals`.
2. **Nome e ícone:** com o app instalado aberto, uma notificação mostra "SlimeGoals" e o ícone no cabeçalho. O painel e o Alt+Tab também mostram o ícone.
3. **Configurações:**
   - **Adiar:** troque para 15 min e responda "Adiar" num lembrete; o próximo vem 15 min depois.
   - **Ao fechar:** escolha "Encerrar o app" e feche a janela; o processo termina (`pgrep -x slimegoals` vazio).
   - **Iniciar com o sistema:** ligue, saia da sessão e entre de novo. O app roda sem janela e os lembretes chegam. Abrir pelo atalho mostra a janela.
4. **Suspensão:** suspenda com um lembrete marcado para dali a poucos minutos e acorde depois do horário. O lembrete chega logo depois de acordar. Se tiver passado da meia-noite, a tela Hoje mostra o dia novo.
5. **Desinstalar:** `./uninstall.sh` remove o app e mantém os dados.
