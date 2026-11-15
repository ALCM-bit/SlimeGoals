# SlimeGoals

Gestor de metas pessoais com lembretes para Linux (GNOME) e Android. .NET 10, Avalonia 12 + CommunityToolkit.Mvvm, SQLite (Microsoft.Data.Sqlite + Dapper). O `README.md` é a fonte da verdade dos comandos; se um daqui falhar, confira lá antes de improvisar.

## Arquitetura

Hexagonal. `SlimeGoals.Core` (domínio, serviços, portas) não depende de nada. `Infrastructure` (SQLite) e `Platform.Linux` (D-Bus, XDG, autostart, agendador) implementam as portas. `SlimeGoals.App` tem Views e ViewModels. A injeção de dependência acontece só nos heads `Desktop` e `Android`. Um teste de arquitetura reprova o Core que referenciar UI, banco ou plataforma.

Servidor de sincronização em `src/SlimeGoals.Server` (ASP.NET Core + PostgreSQL), motor em `src/SlimeGoals.Sync`, contratos em `src/SlimeGoals.Sync.Contracts`. Especificações e planos em `specs/`: leia a spec da funcionalidade antes de mexer nela.

## Compilar e testar

```bash
dotnet build SlimeGoals.slnx
dotnet test --solution SlimeGoals.slnx
```

- `TreatWarningsAsErrors` está ligado: um warning quebra o build.
- Runner: Microsoft Testing Platform (xUnit v3), configurado em `global.json`.
- Os testes do servidor sobem PostgreSQL com Testcontainers: o Docker precisa estar rodando. Os testes do chaveiro são ignorados sem `dbus-daemon` e `gnome-keyring-daemon`.

## Rodar sem tocar nos dados reais

O app instalado usa `~/.local/share/SlimeGoals/slimegoals.db`. Em desenvolvimento, aponte sempre para outra pasta:

```bash
XDG_DATA_HOME=/tmp/slimegoals-dev/data XDG_CONFIG_HOME=/tmp/slimegoals-dev/config \
  dotnet run --project src/SlimeGoals.Desktop
```

O `XDG_CONFIG_HOME` próprio impede que "Iniciar com o sistema" grave a entrada de autostart de verdade.

Para conferir telas sem abrir o app: `dotnet run --project tools/SlimeGoals.Render -- /tmp/render all` (ou `phone`, `desktop`) grava um PNG por página. Leia os PNGs para verificar uma mudança visual.

## Android

Fora de `SlimeGoals.slnx`; usa `SlimeGoals.Android.slnx`. Precisa do workload `android`, JDK 21, Android SDK em `~/Android/Sdk` e do AVD `slimegoals-api36`. Comandos de build, instalação e `adb` no README, seção "Rodar no Android".

Alarmes exatos na API 36: `adb shell appops set --uid dev.alcm.slimegoals SCHEDULE_EXACT_ALARM allow`. A forma por pacote não tem efeito.

## Empacotar e servidor

- Linux: `bash packaging/linux/build.sh`. Android: `packaging/android/build.sh` (pede a senha da chave). A versão vem de `Directory.Build.props`.
- Servidor: `packaging/server/init-env.sh`, depois `docker compose up -d --build --wait`. Use sempre `--build`. O código de confirmação do cadastro aparece em `docker compose logs server`.

## Regras

- **Chave de assinatura:** `*.keystore` e a senha nunca entram em commit, log ou comando com a senha em texto. Não rode `make-keystore.sh` nem troque a chave: perder ou trocar a chave impede atualizar o app instalado.
- **`.env`:** fica fora do repositório; não exiba o conteúdo nem copie para outro lugar.
- **Dados reais:** não abra, apague nem migre `~/.local/share/SlimeGoals/` em desenvolvimento.
- **`docker compose down -v`** apaga o banco do servidor: só com pedido explícito.
- **Repositório público:** nada de segredo, dado pessoal ou caminho privado em commits, PRs ou issues.

## Fluxo

- Uma branch por mudança (`feat/…`, `fix/…`, `docs/…`), integrada na `main` por Pull Request com merge commit. A `main` é protegida.
- Conventional Commits em português, com escopo por camada: `core`, `infra`, `app`, `linux`, `desktop`, `android`, `specs`, `plans`, `build`.
- TDD: comportamento novo começa por um teste que falha. A `main` compila sem warnings e passa nos testes.
