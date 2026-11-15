# Cronômetro visível fora do app — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Com um cronômetro de meta de tempo rodando, o tempo fica à vista fora do app: no Linux, numa janela pequena sempre por cima quando a janela principal está fechada ou minimizada; no Android, numa notificação fixa com o cronômetro do sistema. Nas duas, um toque em "Parar" congela o cronômetro e abre o app em Hoje.

**Architecture:**
- **Core:** `RunningTimerWatcher` escuta o `DataChangeNotifier` e publica o cronômetro rodando (meta, ícone, cor, início) ou nulo. `ITimerNotification` é a porta da notificação. `TimerNotificationSync` liga os dois.
- **App (Linux):** `MiniTimerViewModel` (texto e comandos), `MiniTimerController` (regra de mostrar e esconder, sobre abstrações da janela principal e da janelinha) e `MiniTimerWindow` (a janela da opção A). A `App` liga tudo quando `PlatformCapabilities.MiniTimer` é verdadeiro.
- **Android:** `AndroidTimerNotification` implementa `ITimerNotification` com o cronômetro nativo. O "Parar" chega à `MainActivity` por um `PendingIntent` de atividade.
- Nada muda no banco nem no modelo: o cronômetro continua sendo só o instante de início em `active_timer`.

**Tech Stack:** as versões atuais, sem pacote novo. .NET 10 / C# 14, Avalonia 12.1.4, CommunityToolkit.Mvvm, xUnit v3, AwesomeAssertions, FakeTimeProvider.

**Spec:** `specs/Cronometro_fora_do_app_v1.md` (aprovada em 14/11/2026, opção A da janelinha). Protótipo: `specs/design/cronometro-janelinha-prototipo.png`. Spike: branch local `spike/janelinha-cronometro`.

## Decisões deste plano (o autor confirma na revisão)

1. **"Parar" no Android sem receptor.** A spec previa um receptor não exportado para o "Parar". Desde o Android 12, um receptor não pode abrir uma atividade a partir de uma notificação. O botão "Parar" passa a abrir a `MainActivity` direto, com um extra `stopTimer`, e é ela que para o cronômetro. A Tarefa 6 corrige essa frase na spec.
2. **A notificação do Android não usa o ciclo de vida da tela.** Ela precisa aparecer também quando o cronômetro começa pelo "Iniciar" de um lembrete, com o app fechado. Por isso `TimerNotificationSync` é ligado no `AndroidApplication.OnCreate`, e não como serviço da tela.
3. **Posição da janelinha:** gravada em `settings` ao soltar o arrasto. Ao mostrar, uma posição fora de todas as telas volta para o canto inferior direito da tela principal, com 24 px de margem.
4. **Opção de desligar:** em Configurações › Janela, "Mostrar o cronômetro numa janela pequena", ligada por padrão. Só existe onde `PlatformCapabilities.MiniTimer` é verdadeiro (desktop).
5. **Tamanho da janelinha:** 280 px de largura, altura pelo conteúdo (cerca de 66 px), como no protótipo.
6. **Cronômetro de meta que sumiu ou foi arquivada** não conta como rodando: nem janelinha nem notificação.
7. **Vale no app instalado.** Não há rede envolvida, então a janelinha entra no Release do Linux e a notificação no APK normal.

## Global Constraints

- Identificadores em inglês; textos e comentários em pt-BR. `TreatWarningsAsErrors` ligado.
- .NET 10 / C# 14: usar os tipos da versão (`Lock` em vez de `object` para travas, expressões de coleção, `TimeProvider`).
- `Trace` nunca grava a exceção nem a mensagem dela: `TraceText.Of(e)` ou `TraceText.Types(e)`. O teste `No_trace_call_in_the_app_writes_the_exception_or_its_message` cobre isso.
- "A janelinha não rouba o foco do que a pessoa está digitando" e "a notificação não faz som nem vibra" (spec §3).
- "Nenhum serviço rodando o tempo todo no Android só para manter a notificação" (spec §3). Nenhuma permissão nova no manifesto.
- "O app do Linux não ganha comportamento novo com o cronômetro parado" (spec §3): sem cronômetro rodando, nenhuma janela é criada.
- Core continua sem I/O e sem pacote novo (`ArchitectureTests`).
- Testes nunca usam o banco real, o app instalado, o chaveiro nem o barramento do usuário, e nunca abrem janela de verdade.
- Telas conferidas na Render; a janelinha ganha o modo `mini`.
- **Verificação:** `dotnet test --solution SlimeGoals.slnx` (Docker ligado), `dotnet build SlimeGoals.slnx -c Release` e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, sem avisos.
- **Commits:** Conventional Commits em português, escopos `core`, `app`, `android`, `desktop`, `specs`, terminando com a linha de coautoria que o ambiente indicar. Branch `feat/cronometro-fora-do-app`. Antes de cada push, conferir que o PR da branch, se existir, está aberto.

## Review Focus

1. A meta do cronômetro é excluída ou arquivada com ele rodando: a janelinha e a notificação somem, sem erro (Tarefa 2).
2. A posição gravada ficou fora da tela (o autor usa três monitores; um pode ser desligado): a janelinha aparece no canto padrão, visível (Tarefa 4).
3. "Parar" clicado duas vezes, ou com o cronômetro já parado por outro caminho: nenhum erro aparece, e a janelinha some (Tarefa 4).
4. O app inicia com `--background` e já há um cronômetro rodando: a janelinha aparece sem a janela principal ter sido aberta (Tarefa 4).
5. No Android, o processo morreu e a pessoa toca em "Parar" na notificação: o app abre em Hoje com o cronômetro parado uma vez só; depois de reiniciar o celular, a notificação volta (Tarefa 5, parte no celular).

---

### Task 1: Registrar o spike da janela sempre por cima

**Files:**
- Create: `specs/spikes/janelinha-cronometro.md`

**Pré-condição:** o autor rodou `dotnet run --project spikes/janelinha` na branch `spike/janelinha-cronometro` e respondeu às perguntas abaixo. Sem essa resposta, a tarefa não começa.

- [ ] **Step 1:** Escrever o relatório no formato de `specs/spikes/gnome-notificacoes.md`: ambiente, método, tabela de respostas e decisões. Perguntas:
  1. A janelinha fica por cima de outra janela em foco, inclusive maximizada?
  2. Ela aparece sem roubar o foco de quem estava digitando?
  3. Aparece na posição pedida (canto inferior direito da tela principal)?
  4. Arrastar move, e a posição nova fica?
  5. Os cantos arredondados aparecem sem fundo quadrado?
  6. O GNOME mostra algum aviso de "janela pronta" ou põe a janelinha na lista de janelas?
- [ ] **Step 2:** Registrar o que a medição automática já mostrou em 14/11/2026: `_NET_WM_STATE` com `ABOVE` e `SKIP_TASKBAR`; posição pedida `3536, 990` mantida dois segundos depois; nenhum evento de foco; para a posição valer, ela tem de ser definida **antes** de `Show()`, com `WindowStartupLocation.Manual`.
- [ ] **Step 3: Commit** `docs(specs): registra o spike da janela sempre por cima`

Se a resposta 1 ou 2 for "não", parar e voltar ao autor: o desenho do Linux muda antes das Tarefas 3 e 4.

### Task 2: `RunningTimerWatcher` e a porta da notificação

**Files:**
- Create: `src/SlimeGoals.Core/Services/RunningTimerWatcher.cs`, `src/SlimeGoals.Core/Abstractions/ITimerNotification.cs`, `src/SlimeGoals.Core/Services/TimerNotificationSync.cs`
- Test: `tests/SlimeGoals.Infrastructure.Tests/Services/RunningTimerWatcherTests.cs` (repositórios reais sobre `TestDatabase`), `tests/SlimeGoals.Core.Tests/Services/TimerNotificationSyncTests.cs`

**Interfaces:**
- Consumes: `TimerService.GetAsync()`, `IGoalRepository.GetAsync(Guid)`, `DataChangeNotifier.Changed`, `ActiveTimer(GoalId, StartedAt, StoppedAt)`.
- Produces (namespace `SlimeGoals.Core.Services`, salvo indicação):
  ```csharp
  public sealed record RunningTimer(Guid GoalId, string Title, string Icon, GoalColor Color, DateTimeOffset StartedAt);

  public sealed class RunningTimerWatcher(TimerService timers, IGoalRepository goals, DataChangeNotifier changes) : IDisposable
  {
      public RunningTimer? Current { get; }          // seguro de ler de qualquer thread
      public event EventHandler? Changed;            // só quando Current muda; em qualquer thread
      public Task StartAsync(CancellationToken ct = default);   // assina as mudanças (uma vez) e faz a primeira leitura
      public Task RefreshAsync(CancellationToken ct = default); // relê; nunca lança, falha vai ao Trace e mantém o valor
  }

  // SlimeGoals.Core.Abstractions
  public interface ITimerNotification
  {
      void Show(RunningTimer timer);   // publica ou atualiza
      void Cancel();                   // retira; sem notificação, não faz nada
  }

  public sealed class TimerNotificationSync(RunningTimerWatcher watcher, ITimerNotification notification)
  {
      public Task StartAsync(CancellationToken ct = default);   // assina watcher.Changed (uma vez), inicia o watcher e sincroniza
      public Task SyncAsync(CancellationToken ct = default);    // relê e chama Show ou Cancel; nunca lança
  }
  ```
  - "Rodando" é `ActiveTimer` com `StoppedAt` nulo **e** meta existente e não arquivada. Qualquer outro caso é nulo.

- [ ] **Step 1: Escrever os testes**
  - `Start_reads_the_running_timer_with_the_goal_data`: meta de tempo, `TimerService.StartAsync`; `Current` tem o id, o título, o ícone, a cor e o início.
  - `Stop_register_and_discard_clear_it`: depois de `StopAsync`, `Current` é nulo e `Changed` disparou uma vez; o mesmo para `DiscardAsync`.
  - `Changed_fires_only_when_the_value_changes`: uma mudança sem relação (editar outra meta) não dispara.
  - `Deleted_or_archived_goal_is_not_running` (Review Focus 1): com o cronômetro rodando, excluir a meta, e em outro caso arquivar; `Current` vira nulo.
  - `Renaming_the_goal_updates_the_title`.
  - `Failure_keeps_the_last_value_and_does_not_throw`: repositório que lança; `Current` não muda; o Trace não contém a mensagem da exceção.
  - `Sync_shows_when_running_and_cancels_when_not`: falso de `ITimerNotification` grava as chamadas; iniciar → `Show` com o cronômetro; parar → `Cancel`.
  - `Sync_survives_a_notification_that_throws`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --project tests/SlimeGoals.Infrastructure.Tests` e `dotnet test --project tests/SlimeGoals.Core.Tests`.
- [ ] **Step 3: Implementar.** `Current` guardado com `Volatile`; a leitura em andamento mais nova vence a mais antiga (contador de versão, como `TrayMenuViewModel.RefreshAsync`).
- [ ] **Step 4: Rodar:** passa.
- [ ] **Step 5: Commit** `feat(core): observa o cronômetro rodando`

### Task 3: Opção em Configurações e capacidade da plataforma

**Files:**
- Modify: `src/SlimeGoals.Core/Services/AppSettings.cs`, `src/SlimeGoals.App/Hosting/PlatformCapabilities.cs`, `src/SlimeGoals.App/ViewModels/SettingsPageViewModel.cs`, `src/SlimeGoals.App/Views/SettingsView.axaml`
- Test: `tests/SlimeGoals.Core.Tests` (AppSettings), `tests/SlimeGoals.App.Tests/ViewModels/SettingsPageViewModelTests.cs`

**Interfaces:**
- Produces:
  - `AppSettings`: chaves `timer.mini_window` e `timer.mini_window_position`.
    ```csharp
    Task<bool> GetMiniTimerEnabledAsync(CancellationToken ct = default);          // padrão: true
    Task SetMiniTimerEnabledAsync(bool enabled, CancellationToken ct = default);
    Task<(int X, int Y)?> GetMiniTimerPositionAsync(CancellationToken ct = default);   // nulo se ausente ou ilegível
    Task SetMiniTimerPositionAsync(int x, int y, CancellationToken ct = default);
    ```
    A posição é gravada como `"x,y"` com cultura invariante.
  - `PlatformCapabilities(bool Reminders, bool Autostart, bool WindowClose, bool Quit, bool Sync = false, bool MiniTimer = false)`; `PlatformCapabilities.Desktop` passa a ter `MiniTimer = true`.
  - `SettingsPageViewModel`: `bool ShowMiniTimer` (a capacidade) e `bool MiniTimerEnabled` (grava na hora; se falhar, volta ao valor anterior, como as outras opções).
  - Tela: na página do grupo **Janela**, um `ToggleSwitch` "Mostrar o cronômetro numa janela pequena" e, abaixo, o texto "Aparece quando a janela principal está fechada ou minimizada e há um cronômetro rodando.", visíveis por `ShowMiniTimer`.

- [ ] **Step 1: Escrever os testes**
  - `Mini_timer_is_on_by_default_and_remembers_the_choice`.
  - `Mini_timer_position_round_trips_and_garbage_reads_as_null`: `"12,-40"` volta `(12, -40)`; `"abc"`, `"1"` e `"1,2,3"` voltam nulo.
  - `Mini_timer_option_follows_the_capability`: `ShowMiniTimer` verdadeiro no desktop e falso em `new PlatformCapabilities(true, false, false, false)`.
  - `Toggling_the_mini_timer_saves_and_a_failed_save_reverts`.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.**
- [ ] **Step 4: Rodar:** passa. Render `all`: conferir `settings-window-desktop.png`.
- [ ] **Step 5: Commit** `feat(app): opção de mostrar o cronômetro numa janela pequena`

### Task 4: A janelinha do Linux

**Files:**
- Create: `src/SlimeGoals.App/ViewModels/MiniTimerViewModel.cs`, `src/SlimeGoals.App/Hosting/MiniTimerController.cs`, `src/SlimeGoals.App/Hosting/IMiniTimerWindow.cs`, `src/SlimeGoals.App/Hosting/IMainWindowPresence.cs`, `src/SlimeGoals.App/Hosting/MainWindowPresence.cs`, `src/SlimeGoals.App/Views/MiniTimerWindow.axaml` (+ `.axaml.cs`)
- Modify: `src/SlimeGoals.App/App.axaml.cs`, `src/SlimeGoals.App/Styles/Controls.axaml`, `src/SlimeGoals.Desktop/CompositionRoot.cs`, `tools/SlimeGoals.Render/Program.cs` e `README.md` da Render (modo `mini`)
- Test: `tests/SlimeGoals.App.Tests/ViewModels/MiniTimerViewModelTests.cs`, `tests/SlimeGoals.App.Tests/Hosting/MiniTimerControllerTests.cs`

**Interfaces:**
- Consumes: `RunningTimerWatcher` e `RunningTimer` (Tarefa 2), `AppSettings` (Tarefa 3), `TimerService.StopAsync`, `IWindowActivator.Activate`, `ShellViewModel.CurrentPage`, `GoalFormatting.Elapsed(TimeSpan)`.
- Produces (namespace `SlimeGoals.App.Hosting`, salvo indicação):
  ```csharp
  public interface IMainWindowPresence
  {
      bool IsOnScreen { get; }            // visível e não minimizada
      event EventHandler? Changed;
  }

  public interface IMiniTimerWindow
  {
      void Show((int X, int Y)? position);   // nulo: canto padrão
      void Hide();
      event EventHandler<(int X, int Y)>? Moved;   // ao soltar o arrasto
  }

  // SlimeGoals.App.ViewModels
  public sealed partial class MiniTimerViewModel(RunningTimerWatcher watcher, TimerService timers, IWindowActivator window,
      ShellViewModel shell, TimeProvider time) : ObservableObject
  {
      public string Title { get; }        // nome da meta
      public string IconKey { get; }
      public GoalColor Color { get; }
      public string Elapsed { get; }      // GoalFormatting.Elapsed, atualizado a cada segundo enquanto houver cronômetro
      public IRelayCommand OpenCommand { get; }          // mostra a janela principal
      public IAsyncRelayCommand StopCommand { get; }     // para, abre a janela principal e seleciona Hoje
  }

  public sealed class MiniTimerController(RunningTimerWatcher watcher, IMainWindowPresence main, IMiniTimerWindow window,
      AppSettings settings, DataChangeNotifier changes, Action<Action> dispatch)
  {
      public Task StartAsync();   // assina tudo e aplica a regra uma vez
  }
  ```
  - **Regra:** a janelinha fica à mostra quando a opção está ligada **e** `watcher.Current` não é nulo **e** `main.IsOnScreen` é falso. Qualquer mudança em um dos três reaplica a regra, na thread da interface (`dispatch`). A opção é relida a cada `DataChangeNotifier.Changed`, como faz `WindowCloseBehavior`.
  - **Parar:** `StopAsync` que lança `GoalStateException` (cronômetro já parado ou ausente) não mostra erro: abre o app do mesmo jeito.
  - **Janela** (o que o spike confirmou): `WindowDecorations.None`, `Topmost`, `ShowInTaskbar = false`, `ShowActivated = false`, `CanResize = false`, `SizeToContent`, fundo transparente, `WindowStartupLocation.Manual` e a posição definida **antes** de `Show()`. Arrastar com `BeginMoveDrag`; clique sem arrasto executa `OpenCommand`.
  - **Aparência:** opção A do protótipo, 280 px, com os estilos dos cartões (`Tile`, `Border`, `RadiusCard`), `icon-tile`, o nome em 13 px `Muted`, o tempo em 22 px negrito e o botão `danger-solid icon` com `IconStop`.
  - **Composição:** o desktop registra `RunningTimerWatcher`, `MiniTimerViewModel` e o controlador. A `App` cria a `MiniTimerWindow` só quando `PlatformCapabilities.MiniTimer` é verdadeiro, e só na primeira vez que a regra manda mostrar.

- [ ] **Step 1: Escrever os testes** (falsos de `IMainWindowPresence` e `IMiniTimerWindow`; `FakeTimeProvider`)
  - `Shows_when_a_timer_runs_and_the_main_window_is_away`, `Hides_when_the_main_window_comes_back`, `Hides_when_the_timer_stops`.
  - `Stays_hidden_while_the_option_is_off_and_follows_it_when_toggled`.
  - `Shows_at_startup_in_background_with_a_timer_already_running` (Review Focus 4): `IsOnScreen` falso desde o início e cronômetro já gravado; depois de `StartAsync`, `Show` foi chamado.
  - `Saved_position_is_used_and_a_drop_saves_the_new_one`: `Show` recebe a posição gravada; `Moved` grava a nova.
  - `Nothing_is_created_without_a_running_timer`: sem cronômetro, nenhuma chamada a `Show`.
  - `Elapsed_ticks_every_second_and_stops_ticking_without_a_timer`: avançar o relógio 61 s muda de `0:00:00` para `0:01:01`.
  - `Stop_freezes_the_timer_opens_the_window_and_selects_today`.
  - `Stop_twice_or_with_the_timer_already_stopped_shows_no_error` (Review Focus 3).
  - Na `MiniTimerWindow` (função estática pura, testável sem janela): `PlaceWithin_falls_back_when_the_saved_position_is_off_every_screen` (Review Focus 2), com três telas de 1920×1080 e uma posição em `5000, 300`.
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test --project tests/SlimeGoals.App.Tests`.
- [ ] **Step 3: Implementar** o ViewModel, o controlador, a janela e a ligação na `App`.
- [ ] **Step 4: Render.** Modo `mini` grava `mini-timer.png` com a janela real sobre um fundo neutro. Comparar com `specs/design/cronometro-janelinha-prototipo.png`.
- [ ] **Step 5: Rodar tudo** (Global Constraints) e conferir à mão no build Debug: iniciar um cronômetro, fechar a janela, ver a janelinha, arrastar, reabrir pelo clique, parar pelo botão.
- [ ] **Step 6: Commit** `feat(app): janela pequena com o cronômetro rodando`

### Task 5: A notificação do Android

**Files:**
- Create: `src/SlimeGoals.Android/Reminders/AndroidTimerNotification.cs`
- Modify: `src/SlimeGoals.Android/AndroidCompositionRoot.cs`, `src/SlimeGoals.Android/AndroidApplication.cs`, `src/SlimeGoals.Android/MainActivity.cs`, `src/SlimeGoals.Android/Reminders/ReminderReceivers.cs` (`SystemEventReceiver`)
- Test: a lógica já está coberta na Tarefa 2 (`TimerNotificationSyncTests`); aqui, `tests/SlimeGoals.App.Tests` ganha o teste do pedido de parar

**Interfaces:**
- Consumes: `ITimerNotification`, `TimerNotificationSync`, `RunningTimer` (Tarefa 2), `TimerService.StopAsync`, `ShellViewModel`.
- Produces:
  - `AndroidTimerNotification(Context context) : ITimerNotification`:
    - canal `cronometro`, nome "Cronômetro", importância baixa (sem som nem vibração);
    - id de notificação 2 (os lembretes usam o 1);
    - fixa (`SetOngoing`), `SetUsesChronometer(true)`, `SetWhen` com o início em milissegundos, `SetOnlyAlertOnce`, categoria de cronômetro, o mesmo ícone e a mesma cor dos lembretes;
    - título: o nome da meta;
    - toque: `PendingIntent.GetActivity` para a `MainActivity`, imutável;
    - ação "Parar": `PendingIntent.GetActivity` para a `MainActivity` com o extra `AndroidTimerNotification.ExtraStopTimer = "stopTimer"` verdadeiro, imutável, com código de pedido diferente do toque.
  - `MainActivity`: em `OnCreate` e em `OnNewIntent`, se o intent traz `ExtraStopTimer`, chama `StopRequest.RunAsync` e remove o extra do intent, para uma recriação da tela não parar outro cronômetro.
  - `StopRequest` (em `src/SlimeGoals.App/Hosting/StopRequest.cs`, testável fora do Android):
    ```csharp
    public static Task RunAsync(TimerService timers, ShellViewModel shell);   // para (ignorando GoalStateException) e seleciona Hoje
    ```
  - `AndroidApplication.OnCreate`: depois de migrar, `TimerNotificationSync.StartAsync()` sem esperar; falha vai ao Trace.
  - `SystemEventReceiver`: depois de reagendar os lembretes, `TimerNotificationSync.SyncAsync()`.

- [ ] **Step 1: Escrever os testes**
  - `Stop_request_stops_the_timer_and_selects_today`.
  - `Stop_request_with_no_timer_or_an_already_stopped_one_only_selects_today` (Review Focus 5, a parte testável).
- [ ] **Step 2: Rodar e ver falhar;** **Step 3: implementar;** **Step 4:** `dotnet build SlimeGoals.Android.slnx …` sem avisos, e a suíte verde.
- [ ] **Step 5: Commit** `feat(android): notificação com o cronômetro rodando`

**Verificação no celular (autor, com APK novo de `packaging/android/build.sh`):**
1. Iniciar um cronômetro: a notificação aparece em silêncio, com o tempo correndo, e continua com o app aberto.
2. Sair do app e travar a tela: o tempo segue na tela de bloqueio.
3. Tocar em "Parar": o app abre em Hoje, com o cronômetro parado esperando o registro, e a notificação some.
4. Iniciar pelo botão "Iniciar" de um lembrete, com o app fechado: a notificação aparece.
5. Com o app em segundo plano, matar o processo (`adb shell am kill dev.alcm.slimegoals`) e tocar em "Parar": o app abre com o cronômetro parado (Review Focus 5). "Forçar parada" nas configurações não serve: ela também apaga as notificações do app.
6. Reiniciar o celular com um cronômetro rodando: a notificação volta depois do boot.
7. Dispensar a notificação (Android 14+) e voltar ao app: ela aparece de novo.

### Task 6: Documentos e verificação final

**Files:**
- Modify: `specs/Cronometro_fora_do_app_v1.md`, `specs/Planejamento_v3.md`, `README.md`

- [ ] **Step 1:** Na spec do cronômetro: trocar "Um receptor não exportado trata o 'Parar'" pela decisão 1 deste plano, e acrescentar a seção "Como ficou", com as decisões 2, 3 e 6 e o resultado do spike em uma frase cada.
- [ ] **Step 2:** `Planejamento_v3.md`: na linha de "Hoje" da tabela de telas e na lista de requisitos, registrar a janelinha (desktop) e a notificação (Android), e a opção nova do grupo Janela.
- [ ] **Step 3:** README: uma frase na descrição do app sobre o cronômetro fora da janela.
- [ ] **Step 4: Verificação completa** (Global Constraints).
- [ ] **Step 5: Commit** `docs(specs): registra o cronômetro visível fora do app`
