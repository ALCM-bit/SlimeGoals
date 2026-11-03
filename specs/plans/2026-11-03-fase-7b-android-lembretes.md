# Fase 7b — Android: lembretes — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No Android, os lembretes disparam pelo `AlarmManager` e as três ações da notificação (ação principal, Adiar, Ignorar) funcionam com o app fechado, inclusive depois de reiniciar o aparelho. Critério de pronto da §12: "Lembrete dispara e as 3 ações funcionam com o app fechado, inclusive após reiniciar o aparelho".

**Architecture:**
- **Core:** nova porta `ISystemAlarm` (um único alarme do sistema) e `SystemAlarmReminderScheduler`, a implementação de `IReminderScheduler` para quando o processo pode morrer:
  - a cada reagendamento chama `ReminderService.SyncAsync()`, que dispara os devidos e devolve o próximo, e arma o alarme nesse instante (ou cancela, se não houver);
  - não expira pendentes ao iniciar: no Android a notificação e suas ações sobrevivem ao processo.
- **App:** porta `IReminderPermissions` (notificações e alarmes exatos). A tela Hoje avisa quando falta permissão, com o botão "Permitir"; Configurações mostra o estado das duas.
- **Head `SlimeGoals.Android`:** os adaptadores:
  - `AndroidSystemAlarm`: `SetExactAndAllowWhileIdle`; sem permissão de alarme exato, `SetAndAllowWhileIdle`;
  - `AndroidNotificationService`: canal "Lembretes", uma notificação por disparo, com três ações;
  - receivers sem UI: alarme, ações e eventos do sistema (boot, atualização do app, hora, fuso, permissão de alarme exato devolvida);
  - `AndroidReminderPermissions`;
  - o container vem do `AndroidApplication`, que já é montado no `OnCreate` também quando o processo nasce só para um receiver (spike).

**Tech Stack:** as mesmas versões da 7a; nenhum pacote novo. APIs do Android: `AlarmManager`, `NotificationManager`, `NotificationChannel`, `PendingIntent`, `BroadcastReceiver.GoAsync()`.

**Spec:** `specs/Planejamento_v3.md` v1.4:
- §5 (princípio dos lembretes: um único disparo; `ReminderResponseHandler` chamado direto pelo `BroadcastReceiver`, inclusive com o app fechado);
- §9 itens 2–6, 9 e 11 (regra do intervalo, adiar, disparo atrasado, pendentes, permissões no Android, ação principal por medida);
- §12, linha 7b;
- `specs/spikes/android-alarme.md` ("Respostas", "Outras descobertas", "Decisões resultantes" 2 e 3).

**Fora deste plano:**
- APK assinado, aparelho real, restrições de bateria de fabricantes (7c);
- otimização de bateria (`IsIgnoringBatteryOptimizations`): não é pedida;
- o botão "Adiar 15 min" com o tempo no rótulo (sugestão da Fase 6): continua "Adiar";
- virada do dia com o app aberto em primeiro plano: a tela recarrega ao voltar ao app (7a), sem alarme à meia-noite.

## Decisões deste plano (o autor confirma na revisão)

1. **Agendador do Android no Core** (`SystemAlarmReminderScheduler`, porta `ISystemAlarm`), testável sem o workload `android`. O adaptador `AndroidSystemAlarm` só arma e cancela.
2. **Um alarme só, no próximo lembrete.** Sem lembrete futuro (sem metas, todas cumpridas, pausado), o alarme é cancelado. Não há alarme à meia-noite: acordar o celular todo dia sem mostrar nada não vale a pena; a tela Hoje recarrega ao voltar ao app.
3. **Falha ao sincronizar** (banco indisponível, por exemplo): arma uma nova tentativa em 15 min e registra em `Trace`, para os lembretes não pararem até a próxima abertura.
4. **Pendentes não expiram ao abrir o app no Android.** A notificação continua na gaveta com ações que funcionam (o Android recria o processo para o receiver). Continuam valendo as outras regras da §9.6: o próximo disparo da mesma meta, meta cumprida, arquivada, excluída ou pausa retiram a notificação como ignorada.
5. **Sem permissão de alarme exato, os lembretes seguem inexatos** (`SetAndAllowWhileIdle`, que o sistema pode atrasar alguns minutos) e a tela Hoje avisa. Quando a permissão volta, o sistema envia `ACTION_SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED` e o app reagenda exato. `USE_EXACT_ALARM` continua descartada (spike, decisão 3).
6. **Notificação:**
   - canal `lembretes` ("Lembretes", importância alta);
   - uma por disparo, identificada pelo id do registro no log (tag);
   - título e texto do `IReminderFormatter`;
   - botões: ação principal ("Feito", "+1" ou "Iniciar"), "Adiar" e "Ignorar";
   - tocar no corpo abre o app (sem registrar resposta, como o "Abrir" do Linux);
   - arrastar para o lado (ou "Limpar tudo") conta como **Ignorar**, como o X no GNOME;
   - ícone pequeno: silhueta branca do slime; cor de destaque `#9B7BEA`.
7. **"Iniciar" com o cronômetro de outra meta rodando:** o Android 12+ não deixa um receiver de ação abrir uma Activity (restrição de *trampoline*). A notificação é trocada pelo texto "Outro cronômetro está rodando. Toque para abrir o app.", sem botões, e tocar abre o app. O registro continua pendente (como no Linux).
8. **Permissões num aviso, não numa tela de boas-vindas.**
   - Na tela Hoje, enquanto faltar alguma, um aviso explica o porquê e oferece "Permitir". Ele aparece já na primeira abertura, o que cumpre o "fluxo de primeira execução" da §9.9 sem uma tela a mais.
   - "Permitir" pede notificações primeiro (diálogo do sistema; se o Android não mostrar mais o diálogo, abre a página de notificações do app); depois, alarmes exatos (`ACTION_REQUEST_SCHEDULE_EXACT_ALARM`, direto na página do app).
   - Configurações, seção "Lembretes", mostra o estado das duas, cada uma com "Permitir".
9. **Receivers de sistema:** `BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`, `TIME_SET`, `TIMEZONE_CHANGED` e `SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED` reagendam. Lembretes vencidos com o aparelho desligado disparam uma vez, na hora (regra do disparo atrasado).
10. **Adaptadores no head**, não num projeto `SlimeGoals.Platform.Android`: os receivers precisam do container do `AndroidApplication`, e um projeto a mais não ganharia teste (nenhum dos dois compila no `dotnet test`). A §6 da spec é atualizada.
11. **`PlatformCapabilities` do Android:** `Reminders` passa a verdadeiro. Hoje volta a mostrar "Próximos lembretes" e "Pausar lembretes"; Configurações mostra a seção "Lembretes". O aviso "Nada para configurar" fica genérico: "Nada para configurar por enquanto.".
12. **`ReminderHost` também no Android**, com um `IWindowActivator` que não faz nada: ele inicia o agendador quando o app abre, o que recupera os alarmes depois de "Forçar parada". O `ActionReceived` do `AndroidNotificationService` nunca dispara: as ações chegam pelo receiver.

## Global Constraints

- Tudo das Fases 1–7a continua valendo:
  - `TreatWarningsAsErrors`, `Nullable`, versões centralizadas;
  - Core sem dependências (`ArchitectureTests`); App sem referência a Platform.Linux nem ao Android;
  - textos em pt-BR e identificadores em inglês;
  - ViewModels sem tipos visuais, bindings compilados, cores só por `DynamicResource`, alvos de toque ≥ 44 px.
- Textos exatos:
  - aviso no Hoje sem notificações: `"Os lembretes estão desligados: permita as notificações do SlimeGoals."`;
  - aviso no Hoje só sem alarmes exatos: `"Os lembretes podem atrasar alguns minutos: permita alarmes exatos."`;
  - botão: `"Permitir"`;
  - Configurações: `"Notificações"`, `"Alarmes exatos"`, estados `"Permitidas"`/`"Bloqueadas"` e `"Permitidos"`/`"Bloqueados"`;
  - canal: `"Lembretes"`; botões da notificação: rótulo do formatter, `"Adiar"`, `"Ignorar"`;
  - conflito do cronômetro: `"Outro cronômetro está rodando. Toque para abrir o app."`;
  - Configurações vazia: `"Nada para configurar por enquanto."`.
- Nova tentativa depois de falha: `15` min.
- Branch `feat/fase-7b-android-lembretes`. Commits: Conventional Commits com escopo `core`, `app`, `android` ou `specs`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Integração por PR.
- **Testes:** `dotnet test --solution SlimeGoals.slnx` e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, os dois sem avisos.
- Emulador: AVD `slimegoals-api36`, `adb` em `~/Android/Sdk/platform-tools/adb`. Nunca use o banco real do Linux nem mexa no app instalado em `~/.local`.

## Review Focus

1. **Processo morto quando o usuário toca numa ação:** o receiver trata a ação, a notificação some e o próximo alarme fica armado, sem abrir tela. Coberto na Task 1 (`FiredAsync`/`RescheduleAsync`) e na Task 3 (`am kill` no emulador).
2. **Toque numa notificação que já não vale** (respondida em outra, ou de ontem): nada é registrado duas vezes e a notificação sai da gaveta. Coberto na Task 3 (resultado `Stale` cancela a notificação) e pelos testes existentes do `ReminderResponseHandler`.
3. **Pausar com notificações na gaveta:** elas saem como ignoradas e o alarme é cancelado; retomar arma de novo sem rajada. Coberto na Task 1.
4. **Permissão de alarme exato desligada nas configurações do sistema:** o sistema cancela os alarmes; ao voltar ao app o agendador arma de novo (inexato) e o Hoje avisa; quando a permissão volta, o alarme passa a exato. Coberto nas Tasks 2 e 4.
5. **Aparelho reiniciado, app atualizado ou fuso trocado:** o alarme volta a ficar armado, e o que venceu nesse meio-tempo dispara uma vez. Coberto na Task 4 (`adb reboot` no emulador).

---

## Mapa de arquivos

```
src/SlimeGoals.Core/
  Abstractions/ISystemAlarm.cs                        (novo)
  Services/SystemAlarmReminderScheduler.cs            (novo)
src/SlimeGoals.App/
  Hosting/IReminderPermissions.cs                     (novo)
  ViewModels/TodayPageViewModel.cs, SettingsPageViewModel.cs (alterados)
  Views/TodayView.axaml, SettingsView.axaml           (alterados)
src/SlimeGoals.Android/
  Reminders/AndroidSystemAlarm.cs, AndroidNotificationService.cs, ReminderReceivers.cs,
  AndroidReminderPermissions.cs, NoopWindowActivator.cs (novos)
  AndroidApplication.cs, MainActivity.cs, AndroidCompositionRoot.cs, Properties/AndroidManifest.xml (alterados)
  NoopNotificationService.cs                          (removido)
  Resources/drawable-*/ic_notification.png, make-android-icons.sh (novos/alterados)
tools/SlimeGoals.Render/                              (alterado: capacidades e permissões do celular)
tests/SlimeGoals.Core.Tests/Services/SystemAlarmReminderSchedulerTests.cs (novo)
tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs, SettingsPageViewModelTests.cs (alterados)
README.md, specs/Planejamento_v3.md                   (alterados)
```

---

### Task 1: Agendador por alarme do sistema (Core)

**Files:**
- Create: `src/SlimeGoals.Core/Abstractions/ISystemAlarm.cs`, `src/SlimeGoals.Core/Services/SystemAlarmReminderScheduler.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/SystemAlarmReminderSchedulerTests.cs`

**Interfaces:**
- Consumes: `ReminderService.SyncAsync()` (dispara os devidos e devolve o próximo), `DataChangeNotifier`, `TimeProvider`.
- Produces:

```csharp
namespace SlimeGoals.Core.Abstractions;
/// Um único alarme do sistema, que sobrevive ao processo (Android: AlarmManager). Armar substitui o anterior.
public interface ISystemAlarm
{
    void Set(DateTimeOffset at);   // instante no passado: dispara assim que possível
    void Cancel();
}

namespace SlimeGoals.Core.Services;
public sealed class SystemAlarmReminderScheduler(ReminderService reminders, ISystemAlarm alarm,
    DataChangeNotifier changes, TimeProvider time) : IReminderScheduler
{
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);
    public Task StartAsync(CancellationToken ct = default);      // assina Changed (uma vez) e reagenda; NÃO expira pendentes
    public Task RescheduleAsync(CancellationToken ct = default); // serializado; SyncAsync → Set(próximo) ou Cancel()
    public Task FiredAsync(CancellationToken ct = default);      // o alarme tocou: RescheduleAsync e depois NotifyChanged
    public void Dispose();                                       // cancela a assinatura; não mexe no alarme
}
```

Regras: `Changed` só reagenda (nunca avisa, sem laço), como no `ProcessTimerReminderScheduler`. Exceção do `SyncAsync` (fora cancelamento) vai para o `Trace` e arma `agora + RetryAfterFailure`. Depois do `Dispose`, reagendar não faz nada.

- [ ] **Step 1: Write the failing tests** (`FakeSystemAlarm` local grava `ArmedFor: DateTimeOffset?` e `Cancels`; repositórios em memória e `FakeNotificationService` do `SlimeGoals.Testing`; `FakeTimeProvider` em São Paulo, como nos testes do `ReminderService`):
  - `Start_arms_alarm_for_next_reminder`: meta com janela 08:00–22:00 e intervalo 60 min, agora 07:00 → `ArmedFor` = 08:00 local de hoje.
  - `Start_does_not_expire_pending`: um registro pendente de hoje, válido → continua sem resposta, nada retirado.
  - `Fired_shows_due_reminder_arms_next_and_notifies`: agora 08:00 → `FiredAsync` mostra 1 notificação, `ArmedFor` = 09:00 e `Changed` dispara uma vez.
  - `Without_upcoming_reminder_cancels_alarm`: sem metas → `Cancel()` chamado, `ArmedFor` nulo.
  - `Pause_withdraws_pending_and_cancels_alarm`: depois do `StartAsync`, `ReminderPause.PauseAsync()` → o pendente vira `Ignore`, foi retirado e o alarme cancelado.
  - `Data_change_rearms`: depois do `StartAsync` sem metas, criar uma meta pelo `GoalService` com o mesmo notificador → `ArmedFor` passa a ter valor.
  - `Sync_failure_arms_retry`: um `IGoalRepository` que lança → `ArmedFor` = agora + 15 min, sem exceção.
  - `Start_twice_subscribes_once`: dois `StartAsync` e uma mudança → o `SyncAsync` roda uma vez por aviso (conte pelos `Set`).
- [ ] **Step 2: Run to verify they fail** — `dotnet test --project tests/SlimeGoals.Core.Tests`, erro de compilação pelos tipos novos.
- [ ] **Step 3: Implement** os dois tipos.
- [ ] **Step 4: Run tests** — Expected: PASS, 0 avisos.
- [ ] **Step 5: Commit** — `feat(core): agendador de lembretes por alarme do sistema`

---

### Task 2: Permissões de lembrete na interface (App)

**Files:**
- Create: `src/SlimeGoals.App/Hosting/IReminderPermissions.cs`
- Modify: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`, `SettingsPageViewModel.cs`, `Views/TodayView.axaml`, `Views/SettingsView.axaml`, `tools/SlimeGoals.Render/…`
- Test: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`, `SettingsPageViewModelTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace SlimeGoals.App.Hosting;
public sealed record ReminderPermissionStatus(bool Notifications, bool ExactAlarms);
/// Permissões dos lembretes (Android). Pedir não espera a resposta: ela chega quando o app volta (aviso de dados mudados).
public interface IReminderPermissions
{
    ReminderPermissionStatus Get();
    void RequestNotifications();
    void RequestExactAlarms();
}
```

- **`TodayPageViewModel`:** parâmetro opcional novo no fim, `IReminderPermissions? permissions = null`.
  - `public string? PermissionWarning`: nulo sem porta, sem `Reminders` ou com tudo permitido; sem notificações, o texto de notificações; só sem alarmes exatos, o texto de alarmes (Global Constraints).
  - `public bool HasPermissionWarning`.
  - `RequestPermissionCommand`: sem notificações, `RequestNotifications()`; se não, `RequestExactAlarms()`.
  - O estado é lido a cada carga (que já acontece a cada aviso do `DataChangeNotifier` e ao voltar ao app).
- **`SettingsPageViewModel`:** parâmetros opcionais novos no fim, `IReminderPermissions? permissions = null, DataChangeNotifier? changes = null`.
  - `public bool ShowPermissions` (porta presente e `Reminders`).
  - `NotificationsText` ("Permitidas"/"Bloqueadas"), `ExactAlarmsText` ("Permitidos"/"Bloqueados"), `CanRequestNotifications`, `CanRequestExactAlarms` (verdadeiros quando bloqueado), `RequestNotificationsCommand`, `RequestExactAlarmsCommand`.
  - Relê o estado no `Load` e a cada `Changed` (na thread da interface, como o `TodayPageViewModel`).
  - O texto de `IsEmpty` passa a "Nada para configurar por enquanto.".
- **Views:** o aviso fica no topo do Hoje, no mesmo estilo do aviso "Lembretes pausados" (`inline-panel`), com o botão "Permitir". Em Configurações, as duas linhas ficam dentro da seção "Lembretes", abaixo do adiar, cada uma com o estado e o botão "Permitir" (visível só quando bloqueada).
- **Render:** a variante de celular usa `new PlatformCapabilities(true, false, false, false)` e uma porta falsa com as duas permissões negadas, para o aviso aparecer nas imagens.

- [ ] **Step 1: Write the failing tests** (`FakePermissions` local com estado mutável e contadores dos pedidos):
  - `Today_warns_when_notifications_missing`: `(false, false)` → texto de notificações; `RequestPermissionCommand` chama `RequestNotifications` uma vez e `RequestExactAlarms` nenhuma.
  - `Today_warns_when_only_exact_alarms_missing`: `(true, false)` → texto de alarmes; o comando chama `RequestExactAlarms`.
  - `Today_has_no_warning_when_granted_or_without_port`: `(true, true)` → `HasPermissionWarning` falso; sem porta → falso.
  - `Today_rechecks_permissions_on_data_change`: começa `(false, false)`, muda para `(true, true)` e avisa o notificador → aviso some.
  - `Settings_shows_permission_state`: `(false, true)` → `ShowPermissions` verdadeiro, `"Bloqueadas"`, `"Permitidos"`, `CanRequestNotifications` verdadeiro, `CanRequestExactAlarms` falso; os comandos chamam a porta.
  - `Settings_without_port_hides_permissions`: desktop → `ShowPermissions` falso.
  - Ajuste `Sections_follow_capabilities` para o texto novo de `IsEmpty`, se ele for afirmado.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** VMs e Views; ajuste o Render.
- [ ] **Step 4: Run tests + build** e renderize: `dotnet run --project tools/SlimeGoals.Render -- <pasta temporária> phone`. Expected: PASS, 0 avisos; o Hoje do celular mostra o aviso inteiro (sem corte em 412 px) e Configurações mostra as duas linhas.
- [ ] **Step 5: Commit** — `feat(app): aviso e estado das permissões dos lembretes`

---

### Task 3: Alarme, notificação e ações no Android

**Files:**
- Create: `src/SlimeGoals.Android/Reminders/AndroidSystemAlarm.cs`, `AndroidNotificationService.cs`, `ReminderReceivers.cs` (receivers de alarme e de ação), `NoopWindowActivator.cs`
- Modify: `AndroidApplication.cs`, `AndroidCompositionRoot.cs`, `Properties/AndroidManifest.xml`, `make-android-icons.sh`
- Create: `Resources/drawable-{mdpi,hdpi,xhdpi,xxhdpi,xxxhdpi}/ic_notification.png` (24/36/48/72/96 px, silhueta branca do `icon.svg` com transparência)
- Delete: `NoopNotificationService.cs`

**Interfaces:**
- Consumes: `SystemAlarmReminderScheduler` e `ISystemAlarm` (Task 1); `ReminderResponseHandler.HandleAsync(Guid, ReminderAction)` → `ReminderOutcome`; `INotificationService`; `ReminderHost`.
- Produces: `AndroidApplication.Services` (`internal ServiceProvider?`, o container do processo) e o helper usado pelos receivers da Task 4:

```csharp
// Em ReminderReceivers.cs: roda trabalho assíncrono num receiver com GoAsync(), prazo de 8 s, Trace em falha, Finish() sempre.
internal static class ReceiverWork
{
    public static void Run(BroadcastReceiver receiver, Context context, Func<IServiceProvider, Task> work);
}
```

Regras:
- **`AndroidSystemAlarm`:** `PendingIntent.GetBroadcast` para o receiver de alarme, código fixo, `Immutable | UpdateCurrent`. `Set`: API < 31 ou `CanScheduleExactAlarms()` → `SetExactAndAllowWhileIdle(RtcWakeup, …)`; se não, `SetAndAllowWhileIdle`. `Cancel`: `AlarmManager.Cancel` com o mesmo `PendingIntent`.
- **Receiver de alarme** (não exportado): `ReceiverWork.Run` → `SystemAlarmReminderScheduler.FiredAsync()`.
- **`AndroidNotificationService`:**
  - cria o canal (idempotente) antes de cada `ShowAsync`;
  - `Notify(tag: logId.ToString("N"), id: 1, …)`; `WithdrawAsync` faz `Cancel(tag, 1)`;
  - os três botões e o `DeleteIntent` vão para o receiver de ação, com extras `logId` e `action` (`Primary`, `Snooze`, `Ignore`) e um `Data` único por registro e ação (`slimegoals://reminder/<logId>/<action>`), para os `PendingIntent` não se confundirem;
  - `ContentIntent` abre a `MainActivity` (`SingleTop`), com `AutoCancel`;
  - `ActionReceived` nunca dispara (`add {}`/`remove {}`).
- **Receiver de ação** (não exportado): `ReminderResponseHandler.HandleAsync`; `Handled`/`Stale` → retira a notificação; `OpenApp` → troca a notificação pelo texto do conflito do cronômetro, sem botões, tocando abre o app. Depois, `RescheduleAsync()`.
- **Composição:** `PlatformCapabilities(true, false, false, false)`; `ISystemAlarm` → `AndroidSystemAlarm`; `INotificationService` → `AndroidNotificationService`; `SystemAlarmReminderScheduler` como singleton e como `IReminderScheduler` (mesma instância); `IWindowActivator` → `NoopWindowActivator`; `IAppHostedService` → `ReminderHost`. O `ValidateOnBuild` continua.
- **Manifesto:** `POST_NOTIFICATIONS` e `SCHEDULE_EXACT_ALARM`.

- [ ] **Step 1:** Gere os `ic_notification.png` pelo `make-android-icons.sh` (silhueta: o `icon.svg` com todas as cores trocadas por branco, mantendo o alfa) e confira um deles com o Read.
- [ ] **Step 2: Implement** adaptadores, receivers, composição e manifesto; apague o `NoopNotificationService`.
- [ ] **Step 3: Build** — `dotnet build SlimeGoals.Android.slnx …` e `dotnet test --solution SlimeGoals.slnx`. Expected: 0 avisos, tudo passando.
- [ ] **Step 4: Fumaça no emulador** (instale com o comando do README; libere as permissões por `adb shell pm grant dev.alcm.slimegoals android.permission.POST_NOTIFICATIONS` e `adb shell appops set dev.alcm.slimegoals SCHEDULE_EXACT_ALARM allow`). Crie uma meta de quantidade com intervalo de 1 min pela interface e anote cada resultado no relatório:
  - com o app aberto, o lembrete aparece; `adb shell dumpsys alarm | grep -A3 slimegoals` mostra o próximo alarme;
  - `adb shell am kill dev.alcm.slimegoals` (app em segundo plano): o próximo lembrete aparece; tocar em "+1" registra (confira reabrindo o app) sem abrir tela;
  - com o processo morto de novo, "Adiar" e "Ignorar" funcionam (o próximo alarme muda conforme a regra); arrastar a notificação conta como ignorada;
  - meta de tempo com o cronômetro de outra meta rodando: "Iniciar" mostra o texto do conflito.
- [ ] **Step 5: Commit** — `feat(android): lembretes com AlarmManager e ações na notificação`

---

### Task 4: Permissões e eventos do sistema no Android

**Files:**
- Create: `src/SlimeGoals.Android/Reminders/AndroidReminderPermissions.cs`
- Modify: `Reminders/ReminderReceivers.cs` (receiver de eventos do sistema), `MainActivity.cs`, `AndroidCompositionRoot.cs`, `Properties/AndroidManifest.xml`

**Interfaces:**
- Consumes: `IReminderPermissions`/`ReminderPermissionStatus` (Task 2); `ReceiverWork.Run` e `SystemAlarmReminderScheduler.RescheduleAsync` (Tasks 1 e 3).

Regras:
- **`AndroidReminderPermissions`:**
  - `Notifications` = `AreNotificationsEnabled()` e o canal `lembretes`, se já existir, com importância diferente de `None`;
  - `ExactAlarms` = API < 31 ou `CanScheduleExactAlarms()`;
  - `RequestNotifications`: na API ≥ 33, pede `POST_NOTIFICATIONS` pela Activity atual na primeira vez (marca em `SharedPreferences`) e quando `ShouldShowRequestPermissionRationale` for verdadeiro; nos outros casos abre `Settings.ActionAppNotificationSettings` do app;
  - `RequestExactAlarms`: abre `Settings.ActionRequestScheduleExactAlarm` com `package:dev.alcm.slimegoals`;
  - a `MainActivity` se registra como Activity atual em `OnCreate`/`OnDestroy`; sem Activity, os pedidos não fazem nada.
- **`MainActivity`:** `OnRequestPermissionsResult` avisa o `DataChangeNotifier` (a tela relê o estado). O `OnResume` da 7a já avisa ao voltar das configurações do sistema.
- **Receiver de eventos do sistema** (exportado, só com `intent-filter` de ações protegidas do sistema): `BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`, `TIME_SET`, `TIMEZONE_CHANGED`, `android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED` → `ReceiverWork.Run` → `RescheduleAsync()`.
- **Manifesto:** `RECEIVE_BOOT_COMPLETED`.
- **Composição:** `IReminderPermissions` → `AndroidReminderPermissions`.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build** — os dois comandos das Global Constraints. Expected: 0 avisos.
- [ ] **Step 3: Fumaça no emulador** (anote cada resultado no relatório):
  - desinstale e instale de novo: o Hoje mostra o aviso de notificações; "Permitir" abre o diálogo; aceitando, o aviso passa ao de alarmes exatos; "Permitir" abre a página "Alarmes e lembretes"; ligando e voltando, o aviso some; Configurações mostra as duas como permitidas;
  - `adb shell appops set dev.alcm.slimegoals SCHEDULE_EXACT_ALARM deny` e volte ao app: aviso de alarmes, e o `dumpsys alarm` mostra o alarme armado de novo; devolvendo (`allow`), o alarme é rearmado (veja o receiver no `logcat`);
  - com uma meta de intervalo de 1 min, `adb reboot`; depois do boot, sem abrir o app, o lembrete aparece em até ~1 min e "+1" funciona.
- [ ] **Step 4: Commit** — `feat(android): permissões e reagendamento após boot`

---

### Task 5: Spec e README

**Files:**
- Modify: `specs/Planejamento_v3.md`, `README.md`

- [ ] **Step 1:** README, seção "Rodar no Android": tira "Nesta fase o Android ainda não tem lembretes"; explica as duas permissões e os comandos `adb` úteis (`pm grant`, `appops`, `dumpsys alarm`, `am kill`).
- [ ] **Step 2:** Spec:
  - **§6:** os adaptadores ficam em `SlimeGoals.Android/Reminders` (não há `Platform.Android`), com o `SystemAlarmReminderScheduler` no Core; ajuste a árvore e o diagrama.
  - **§9.4, 9.6 e 9.9:** sem alarme à meia-noite no Android; pendentes não expiram ao abrir; inexato sem a permissão; o aviso no Hoje como fluxo de primeira execução.
  - **§12, linha 7b:** "Implementado (verificação manual pelo autor pendente)", com o resumo.
  - **§14:** a linha do Android ganha o resultado do receiver de boot.
  - **§15:** novo bloco "Decisões da Fase 7b (03/11/2026)" com as decisões 1–12, uma linha cada.
- [ ] **Step 3: Commit** — `docs(specs): lembretes no Android da Fase 7b`

---

## Verificação manual (autor, depois do PR aberto)

1. **Instalar do zero** (desinstale antes): o aviso do Hoje pede as permissões; conceda pelo "Permitir".
2. **Lembrete com o app aberto:** crie uma meta de cada medida com intervalo curto; a notificação aparece com "Feito"/"+1"/"Iniciar", "Adiar" e "Ignorar".
3. **App fechado:** remova o app dos recentes; espere o lembrete; teste as três ações e o arrastar; reabra e confira os registros e os "Próximos lembretes".
4. **Reiniciar o emulador** com uma meta ativa: o lembrete aparece sem abrir o app.
5. **Pausar lembretes** com uma notificação na gaveta: ela some; retomar volta a lembrar depois do intervalo.
6. **No Linux, nada muda:** lembretes, "Abrir" pela notificação e Configurações como antes.
