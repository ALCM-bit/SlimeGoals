# Desenvolvimento

Como compilar, testar e conferir as telas no Linux. O Android tem o próprio guia: [Android](android.md).

## Pré-requisitos

- **SDK do .NET 10**, versão 10.0.113 ou mais nova (o `global.json` fixa a versão mínima). No Arch/CachyOS: `sudo pacman -S dotnet-sdk`.
- **Docker**, para os testes do servidor, que sobem um PostgreSQL com Testcontainers.
- **`dbus-daemon` e `gnome-keyring-daemon`**, para os testes do chaveiro. Sem eles, esses testes são ignorados, não reprovados.

## Compilar e testar

```bash
dotnet build SlimeGoals.slnx
dotnet test --solution SlimeGoals.slnx
```

- **Um aviso quebra o build.** O `Directory.Build.props` liga `TreatWarningsAsErrors` em todos os projetos.
- **Os testes usam xUnit v3 no Microsoft Testing Platform**, configurado no `global.json`. Para rodar um projeto só, ou só uma classe:

  ```bash
  dotnet test --project tests/SlimeGoals.Core.Tests
  dotnet test --project tests/SlimeGoals.Core.Tests --filter-class "*ArchitectureTests"
  ```

- **O head Android fica fora da `SlimeGoals.slnx`.** O dia a dia não precisa do workload `android`. O Android compila pela `SlimeGoals.Android.slnx`.

### Testes que dependem de algo de fora

**Docker.** Os testes de integração do servidor sobem um PostgreSQL de verdade com Testcontainers, por isso o `dotnet test` precisa do Docker rodando. O Testcontainers pode falhar ao subir o contêiner auxiliar (ryuk), por exemplo por uma credencial antiga do Docker. Nesse caso, baixe as imagens à mão e rode de novo:

```bash
docker pull postgres:18-alpine
docker pull testcontainers/ryuk:0.14.0   # use a versão que o erro indicar
```

**Chaveiro.** Os testes do cofre do Linux sobem um barramento D-Bus e um gnome-keyring isolados, num diretório temporário, sem tocar no seu chaveiro.

## Rodar sem tocar nos seus dados

O app instalado guarda o banco em `$XDG_DATA_HOME/SlimeGoals/slimegoals.db` (padrão: `~/.local/share/SlimeGoals/slimegoals.db`). Em desenvolvimento, aponte sempre para outra pasta:

```bash
XDG_DATA_HOME=/tmp/slimegoals-dev/data XDG_CONFIG_HOME=/tmp/slimegoals-dev/config \
  dotnet run --project src/SlimeGoals.Desktop
```

O `XDG_CONFIG_HOME` próprio impede que ligar "Iniciar com o sistema" grave a entrada de autostart de verdade (`~/.config/autostart/slimegoals.desktop`). Cada pasta de dados tem a sua instância, então essa cópia abre mesmo com o app instalado aberto.

Em Debug, Configurações traz a seção de conta. Veja [Sincronização](sincronizacao.md).

## Conferir as telas sem abrir o app

```bash
dotnet run --project tools/SlimeGoals.Render -- /tmp/render all
```

A ferramenta grava um PNG por página, no tamanho do celular (412×915) e no do desktop (1100×720). Ela usa um banco temporário, apagado no fim, e nunca toca o banco do app. O segundo argumento escolhe o que gravar:

| Modo | O que grava |
|---|---|
| `all` (padrão), `phone`, `desktop` | as páginas nos dois tamanhos, ou só num |
| `narrow` | as páginas a 320 e 360 px e os estados mais apertados do Hoje e da aba Tempo |
| `time` | só a aba Tempo (Pomodoro), em quatro tamanhos |
| `account` | a seção de conta, sobre uma conta falsa e sem rede |
| `sync-warning` | a tela Hoje com o aviso de sincronização parada (320, 360, 412 e 1100 de largura) |
| `mini` | a janelinha do cronômetro |
| `mascot` | as quatro poses do slime |

A lista dos arquivos de cada modo está em [tools/SlimeGoals.Render/README.md](../tools/SlimeGoals.Render/README.md). Numa mudança visual, confira mais de uma largura: os textos longos de tempo costumam quebrar primeiro a 320 px.

## CI

O GitHub Actions (`.github/workflows/ci.yml`) roda a cada Pull Request e a cada commit na `main`. Ele compila em Release, com avisos tratados como erro, e roda todos os testes no Ubuntu 24.04, com Docker, `dbus` e `gnome-keyring`. O fluxo só tem permissão de leitura, e as ações ficam fixadas pelo commit.

O Dependabot (`.github/dependabot.yml`) propõe toda semana as atualizações dos pacotes NuGet, das ações do GitHub e das imagens Docker do servidor e do Compose.

## Subir a versão

A versão fica em `<Version>`, no `Directory.Build.props`. Dela saem o nome do pacote do Linux, o APK (o `versionCode` é calculado dela: 0.13.0 vira 1300) e a versão na tela Sobre. Ao subir, troque também a versão escrita nos comandos dos guias:

```bash
grep -rn '0\.12\.1' README.md docs/
```

## Como contribuir

- **Uma branch por mudança** (`feat/…`, `fix/…`, `docs/…`), integrada na `main` por Pull Request com *merge commit*. A `main` é protegida.
- **[Conventional Commits](https://www.conventionalcommits.org/pt-br/) em português**, com escopo por camada: `core`, `infra`, `app`, `linux`, `desktop`, `android`, `sync`, `server`, `specs`, `plans`, `build`.
- **TDD:** todo comportamento novo começa por um teste que falha. A `main` sempre compila sem avisos e passa nos testes.
- **Specs antes do código:** uma funcionalidade nova começa por uma especificação e um plano em [`specs/`](../specs/). Leia a especificação de uma funcionalidade antes de mexer nela.
