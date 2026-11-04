# SlimeGoals

Gestor de metas pessoais com lembretes, para **Linux (GNOME)** e **Android**.
Você cadastra hábitos diários ou metas semanais, recebe lembretes até cumpri-las,
registra o progresso com um check-in e acompanha relatórios semanais e mensais. Funciona offline,
com os dados guardados localmente em SQLite.

![Tela Hoje no desktop, com o slime e as metas do dia](docs/images/hoje-desktop.png)

> **Status:** em desenvolvimento. O app já funciona no Linux, com pacote de instalação, e no Android, por enquanto
> só em APK Debug. A próxima etapa é a Fase 7c: APK assinado para instalar num celular de verdade. Veja o [roteiro](#roteiro).

## Funcionalidades do MVP

- **Dois tipos de meta:** diária (alvo por dia nos dias escolhidos) e semanal (alvo por semana, de segunda a domingo)
- **Três medidas por meta:** conclusão, quantidade ou tempo (com cronômetro)
- **Lembretes** dentro de uma janela de horário, com a ação da medida (**Feito**, **+1** ou **Iniciar**), **Adiar** e **Ignorar** direto na notificação do sistema
- **Tela Hoje** com o progresso do período atual e registro rápido
- **Slime** no Hoje, que muda de pose e de frase conforme o andamento do dia
- **Relatórios** semanais e mensais, com sequências em dias ou semanas
- **Arquivamento** sem perda de histórico; tema escuro roxo

## Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10 (LTS) |
| UI | Avalonia 12 + CommunityToolkit.Mvvm |
| Dados | SQLite (Microsoft.Data.Sqlite + Dapper), migrações por `PRAGMA user_version` |
| Testes | xUnit v3 (Microsoft Testing Platform) + AwesomeAssertions + `FakeTimeProvider` |

## Arquitetura

Hexagonal pragmática: o **Core** concentra domínio e regras e não depende de nada. Os demais
projetos implementam suas portas, e a composição (injeção de dependência) acontece só nos *heads*.

```
SlimeGoals.Desktop   SlimeGoals.Android
         └─────────┬─────────┘
                   ▼
            SlimeGoals.App          Views + ViewModels Avalonia
                   │
                   ▼
            SlimeGoals.Core         domínio, serviços, interfaces
                   ▲
       ┌───────────┴──────────────┐
SlimeGoals.Infrastructure   SlimeGoals.Platform.Linux
(SQLite, repositórios)      (caminhos XDG, notificações D-Bus, agendador,
                             autostart, instância única)
```

O head Android implementa as portas de lembretes direto no projeto (`AlarmManager`, notificações, permissões).
Um teste de arquitetura garante que o Core não referencia UI, banco ou plataforma.

```
src/
  SlimeGoals.Core/             Domain/, Services/, Validation/, Abstractions/
  SlimeGoals.Infrastructure/   Data/ (conexão, migrações SQL, repositórios)
  SlimeGoals.Platform.Linux/   Notifications/, Scheduling/, Autostart/, Power/, SingleInstance/
  SlimeGoals.App/              Views/, ViewModels/, Styles/ (tokens do tema), Assets/Fonts/
  SlimeGoals.Desktop/          Program.cs, CompositionRoot.cs
  SlimeGoals.Android/          MainActivity, AndroidCompositionRoot, Reminders/
tests/                         testes do Core, Infrastructure, App e Platform.Linux; SlimeGoals.Testing (falsos)
tools/SlimeGoals.Render/       grava PNGs das telas sem abrir o app
packaging/linux/               pacote .tar.gz e instalador
specs/                         especificação, protótipos, planos e conclusões de spikes
```

## Como rodar

**Pré-requisito:** SDK do .NET 10 (`dotnet --version` ≥ 10.0.113; no Arch/CachyOS: `sudo pacman -S dotnet-sdk`).

```bash
git clone https://github.com/ALCM-bit/SlimeGoals.git
cd SlimeGoals

dotnet build SlimeGoals.slnx
dotnet test --solution SlimeGoals.slnx
dotnet run --project src/SlimeGoals.Desktop
```

Os dados ficam em `$XDG_DATA_HOME/SlimeGoals/slimegoals.db` (padrão: `~/.local/share/SlimeGoals/slimegoals.db`).
Para testar sem tocar nos seus dados, aponte para outra pasta:

```bash
XDG_DATA_HOME=/tmp/slimegoals-dev dotnet run --project src/SlimeGoals.Desktop
```

## Rodar no Android (desenvolvimento)

O head Android fica fora de `SlimeGoals.slnx` (o `dotnet test` do dia a dia não exige o workload `android`); ele entra em `SlimeGoals.Android.slnx`.

**Pré-requisitos** (os do spike): workload `android` do .NET, JDK 21, Android SDK em `~/Android/Sdk` e o AVD `slimegoals-api36`.

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

Os dados ficam no armazenamento privado do app (`<FilesDir>/slimegoals.db`); desinstalar o app apaga tudo.

**Lembretes:** o app precisa de duas permissões, que o Android 16 traz negadas num app recém-instalado: notificações e alarmes exatos. O aviso na tela Hoje oferece "Permitir" para as duas (Configurações mostra o estado de cada uma). Sem alarmes exatos os lembretes seguem, mas podem atrasar alguns minutos.

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

Para conferir os layouts sem emulador, `dotnet run --project tools/SlimeGoals.Render -- /tmp/render [phone|desktop|all]` grava PNGs de cada página em 412×915 (celular) e 1100×720 (desktop).

## Instalar no Linux

O pacote é um `.tar.gz` *self-contained* (não precisa do .NET instalado) para linux-x64. A instalação é só no seu usuário, sem root.

```bash
# gerar o pacote (ou baixar o .tar.gz pronto)
bash packaging/linux/build.sh
tar -xzf artifacts/slimegoals-0.6.0-linux-x64.tar.gz
cd slimegoals-0.6.0
./install.sh
```

Isso instala o app em `~/.local/opt/slimegoals/`, o link em `~/.local/bin/slimegoals`, o atalho em `~/.local/share/applications/slimegoals.desktop` e os ícones em `~/.local/share/icons/hicolor/`. Rodar `./install.sh` de novo substitui a versão instalada.

Para desinstalar, rode `./uninstall.sh` de dentro da pasta extraída. Ele não apaga seus dados: o banco de dados fica em `~/.local/share/SlimeGoals`. A entrada de "iniciar com o sistema" (`~/.config/autostart/slimegoals.desktop`) também fica; apague-a à mão se quiser.

## Documentação

- [Especificação (v1.4)](specs/Planejamento_v3.md): requisitos, modelo de dados, regras de lembrete e design system
- [Protótipos visuais](specs/design/): desktop (`v4-bento-desktop.html`) e mobile (`v3-*-mobile.html`)
- [Planos de implementação](specs/plans/)

## Como contribuir

- **Feature Branch Workflow:** uma branch por funcionalidade (`feat/…`, `fix/…`, `docs/…`), integrada na `main` por Pull Request com *merge commit*
- **[Conventional Commits](https://www.conventionalcommits.org/pt-br/)** em português, com escopos por camada: `core`, `infra`, `app`, `linux`, `desktop`, `android`, `specs`, `plans`, `build`
- **TDD:** todo comportamento novo começa por um teste que falha; a `main` sempre compila sem warnings e passa nos testes

## Roteiro

| Fase | Entrega | Status |
|---|---|---|
| 0 | Fundação, tema, navegação e spikes (notificações GNOME, Android) | ✅ |
| 1 | Domínio e persistência de metas | ✅ |
| 2 | Telas de metas | ✅ |
| 3 | Medidas, registros e tela Hoje | ✅ |
| 4 | Lembretes (planejador, agendador, notificação nativa, segundo plano) | ✅ |
| 5 | Relatórios | ✅ |
| 6 | Acabamento e empacotamento Linux | ✅ |
| 7 | Android (7a interface ✅ · 7b lembretes ✅ · 7c distribuição ⏳) | ⏳ |
| 8 | Mascote slime | ✅ |

## Licença

[PolyForm Noncommercial 1.0.0](LICENSE.md): uso **pessoal, educacional, de pesquisa e por organizações sem fins lucrativos** é permitido,
inclusive modificar e redistribuir com a licença. **Uso comercial não é permitido.**
Por restringir o uso comercial, este projeto é *source-available*, não *open source* no sentido da OSI.

Componentes de terceiros mantêm suas licenças: veja [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
