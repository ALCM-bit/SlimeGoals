# Android

O head Android (`src/SlimeGoals.Android`, pacote `dev.alcm.slimegoals`) fica fora da `SlimeGoals.slnx`, e o `dotnet test` do dia a dia não exige o workload `android`. Ele entra na `SlimeGoals.Android.slnx`. O app exige Android 8.0 ou mais novo (API 26).

## Pré-requisitos

- O workload `android` do .NET (`dotnet workload install android`);
- JDK 21, com padrão em `/usr/lib/jvm/java-21-openjdk`;
- Android SDK em `~/Android/Sdk`;
- para o emulador, o AVD `slimegoals-api36` (Android 16).

## Rodar no emulador

```bash
# compilar
dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk

# iniciar o emulador (em outro terminal)
~/Android/Sdk/emulator/emulator -avd slimegoals-api36

# instalar o APK Debug no emulador
dotnet build src/SlimeGoals.Android -t:Install -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk

# abrir o app (ou toque no ícone do slime)
~/Android/Sdk/platform-tools/adb shell monkey -p dev.alcm.slimegoals 1
```

O APK Debug é assinado com a chave de debug, por isso é só para o emulador: no celular, ele não instala por cima do app assinado com a sua chave. Ele também aceita o servidor de desenvolvimento em texto puro no loopback (veja [Sincronização](sincronizacao.md#no-android)).

Os dados ficam no armazenamento privado do app (`<FilesDir>/slimegoals.db`), e desinstalar o app apaga tudo. O app não entra no backup do Android nem na transferência entre aparelhos: sem conta, as metas ficam só neste celular. Com conta, elas vão cifradas para o servidor (veja [Sincronização](sincronizacao.md)).

Para conferir os layouts sem emulador, use a ferramenta de telas em PNG (veja [Desenvolvimento](desenvolvimento.md#conferir-as-telas-sem-abrir-o-app)).

### Lembretes e permissões

O app precisa de duas permissões, que o Android 16 traz negadas num app recém-instalado: notificações e alarmes exatos. O aviso na tela Hoje oferece "Permitir" para as duas, e Configurações mostra o estado de cada uma. Sem alarmes exatos, os lembretes seguem, mas podem atrasar alguns minutos.

Comandos `adb` úteis (`adb` em `~/Android/Sdk/platform-tools`):

```bash
# notificações
adb shell pm grant dev.alcm.slimegoals android.permission.POST_NOTIFICATIONS

# alarmes exatos (use --uid; a forma por pacote não tem efeito na API 36)
adb shell appops set --uid dev.alcm.slimegoals SCHEDULE_EXACT_ALARM allow   # ou deny

# alarme armado
adb shell dumpsys alarm | grep -A3 slimegoals

# encerrar o processo como o sistema faz (o alarme continua; force-stop o cancela)
adb shell am kill dev.alcm.slimegoals
```

## Instalar no celular

O APK de distribuição é assinado com uma chave sua, que fica fora do repositório. Ele traz as arquiteturas arm64 (celulares) e x64 (emulador) e tem a conta de sincronização, com a permissão `INTERNET` e sem tráfego em texto puro: só fala `https://`.

1. **Crie a chave uma vez** e guarde uma cópia dela num lugar seguro:

   ```bash
   packaging/android/make-keystore.sh
   ```

   O script pede uma senha e grava a chave em `~/.android-keystores/slimegoals.keystore` (ou onde `SLIMEGOALS_KEYSTORE` apontar). Ele não sobrescreve uma chave que já existe. **Perder a chave, ou a senha, impede atualizar o app instalado.**

2. **Gere o APK:**

   ```bash
   packaging/android/build.sh
   ```

   O script pede a senha da chave, que não fica no histórico do shell. Para uma chave em outro lugar, aponte `SLIMEGOALS_KEYSTORE`; sem terminal (no CI, por exemplo), defina `SLIMEGOALS_KEYSTORE_PASS`. O SDK do Android e o JDK vêm de `ANDROID_HOME` e `JAVA_HOME`, com padrão `~/Android/Sdk` e `/usr/lib/jvm/java-21-openjdk`.

   O resultado é `artifacts/slimegoals-0.13.1-android.apk`. Com `--dev`, o script gera `artifacts/slimegoals-0.13.1-android-dev.apk`, com o servidor de desenvolvimento em texto puro no loopback e o receiver de vetores (veja [Sincronização](sincronizacao.md#no-android)).

3. **Instale no celular:** com a depuração USB ligada, rode `adb install artifacts/slimegoals-0.13.1-android.apk`. Outra opção é copiar o APK para o aparelho e abri-lo, permitindo "instalar apps desconhecidos" para o app que o abriu.

4. **Primeira abertura:** no aviso do Hoje, toque em "Permitir" (notificações e alarmes exatos). Depois, em Configurações → Bateria, toque em "Liberar" e aceite. Sem isso, o sistema pode adiar ou suspender os lembretes com o celular parado. Liberar a bateria faz "Alarmes exatos" aparecer como permitido, porque o Android trata assim os apps sem restrição de bateria. Se você tirar o app dessa isenção, o bloqueio volta.

5. **No Samsung (One UI)**, confira também:
   - Configurações → Aplicativos → SlimeGoals → Bateria → "Sem restrições";
   - Configurações → Bateria → Limites de uso em segundo plano: o app não pode estar em "Apps em suspensão" nem em "Apps em suspensão profunda".

## Atualizar

Gere o APK de novo com o mesmo comando e instale com `adb install -r artifacts/slimegoals-<versão>-android.apk`. Os dados ficam. **Com outra chave, só desinstalando o app, o que apaga os dados.**

## Notificações na tela de bloqueio

Os lembretes e o cronômetro têm uma versão para a tela de bloqueio que não mostra a meta nem a tarefa: o lembrete aparece como "Lembrete: Desbloqueie para ver a meta.", com os mesmos botões, e o cronômetro aparece só com "Cronômetro" (ou a fase do Pomodoro) e o tempo. O aviso de fim de bloco do Pomodoro já só mostra a fase e aparece sempre inteiro.

Quem escolhe entre a versão escondida e a inteira é o Android, não o app. Para esconder a meta, ligue em Configurações → Notificações a opção de ocultar o conteúdo sensível na tela de bloqueio (na Samsung, "Notificações na tela de bloqueio" → "Ocultar conteúdo"). Ela vale para todos os apps. Com o sistema mostrando todo o conteúdo, a tela de bloqueio mostra a notificação inteira, com o título da meta.
