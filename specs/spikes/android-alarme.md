# Spike: Avalonia no Android com alarme exato e ação na notificação

> Fase 0, Task 4 · executado em 01/11/2026 · código descartável na branch local `spike/android-alarme` (não mesclada)

**Ambiente:** CachyOS, SDK .NET 10.0.114 do pacote da distro (RID `arch-x64`), workload `android` 36.1.118, OpenJDK 21 (`jdk21-openjdk`), Android SDK em `~/Android/Sdk` (platform 36, build-tools 36.0.0), emulador 37.2.13 com imagem `system-images;android-36;google_apis;x86_64` (Android 16, perfil Pixel 7, KVM). Avalonia 12.1.4. Build **Debug**.

**Método:** app gerado com `dotnet new avalonia.xplat`, mantendo só o projeto compartilhado e o head Android. Botões para pedir permissões e agendar um alarme em 15 s ou 1 min (`SetExactAndAllowWhileIdle`, `RTC_WAKEUP`). Um `AlarmReceiver` publica a notificação com a ação **Feito**. Um `ActionReceiver` simula o `ReminderResponseHandler` e grava em `FilesDir/actions.log`. Cada linha do log registra o PID e a idade do processo, e `Application.OnCreate` também escreve no log, para mostrar quando o Android cria um processo novo. O autor tocou na interface do emulador; o `adb` controlou o Doze, a tela e o encerramento do processo.

## Respostas

| # | Pergunta | Resposta | Evidência |
|---|---|---|---|
| 1 | O toolchain funciona no CachyOS com o workload `android` do .NET 10? | **Sim, com o SDK da distro.** `sudo dotnet workload install android` funcionou no SDK *source-build*; o plano B (SDK da Microsoft em `~/.dotnet`) **não** foi necessário. Build sem avisos. | Comandos abaixo |
| 2 | O Avalonia abre em < 2 s? | **Sim:** abertura a frio de **1,0–1,2 s** (`am start -W`, 3 medições, Debug, emulador). O processo leva ~130 ms até `Application.OnCreate` e ~0,5 s até a `Activity`. | `TotalTime: 1244 / 991 / 1014` |
| 3 | O alarme exato dispara com o app fechado e mostra a notificação com ação? | **Sim.** Removido dos recentes, o processo morre, mas o alarme continua registrado no sistema (`dumpsys alarm`: `exactAllowReason=permission`). No horário exato, o Android **cria um processo novo** só para o `AlarmReceiver`, que publica a notificação. Em **Doze profundo forçado** (`IDLE`, tela apagada), o alarme disparou no segundo previsto. | Testes B e C |
| 4 | O receiver da ação roda com o processo morto e grava no armazenamento privado? | **Sim.** Com o processo encerrado **depois** de a notificação aparecer (`am kill`), o toque em **Feito** criou outro processo, e o `ActionReceiver` gravou a ação em ~0,5 s, sem abrir nenhuma tela. | Teste C (pid 7481) |
| 5 | Quais permissões? E o Doze? | No Android 16, um app recém-instalado vem com **notificações negadas** (`POST_NOTIFICATIONS`, pedido em diálogo) e **alarme exato negado** (`SCHEDULE_EXACT_ALARM`, liberado numa tela de configurações). O intent `ACTION_REQUEST_SCHEDULE_EXACT_ALARM` com `package:` abre **direto a página do app** ("Alarms & reminders", com a chave). Com as duas concedidas, `SetExactAndAllowWhileIdle` atravessou o Doze. | Status da tela; `dumpsys`/`appops` |

### Linha do tempo dos testes (`actions.log`)

```
Teste A — app aberto
17:40:55 pid=5326 AGENDADO +15s
17:41:10 pid=5326 ALARME disparou (notificações habilitadas: True)
17:41:24 pid=5326 AÇÃO recebida: done

Teste B — app removido dos recentes (processo morto às 17:42:04)
17:41:44 pid=5326 AGENDADO +60s
17:42:44 pid=6752 idadeProc=122ms PROCESSO iniciado (Application.OnCreate)
17:42:45 pid=6752 idadeProc=530ms ALARME disparou
17:43:00 pid=6752 AÇÃO recebida: done

Teste C — Doze profundo forçado + processo morto entre a notificação e o toque
17:43:55 pid=6752 AGENDADO +60s
(17:44:03 processo morto; bateria "desconectada"; tela apagada; deviceidle force-idle → IDLE)
17:44:55 pid=7109 idadeProc=114ms PROCESSO iniciado (Application.OnCreate)
17:44:56 pid=7109 idadeProc=403ms ALARME disparou
(17:45:03 am kill: processo morto com a notificação na tela)
17:46:26 pid=7481 idadeProc=128ms PROCESSO iniciado (Application.OnCreate)
17:46:27 pid=7481 idadeProc=500ms AÇÃO recebida: done
```

## Outras descobertas

- **Ciclo de vida do Avalonia 12 no Android:** o head usa `IActivityApplicationLifetime` com `MainViewFactory`, e não `ISingleViewApplicationLifetime`. A `App.OnFrameworkInitializationCompleted` do SlimeGoals trata só `IClassicDesktopStyleApplicationLifetime` e `ISingleViewApplicationLifetime`, então precisa de um terceiro ramo na Fase 7a.
- **A `Application` do Avalonia roda também quando o processo nasce só para um receiver.** Há ~300–400 ms entre `Application.OnCreate` e o receiver, numa build Debug. Fica bem abaixo do limite de ~10 s de um `BroadcastReceiver`, mas o caminho do receiver deve continuar enxuto: abrir o SQLite e chamar o `ReminderResponseHandler`, sem montar a UI. Para trabalho assíncrono no receiver, usar `GoAsync()`.
- **Desligar a permissão de alarme exato cancela os alarmes já agendados** (aviso na própria tela de configurações). O app precisa reagendar quando a permissão volta (`ACTION_SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED`) e checar `CanScheduleExactAlarms()` antes de agendar.
- **Notificações negadas não geram erro:** `Notify` é ignorado em silêncio. O app deve consultar `AreNotificationsEnabled()` e avisar na tela Hoje.
- **Isolamento da pasta do spike:** o `Directory.Build.props` da raiz (com `TreatWarningsAsErrors` e `net10.0`) é herdado por qualquer projeto abaixo dele. O spike precisou de um `Directory.Build.props` próprio. Na Fase 7a, o head Android herda a raiz normalmente e só sobrescreve `TargetFramework` com `net10.0-android`.

## O que este spike não cobriu

- **Restrições de bateria de fabricantes** (Samsung, Xiaomi etc.): o emulador usa Android puro. Validar num aparelho real na Fase 7b/7c.
- **"Forçar parada"** nas configurações do app: no Android, isso cancela todos os alarmes até o app ser aberto de novo. É comportamento esperado do sistema e não foi testado aqui.
- **Receiver de boot** (`BOOT_COMPLETED`) e build **Release/AOT**: ficam para a Fase 7b/7c.
- **Versões antigas do Android:** só a API 36 foi exercitada.

## Decisões resultantes

1. **Toolchain (Fase 7):** SDK .NET da distro + `sudo dotnet workload install android`; JDK 21 do `pacman`; Android SDK em `~/Android/Sdk`, instalado pelo MSBuild (comandos abaixo).
2. **Lembretes no Android (Fase 7b):** `AlarmManager.SetExactAndAllowWhileIdle` com `RTC_WAKEUP`, um alarme por vez (spec §9). As ações chegam a um `BroadcastReceiver` não exportado, que chama o `ReminderResponseHandler` direto, sem UI, como a spec previa.
3. **Permissões (Fase 7b):** pedir `POST_NOTIFICATIONS` e abrir `ACTION_REQUEST_SCHEDULE_EXACT_ALARM` num fluxo de primeira execução, explicando o porquê. Exibir o estado das duas na tela de Configurações, reagendar quando a permissão de alarme exato voltar e avisar na tela Hoje se alguma faltar. `USE_EXACT_ALARM`, que é concedida automaticamente, fica descartada: a política da Play Store a reserva a apps de despertador e agenda.
4. **Versão mínima sugerida (pendência da Fase 7c): Android 8.0, API 26.** Canais de notificação são obrigatórios a partir dela, e o código do spike (`Notification.Builder(context, canal)`) já assume API 26. Isso evita caminhos de compatibilidade, cobrindo a quase totalidade dos aparelhos em uso. A confirmar num aparelho antigo, se houver.
5. **Avalonia no Android:** desempenho de abertura aprovado; o risco "suporte mobile menos maduro" da spec §14 está mitigado para o MVP.

## Comandos que funcionaram

```bash
# JDK e workload (terminal do autor, com sudo)
sudo pacman -S jdk21-openjdk
sudo dotnet workload install android

# Android SDK (sem sudo), a partir de um projeto net10.0-android
dotnet build <Projeto.Android> -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk \
  -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk -p:AcceptAndroidSDKLicenses=True

# Emulador
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk
S=$HOME/Android/Sdk
yes | $S/cmdline-tools/latest/bin/sdkmanager --sdk_root=$S "emulator" "system-images;android-36;google_apis;x86_64"
echo no | $S/cmdline-tools/latest/bin/avdmanager create avd -n slimegoals-api36 \
  -k "system-images;android-36;google_apis;x86_64" -d pixel_7
$S/emulator/emulator -avd slimegoals-api36 -no-boot-anim -no-audio &

# Instalar e medir a abertura
dotnet build <Projeto.Android> -t:Install -f net10.0-android \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk
adb shell am start -W -n <pacote>/<atividade>

# Doze e diagnóstico
adb shell dumpsys battery unplug && adb shell input keyevent KEYCODE_SLEEP
adb shell dumpsys deviceidle force-idle      # depois: deviceidle unforce; battery reset
adb shell dumpsys alarm | grep <pacote>
adb shell run-as <pacote> cat files/actions.log
adb shell am kill <pacote>                   # mata como o sistema faria (não cancela alarmes)
```
