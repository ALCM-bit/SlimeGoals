# Fases 0 e 1 — Fundação, spikes e domínio de metas — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ter a solução .NET 10 com o shell Avalonia no tema v4, os dois spikes de risco respondidos (notificação com ações no GNOME e alarme + notificação no Android) e o domínio de metas (três recorrências, período de avaliação, validação, persistência SQLite) pronto e testado.

**Architecture:** Hexagonal pragmática da spec (seção 5): `Core` sem dependências, `Infrastructure` com SQLite/Dapper, `Platform.Linux` com adaptadores de SO, `App` com Views/ViewModels compartilhados e `Desktop` como head e raiz de composição (DI). Os projetos Android só entram na Fase 7a; nesta fase o Android aparece apenas como spike descartável.

**Tech Stack:** .NET SDK 10.0.113 · Avalonia 12.1.4 · CommunityToolkit.Mvvm 8.4.2 · Microsoft.Extensions.DependencyInjection 10.0.14 · Microsoft.Data.Sqlite 10.0.14 · Dapper 2.1.89 · xunit.v3 4.0.2 · xunit.runner.visualstudio 4.0.1 · Microsoft.NET.Test.Sdk 18.11.0 · AwesomeAssertions 9.6.0 · Microsoft.Extensions.TimeProvider.Testing 10.11.0 · Tmds.DBus.Protocol 0.95.2 (só no spike)

**Spec:** `specs/Planejamento_v3.md` (v1.3)

## Global Constraints

- Todos os projetos em `net10.0`; `Nullable` e `ImplicitUsings` habilitados; `TreatWarningsAsErrors=true`.
- Versões de pacotes centralizadas em `Directory.Packages.props` (exatamente as do Tech Stack).
- `SlimeGoals.Core` não referencia nenhum outro projeto nem pacote de UI, banco ou plataforma (spec §5).
- Relógio sempre via `TimeProvider` injetado; nunca `DateTime.Now`/`DateTimeOffset.UtcNow` no Core.
- Instantes gravados em UTC, formato ISO 8601 round-trip (`"O"`); `start_date`/`end_date` como data local `yyyy-MM-dd`; horários `HH:mm` (spec §8).
- `active_days` gravado como `'1,2,3,4,5'` com 1 = segunda … 7 = domingo (spec §8).
- Semana local de **segunda a domingo**, fixa (spec §5.1).
- Toda consulta ignora registros com `deleted_at` preenchido (spec §8).
- Tokens visuais só via `DynamicResource` definidos em `Theme.axaml`, com os valores da spec §11; fonte Figtree embutida.
- Textos visíveis ao usuário em português do Brasil; identificadores de código em inglês.
- Git: Feature Branch Workflow — cada grupo de tarefas tem sua branch; Conventional Commits com escopos `core`, `infra`, `app`, `linux`, `desktop`, `build`, `specs`; merge na `main` com `--no-ff` só após aprovação do autor; branches `spike/*` nunca entram na `main`. Todo commit termina com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Meia-noite inexistente ou duplicada no horário de verão** — converter o início de um dia/semana local para UTC num fuso cujo horário de verão começa ou termina às 00:00 deve dar o primeiro instante válido do dia, sem exceção. Coberto na Task 7.
2. **Semana que cruza domingo→segunda e meta que começa no meio da semana** — domingo pertence à semana iniciada na segunda anterior; uma meta semanal com `start_date` numa quarta tem período de quarta até a segunda seguinte. Coberto na Task 6.
3. **GUID gravado como BLOB pelo Microsoft.Data.Sqlite** — sem conversão explícita, `GetAsync(id)` não encontra o que `AddAsync` gravou. Coberto na Task 11.
4. **Título com espaços, acentos e emoji** — `"  Ler 📚 ação  "` é salvo como `"Ler 📚 ação"` e volta idêntico do banco. Coberto nas Tasks 8 e 11.
5. **Reabrir o app com banco existente** — as migrações não rodam de novo, os dados continuam lá e a pasta do banco é criada se não existir. Coberto nas Tasks 10, 11 e 12.

---

## Mapa de arquivos

```
SlimeGoals/
├── global.json                         # fixa SDK 10.0.113 (rollForward latestFeature)
├── Directory.Build.props               # net10.0, nullable, warnings como erro
├── Directory.Packages.props            # versões centralizadas
├── SlimeGoals.slnx                     # formato padrão do .NET 10
├── src/
│   ├── SlimeGoals.Core/
│   │   ├── Domain/
│   │   │   ├── Recurrence.cs           # enum Once | Daily | Weekly
│   │   │   ├── GoalColor.cs            # enum Purple | Pink | Amber | Green
│   │   │   ├── WeekDays.cs             # [Flags] dias ativos + extensões
│   │   │   ├── Goal.cs                 # record imutável
│   │   │   ├── GoalDraft.cs            # dados de entrada do formulário
│   │   │   ├── DateRange.cs            # [início, fim) em datas locais + ToUtc
│   │   │   ├── WeekMath.cs             # início da semana (segunda)
│   │   │   └── GoalPeriod.cs           # período de avaliação e regras de calendário
│   │   ├── Validation/
│   │   │   ├── ValidationError.cs
│   │   │   ├── GoalValidator.cs
│   │   │   └── GoalValidationException.cs
│   │   ├── Services/GoalService.cs
│   │   ├── Abstractions/IGoalRepository.cs
│   │   └── GoalNotFoundException.cs
│   ├── SlimeGoals.Infrastructure/
│   │   ├── Data/
│   │   │   ├── SqliteConnectionFactory.cs
│   │   │   ├── MigrationRunner.cs
│   │   │   ├── Migrations/001_init.sql # recurso embutido
│   │   │   ├── GoalRow.cs              # linha crua (strings) ↔ Goal
│   │   │   └── GoalRepository.cs
│   │   └── InfrastructureServiceCollectionExtensions.cs
│   ├── SlimeGoals.Platform.Linux/
│   │   └── XdgPaths.cs
│   ├── SlimeGoals.App/
│   │   ├── App.axaml(.cs)
│   │   ├── Assets/Fonts/               # Figtree-*.ttf + OFL.txt
│   │   ├── Styles/Theme.axaml          # tokens
│   │   ├── Styles/Controls.axaml       # estilos de navegação e cartões
│   │   ├── ViewModels/{PageViewModel,ShellViewModel,TodayPageViewModel,GoalsPageViewModel,ReportsPageViewModel,SettingsPageViewModel}.cs
│   │   └── Views/{ShellView,TodayView,GoalsView,ReportsView,SettingsView}.axaml
│   └── SlimeGoals.Desktop/
│       └── Program.cs                  # DI, migração, AppBuilder
└── tests/
    ├── SlimeGoals.Core.Tests/
    ├── SlimeGoals.Infrastructure.Tests/
    └── SlimeGoals.App.Tests/           # ViewModels, sem renderizar
```

---

# FASE 0 — Fundação + spikes

## Task 1: Esqueleto da solução e regra de dependência

**Branch:** criar `feat/fase-0-fundacao` a partir da `main` (usada nas Tasks 1 e 2).

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `SlimeGoals.slnx`
- Create: `src/SlimeGoals.Core/SlimeGoals.Core.csproj`, `src/SlimeGoals.Core/CoreAssembly.cs`
- Create: `src/SlimeGoals.Infrastructure/SlimeGoals.Infrastructure.csproj`, `src/SlimeGoals.Platform.Linux/SlimeGoals.Platform.Linux.csproj`
- Create: `tests/SlimeGoals.Core.Tests/SlimeGoals.Core.Tests.csproj`, `tests/SlimeGoals.Infrastructure.Tests/SlimeGoals.Infrastructure.Tests.csproj`
- Test: `tests/SlimeGoals.Core.Tests/ArchitectureTests.cs`

**Interfaces:**
- Produces: `SlimeGoals.Core.CoreAssembly` (classe estática marcadora, `public static Assembly Reference => typeof(CoreAssembly).Assembly;`); projetos referenciáveis por caminho.

- [ ] **Step 1: Criar a estrutura.** `dotnet new globaljson --sdk-version 10.0.113 --roll-forward latestFeature`; `dotnet new sln -n SlimeGoals` (gera `.slnx`); `dotnet new classlib` para Core, Infrastructure e Platform.Linux; `dotnet new xunit3` para os dois projetos de teste (se o template `xunit3` não existir: `dotnet new install xunit.v3.templates`). Apagar os `Class1.cs`/`UnitTest1.cs` gerados. Referências: Infrastructure → Core; Platform.Linux → Core; Core.Tests → Core; Infrastructure.Tests → Infrastructure. `Directory.Build.props` com `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `ManagePackageVersionsCentrally=true`. `Directory.Packages.props` com todas as versões do Tech Stack; os `.csproj` de teste referenciam `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `AwesomeAssertions`, sem versão.

- [ ] **Step 2: Escrever o teste de arquitetura que falha**

```csharp
public class ArchitectureTests
{
    [Fact]
    public void Core_does_not_reference_other_projects_or_infrastructure_packages()
    {
        var forbidden = new[] { "SlimeGoals.", "Avalonia", "Dapper", "Microsoft.Data.Sqlite", "Tmds" };
        var refs = CoreAssembly.Reference.GetReferencedAssemblies().Select(a => a.Name!);
        refs.Should().NotContain(r => forbidden.Any(f => r.StartsWith(f)));
    }
}
```

- [ ] **Step 3: Rodar e ver falhar.** `dotnet test tests/SlimeGoals.Core.Tests` → falha de compilação: `CoreAssembly` não existe.

- [ ] **Step 4: Criar `CoreAssembly` em `src/SlimeGoals.Core/CoreAssembly.cs`.**

- [ ] **Step 5: Rodar e ver passar.** `dotnet build SlimeGoals.slnx` sem warnings; `dotnet test SlimeGoals.slnx` → 1 teste aprovado.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "build: cria solução .NET 10 com Core, Infrastructure, Platform.Linux e testes"
```

## Task 2: App Avalonia, head Desktop, tema e navegação lateral

**Files:**
- Create: `src/SlimeGoals.App/` (csproj, `App.axaml(.cs)`, `Styles/Theme.axaml`, `Styles/Controls.axaml`, `Assets/Fonts/*`, `ViewModels/*`, `Views/*`)
- Create: `src/SlimeGoals.Desktop/` (csproj, `Program.cs`, `app.manifest` se o template gerar)
- Create: `tests/SlimeGoals.App.Tests/` (csproj), Test: `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs`

**Interfaces:**
- Consumes: estrutura da Task 1.
- Produces:
  - `abstract class PageViewModel : ObservableObject { public abstract string Title { get; } }`
  - `TodayPageViewModel` ("Hoje"), `GoalsPageViewModel` ("Metas"), `ReportsPageViewModel` ("Relatórios"), `SettingsPageViewModel` ("Configurações") — placeholders sem lógica.
  - `sealed partial class ShellViewModel(IEnumerable<PageViewModel> pages) : ObservableObject` com `IReadOnlyList<PageViewModel> Pages`, `[ObservableProperty] PageViewModel CurrentPage` (inicia em `Pages[0]`) e `[RelayCommand] void Navigate(PageViewModel page)`.
  - `App(IServiceProvider services)`: a `App` recebe o provider pelo construtor; `Program` usa `AppBuilder.Configure(() => new App(provider))`.
  - Na DI, as páginas são registradas como `PageViewModel` em ordem: Hoje, Metas, Relatórios, Configurações.

- [ ] **Step 1: Gerar boilerplate de referência fora do repositório.** Em `/tmp/claude-1000/.../scratchpad/ref`: `dotnet new install Avalonia.Templates::12.1.4` e `dotnet new avalonia.xplat -n Ref`. Copiar dele só o que a API do Avalonia 12 exige (`Program.cs` desktop, `App.axaml(.cs)`, pacotes do csproj, `AvaloniaUseCompiledBindingsByDefault`). Isso evita usar APIs do Avalonia 11 que mudaram na 12. Criar `SlimeGoals.App` (biblioteca: `Avalonia`, `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection.Abstractions`) e `SlimeGoals.Desktop` (WinExe: `Avalonia.Desktop`; referencia App, Core, Infrastructure, Platform.Linux). Adicionar os pacotes ao `Directory.Packages.props` com a versão 12.1.4.

- [ ] **Step 2: Escrever os testes do shell que falham**

```csharp
public class ShellViewModelTests
{
    private static ShellViewModel Create() => new(new PageViewModel[]
        { new TodayPageViewModel(), new GoalsPageViewModel(), new ReportsPageViewModel(), new SettingsPageViewModel() });

    [Fact]
    public void Pages_follow_spec_order_and_start_on_today()
    {
        var shell = Create();
        shell.Pages.Select(p => p.Title).Should().Equal("Hoje", "Metas", "Relatórios", "Configurações");
        shell.CurrentPage.Should().BeOfType<TodayPageViewModel>();
    }

    [Fact]
    public void Navigate_changes_current_page_and_notifies()
    {
        var shell = Create();
        using var monitor = shell.Monitor();
        shell.NavigateCommand.Execute(shell.Pages[2]);
        shell.CurrentPage.Should().BeOfType<ReportsPageViewModel>();
        monitor.Should().RaisePropertyChangeFor(s => s.CurrentPage);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar.** `dotnet test tests/SlimeGoals.App.Tests` → erro de compilação (tipos inexistentes).

- [ ] **Step 4: Implementar os ViewModels da seção Interfaces.**

- [ ] **Step 5: Rodar e ver passar.** `dotnet test tests/SlimeGoals.App.Tests` → 2 aprovados.

- [ ] **Step 6: Tema e fonte.** Baixar Figtree estática nos pesos 400, 500, 600, 700 e 800 (repositório oficial `erikdkennedy/figtree`, pasta de TTF estáticos) e o `OFL.txt` para `Assets/Fonts/` como `AvaloniaResource`. Em `Theme.axaml`, criar `Color` + `SolidColorBrush` para **todos** os tokens da spec §11, com chaves iguais aos nomes dos tokens (`Bg`, `BgDeep`, `Panel`, `Surface`, `Tile`, `Surface2`, `Border`, `Text`, `Muted`, `Primary`, `PrimarySoft`, `PrimaryTint`, `Selected`, `SelectedStrong`, `BarIdle`, `OnPrimary`, `Success`, `Warning`), mais `HeroBrush` (gradiente linear 145°, `#7656D0`→`#43347F`), a paleta de metas `GoalPurpleBg/GoalPurpleFg`, `GoalPinkBg/Fg`, `GoalAmberBg/Fg`, `GoalGreenBg/Fg`, os raios `RadiusControl=14`, `RadiusCard=22` e `FigtreeFont` (`avares://SlimeGoals.App/Assets/Fonts#Figtree`). `App.axaml` usa `FluentTheme` com `RequestedThemeVariant="Dark"` e inclui Theme e Controls.

- [ ] **Step 7: Shell visual.** `ShellView` em grade `220,*` com espaço de 14 px, fundo `BgDeep`. A barra lateral fica num painel `Panel`, borda `Border`, raio 22, com a marca "SlimeGoals" e um botão por página (altura ≥ 44 px, raio 14, texto `Muted`; quando ativo, fundo `Selected`, texto branco e peso 700; foco com contorno de 2 px em `PrimarySoft`). O conteúdo é um `ContentControl` ligado a `CurrentPage`. Usar um `DataTemplate` por tipo de página em `App.axaml`, com bindings compilados e sem `ViewLocator` por reflexão. Cada página placeholder mostra só o título (24 px, peso 700). Em `Program.cs`, montar o `ServiceCollection`: as páginas e o `ShellViewModel` como singleton, e a janela principal com `MinWidth=900`, `MinHeight=600` e título "SlimeGoals".

- [ ] **Step 8: Verificação manual.** `dotnet run --project src/SlimeGoals.Desktop`: a janela abre em menos de 2 s no GNOME, com o fundo e a barra lateral parecidos com `specs/design/v4-bento-desktop.html`; a fonte é Figtree (os pesos 400 e 800 aparecem visivelmente diferentes); clicar nos quatro itens troca a página e destaca o item ativo; Tab percorre os itens com contorno de foco visível. `dotnet build` continua sem warnings.

- [ ] **Step 9: Commit e merge (após aprovação do autor)**

```bash
git add -A && git commit -m "feat(app): adiciona shell Avalonia com tema v4 e navegação lateral"
git switch main && git merge --no-ff feat/fase-0-fundacao -m "feat: integra fundação da Fase 0" && git branch -d feat/fase-0-fundacao
```

## Task 3: Spike — notificação com ações no GNOME (descartável)

**Branch:** `spike/notificacao-gnome` a partir da `main`. **Nunca entra na `main`.** Requer o autor para clicar nas notificações.

**Perguntas a responder (spec §9 e §14):**
1. O servidor de notificação do GNOME anuncia a capacidade `actions`?
2. Os botões Feito/Adiar/Ignorar chegam como `ActionInvoked` com a janela oculta? E clicando depois, pela central de notificações?
3. Com Não Perturbe ativo, o banner some, mas a notificação fica na central e a ação ainda funciona?
4. `expire_timeout` é respeitado ou ignorado? `NotificationClosed` chega com qual motivo?
5. A dica `desktop-entry` faz aparecer o nome e o ícone do app? Clicar no corpo da notificação (`default`) consegue trazer a janela para a frente no Wayland?
6. `Tmds.DBus.Protocol` 0.95.2 consegue chamar `Notify` e assinar os sinais sem problemas de trimming ou AOT?

- [ ] **Step 1: Responder 1, 3 e 4 sem código.** `gdbus call --session --dest org.freedesktop.Notifications --object-path /org/freedesktop/Notifications --method org.freedesktop.Notifications.GetCapabilities`; enviar uma notificação com `Notify`, actions `['done','Feito','snooze','Adiar','ignore','Ignorar']`, `expire_timeout` 10000 e a dica `desktop-entry`; observar em outro terminal com `gdbus monitor --session --dest org.freedesktop.Notifications`. Repetir com Não Perturbe ativo.
- [ ] **Step 2: Responder 2, 5 e 6 com um console app em `spikes/gnome-notify/`.** O app envia a notificação via `Tmds.DBus.Protocol`, imprime os sinais `ActionInvoked` e `NotificationClosed` filtrados pelo id, e é instalado com um arquivo `slimegoals-spike.desktop` em `~/.local/share/applications/`. Para a pergunta 5, testar a variante Avalonia mínima: janela oculta, que deve ser mostrada e ativada na ação `default`.
- [ ] **Step 3: Registrar as conclusões.** Na branch `docs/spikes-fase-0`, criar `specs/spikes/gnome-notificacoes.md` com uma resposta por pergunta e a decisão resultante (ex.: "expiração controlada pelo app confirmada"). Commit: `docs(specs): registra conclusões do spike de notificações no GNOME`. Se mudar alguma regra, ajustar também `Planejamento_v3.md` §9/§14. Remover o `.desktop` do spike de `~/.local/share/applications/`.

## Task 4: Spike — Avalonia no Android com alarme e ação (descartável)

**Branch:** `spike/android-alarme` a partir da `main`. **Nunca entra na `main`.** Pré-requisitos do autor: um celular Android com depuração USB ativada (ou concordar com o uso de um emulador) e espaço em disco para o Android SDK (~5 GB).

**Perguntas a responder:**
1. O toolchain funciona no CachyOS com o workload `android` do .NET 10?
2. O Avalonia 12.1.4 roda no aparelho com desempenho aceitável (abre em < 2 s)?
3. Um alarme exato (`SetExactAndAllowWhileIdle`) dispara com o app fechado (removido da lista de recentes) e mostra uma notificação com um botão de ação?
4. O `BroadcastReceiver` da ação roda com o processo morto e consegue gravar num arquivo do armazenamento privado (simulando o `ReminderResponseHandler`)?
5. Quais permissões o Android do aparelho pede (notificações 13+, alarme exato 12+)? Qual é o comportamento no modo Doze (`adb shell dumpsys deviceidle force-idle`)?

- [ ] **Step 1: Toolchain.** O SDK atual vem do pacote da distro (`dotnet-sdk-10.0`, RID `arch-x64`, *source-build*), e o workload Android costuma falhar nesse tipo de build. Tentar primeiro `sudo dotnet workload install android`. Se falhar, instalar o SDK da Microsoft só para o usuário, com `dotnet-install.sh --channel 10.0 --install-dir ~/.dotnet`, e rodar o spike com `DOTNET_ROOT=~/.dotnet` e `~/.dotnet/dotnet` (sem mexer no SDK do sistema). Registrar qual caminho funcionou, porque isso define o ambiente da Fase 7. JDK 21 via `pacman`; Android SDK em `~/Android/Sdk` via `dotnet build -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=<jdk> -p:AcceptAndroidSDKLicenses=True`. Anotar cada comando que funcionou.
- [ ] **Step 2: Gerar `dotnet new avalonia.xplat -n SpikeAndroid` em `spikes/android/`.** Adicionar um botão "Agendar em 1 min" que cria o canal de notificação, pede as permissões e agenda o alarme; um `AlarmReceiver` mostra a notificação com a ação "Feito"; um `ActionReceiver` grava a data e hora em `FilesDir/actions.log`. Instalar com `dotnet build -t:Run -f net10.0-android`.
- [ ] **Step 3: Executar as perguntas 3 a 5.** Testar com o app aberto, com o app fechado e em Doze forçado; conferir o `actions.log` via `adb shell run-as`.
- [ ] **Step 4: Registrar as conclusões** em `specs/spikes/android-alarme.md`, na branch `docs/spikes-fase-0`. Incluir uma sugestão de versão mínima do Android (pendência da Fase 7c) e os comandos de toolchain. Commit: `docs(specs): registra conclusões do spike Android`. Fazer o merge de `docs/spikes-fase-0` na `main` após aprovação.

**Critério de pronto da Fase 0 (spec §12):** app abre com o tema; notificação com ação responde no Linux; build Android roda e dispara alarme — tudo documentado nos dois arquivos de `specs/spikes/`.

---

# FASE 1 — Metas (domínio + dados)

**Branch:** `feat/fase-1-metas-dominio` a partir da `main` (Tasks 5 a 12).

## Task 5: Tipos de domínio

**Files:**
- Create: `src/SlimeGoals.Core/Domain/{Recurrence,GoalColor,WeekDays,Goal,DateRange,WeekMath}.cs`
- Test: `tests/SlimeGoals.Core.Tests/Domain/WeekDaysTests.cs`, `tests/SlimeGoals.Core.Tests/Domain/WeekMathTests.cs`

**Interfaces:**
- Produces:
  - `enum Recurrence { Once, Daily, Weekly }`; `enum GoalColor { Purple, Pink, Amber, Green }`
  - `[Flags] enum WeekDays { None = 0, Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8, Friday = 16, Saturday = 32, Sunday = 64, Weekdays = 31, All = 127 }`
  - `static class WeekDaysExtensions`: `bool Includes(this WeekDays days, DayOfWeek day)`, `string ToStorage(this WeekDays days)` (`"1,2,3"`, crescente, 1 = segunda), `static WeekDays ParseStorage(string value)` (lança `FormatException` para tokens fora de 1–7)
  - `readonly record struct DateRange(DateOnly Start, DateOnly EndExclusive)` com `bool Contains(DateOnly d)`; `DateOnly.MinValue`/`DateOnly.MaxValue` significam "aberto" (a conversão para UTC fica na Task 7)
  - `static class WeekMath`: `DateOnly StartOfWeek(DateOnly date)` (segunda)
  - `sealed record Goal` com propriedades `init`: `Guid Id`, `string Title`, `Recurrence Recurrence`, `int Target`, `string Icon`, `GoalColor Color`, `int ReminderEveryMinutes`, `TimeOnly ActiveFrom`, `TimeOnly ActiveUntil`, `WeekDays ActiveDays`, `DateOnly? StartDate`, `DateOnly? EndDate`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`, `DateTimeOffset? CompletedAt`, `DateTimeOffset? ArchivedAt`, `DateTimeOffset? DeletedAt`. Usa `WeekDays` em vez de um conjunto para que `Goal` tenha igualdade por valor.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Theory]
[InlineData(WeekDays.Weekdays, "1,2,3,4,5")]
[InlineData(WeekDays.Sunday | WeekDays.Monday, "1,7")]
[InlineData(WeekDays.All, "1,2,3,4,5,6,7")]
public void Storage_round_trips(WeekDays days, string stored)
{
    days.ToStorage().Should().Be(stored);
    WeekDaysExtensions.ParseStorage(stored).Should().Be(days);
}

[Fact] public void Includes_maps_DayOfWeek_sunday_to_flag_sunday() =>
    (WeekDays.Sunday).Includes(DayOfWeek.Sunday).Should().BeTrue();

[Theory] [InlineData("0")] [InlineData("8")] [InlineData("1,x")]
public void ParseStorage_rejects_invalid_tokens(string s) =>
    FluentActions.Invoking(() => WeekDaysExtensions.ParseStorage(s)).Should().Throw<FormatException>();

[Theory] // 2026-11-02 é segunda
[InlineData("2026-11-02", "2026-11-02")]
[InlineData("2026-11-08", "2026-11-02")] // domingo pertence à semana anterior
[InlineData("2026-11-05", "2026-11-02")]
public void StartOfWeek_is_monday(string date, string monday) =>
    WeekMath.StartOfWeek(DateOnly.Parse(date)).Should().Be(DateOnly.Parse(monday));
```

- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/SlimeGoals.Core.Tests` → erro de compilação.
- [ ] **Step 3: Implementar os tipos da seção Interfaces.**
- [ ] **Step 4: Rodar e ver passar.** Todos os testes aprovados.
- [ ] **Step 5: Commit** — `feat(core): adiciona tipos de domínio de metas e cálculo de semana`

## Task 6: `GoalPeriod` — período de avaliação e regras de calendário

**Files:**
- Create: `src/SlimeGoals.Core/Domain/GoalPeriod.cs`
- Test: `tests/SlimeGoals.Core.Tests/Domain/GoalPeriodTests.cs`

**Interfaces:**
- Consumes: `Goal`, `DateRange`, `WeekMath`, `WeekDays.Includes` (Task 5).
- Produces (`static class GoalPeriod`):
  - `bool IsInPeriod(Goal goal, DateOnly date)` — `date` dentro de `[StartDate, EndDate]` (lados nulos = aberto)
  - `bool IsShownOn(Goal goal, DateOnly date)` — `IsInPeriod` e, se `Daily`, `ActiveDays.Includes(date.DayOfWeek)`; `Weekly`/`Once` aparecem em qualquer dia do período
  - `bool RemindsOn(Goal goal, DateOnly date)` — `IsInPeriod` e `ActiveDays.Includes(date.DayOfWeek)`, para os três tipos
  - `DateRange? EvaluationPeriod(Goal goal, DateOnly date)` — `null` se `!IsInPeriod`. `Once`: `[StartDate ?? MinValue, EndDate+1 ?? MaxValue)`. `Daily`: `[date, date+1)`. `Weekly`: `[StartOfWeek(date), +7)` recortado por `StartDate` e `EndDate+1`.
  - Arquivamento e exclusão **não** entram aqui; esse filtro é do serviço (Fase 3/4).

- [ ] **Step 1: Escrever os testes que falham.** Usar um helper `G(Recurrence r, string? start = null, string? end = null, WeekDays days = WeekDays.All)` que monta um `Goal` válido. Casos obrigatórios (datas de nov/2026; 02/11 é segunda):

```csharp
[Fact] public void Weekly_period_is_monday_to_next_monday() =>
    GoalPeriod.EvaluationPeriod(G(Recurrence.Weekly), D("2026-11-08"))
        .Should().Be(new DateRange(D("2026-11-02"), D("2026-11-09")));

[Fact] public void Weekly_period_is_clipped_by_start_and_end() =>
    GoalPeriod.EvaluationPeriod(G(Recurrence.Weekly, start: "2026-11-04", end: "2026-11-06"), D("2026-11-05"))
        .Should().Be(new DateRange(D("2026-11-04"), D("2026-11-07")));

[Fact] public void Daily_period_is_the_day() =>
    GoalPeriod.EvaluationPeriod(G(Recurrence.Daily), D("2026-11-05"))
        .Should().Be(new DateRange(D("2026-11-05"), D("2026-11-06")));

[Fact] public void Once_period_spans_start_to_end_inclusive() =>
    GoalPeriod.EvaluationPeriod(G(Recurrence.Once, start: "2026-11-01", end: "2026-11-15"), D("2026-11-05"))
        .Should().Be(new DateRange(D("2026-11-01"), D("2026-11-16")));

[Fact] public void Once_without_dates_is_open() =>
    GoalPeriod.EvaluationPeriod(G(Recurrence.Once), D("2026-11-05"))
        .Should().Be(new DateRange(DateOnly.MinValue, DateOnly.MaxValue));

[Theory] [InlineData("2026-11-03")] [InlineData("2026-11-11")]
public void Outside_start_end_has_no_period_and_is_not_shown(string date)
{
    var g = G(Recurrence.Weekly, start: "2026-11-04", end: "2026-11-10");
    GoalPeriod.EvaluationPeriod(g, D(date)).Should().BeNull();
    GoalPeriod.IsShownOn(g, D(date)).Should().BeFalse();
}

[Fact] public void Daily_is_hidden_on_inactive_day_but_weekly_is_shown()
{
    // 2026-11-07 é sábado
    GoalPeriod.IsShownOn(G(Recurrence.Daily, days: WeekDays.Weekdays), D("2026-11-07")).Should().BeFalse();
    GoalPeriod.IsShownOn(G(Recurrence.Weekly, days: WeekDays.Weekdays), D("2026-11-07")).Should().BeTrue();
    GoalPeriod.RemindsOn(G(Recurrence.Weekly, days: WeekDays.Weekdays), D("2026-11-07")).Should().BeFalse();
}

[Fact] public void End_date_is_inclusive() =>
    GoalPeriod.IsInPeriod(G(Recurrence.Daily, end: "2026-11-10"), D("2026-11-10")).Should().BeTrue();
```

- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/SlimeGoals.Core.Tests --filter "FullyQualifiedName~GoalPeriodTests"` → erro de compilação.
- [ ] **Step 3: Implementar `GoalPeriod` conforme a seção Interfaces.**
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** — `feat(core): calcula período de avaliação para metas únicas, diárias e semanais`

## Task 7: Conversão de períodos locais para UTC (horário de verão)

**Files:**
- Modify: `src/SlimeGoals.Core/Domain/DateRange.cs`
- Test: `tests/SlimeGoals.Core.Tests/Domain/DateRangeUtcTests.cs`

**Interfaces:**
- Produces: `(DateTimeOffset StartUtc, DateTimeOffset EndUtc) DateRange.ToUtc(TimeZoneInfo zone)` e `static DateTimeOffset DateRange.LocalMidnightToUtc(DateOnly date, TimeZoneInfo zone)`. `MinValue` vira `DateTimeOffset.MinValue` e `MaxValue` vira `DateTimeOffset.MaxValue`, sem conversão.

Algoritmo de `LocalMidnightToUtc` (não determinado pelos testes isoladamente):

```
local = date à meia-noite (DateTimeKind.Unspecified)
enquanto zone.IsInvalidTime(local): local += 1 minuto     // meia-noite pulada → primeiro instante válido
se zone.IsAmbiguousTime(local): offset = maior de zone.GetAmbiguousTimeOffsets(local)  // primeira ocorrência
senão: offset = zone.GetUtcOffset(local)
retorna new DateTimeOffset(local, offset).ToUniversalTime()
```

- [ ] **Step 1: Escrever os testes que falham.** Criar no teste um fuso customizado e determinístico com `TimeZoneInfo.CreateCustomTimeZone("Test/MidnightDst", TimeSpan.FromHours(-3), ...)`, com uma regra de ajuste em que o horário de verão (+1 h) começa no primeiro domingo de novembro às 00:00 (a meia-noite é pulada para 01:00) e termina no terceiro domingo de fevereiro às 01:00 do horário de verão (volta para 00:00, então 00:00–00:59 ocorre duas vezes). Em 2026/27, isso cai em 01/11/2026 e 21/02/2027.

```csharp
[Fact] public void Plain_day_in_utc_minus_3()
{
    var r = new DateRange(D("2026-11-03"), D("2026-11-04")).ToUtc(Fixed(-3));
    r.StartUtc.Should().Be(new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero));
    r.EndUtc.Should().Be(new DateTimeOffset(2026, 11, 4, 3, 0, 0, TimeSpan.Zero));
}

[Fact] public void Skipped_midnight_uses_first_valid_instant() =>   // 2026-11-01 00:00 não existe → 01:00 (-02:00)
    DateRange.LocalMidnightToUtc(D("2026-11-01"), MidnightDst)
        .Should().Be(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero));

[Fact] public void Repeated_midnight_uses_first_occurrence() =>     // 2027-02-21 00:00 ocorre duas vezes → a primeira (-02:00)
    DateRange.LocalMidnightToUtc(D("2027-02-21"), MidnightDst)
        .Should().Be(new DateTimeOffset(2027, 2, 21, 2, 0, 0, TimeSpan.Zero));

[Fact] public void Open_bounds_map_to_extremes() =>
    new DateRange(DateOnly.MinValue, DateOnly.MaxValue).ToUtc(Fixed(-3))
        .Should().Be((DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
```

- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar com o algoritmo acima.**
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** — `feat(core): converte períodos locais para UTC tratando horário de verão`

## Task 8: Rascunho e validação de metas

**Files:**
- Create: `src/SlimeGoals.Core/Domain/GoalDraft.cs`, `src/SlimeGoals.Core/Validation/{ValidationError,GoalValidator,GoalValidationException}.cs`
- Test: `tests/SlimeGoals.Core.Tests/Validation/GoalValidatorTests.cs`

**Interfaces:**
- Produces:
  - `sealed record GoalDraft(string Title, Recurrence Recurrence, int Target, string Icon, GoalColor Color, int ReminderEveryMinutes, TimeOnly ActiveFrom, TimeOnly ActiveUntil, WeekDays ActiveDays, DateOnly? StartDate, DateOnly? EndDate)`
  - `sealed record ValidationError(string Field, string Message)` — `Field` = `nameof(GoalDraft.X)`
  - `static class GoalValidator { IReadOnlyList<ValidationError> Validate(GoalDraft draft); }`
  - `sealed class GoalValidationException(IReadOnlyList<ValidationError> errors) : Exception` com `Errors`

**Regras e mensagens exatas** (a spec não fixa limites; estes são decisões deste plano):

| Campo                  | Regra                                     | Mensagem                                                      |
|------------------------|-------------------------------------------|---------------------------------------------------------------|
| `Title`                | após `Trim()`, 1 a 80 caracteres          | "Informe um título." / "O título deve ter até 80 caracteres." |
| `Target`               | `Once` → exatamente 1; demais → 1 a 99    | "Tarefa única tem alvo 1." / "O alvo deve ser entre 1 e 99."  |
| `ReminderEveryMinutes` | 5 a 1440                                  | "O intervalo deve ser entre 5 minutos e 24 horas."            |
| `ActiveUntil`          | `ActiveFrom < ActiveUntil`                | "O fim do horário deve ser depois do início."                 |
| `ActiveDays`           | `!= None`                                 | "Escolha ao menos um dia."                                    |
| `EndDate`              | se ambos existem, `EndDate >= StartDate`  | "A data de fim deve ser igual ou posterior à de início."      |
| `Icon`                 | não vazio                                 | "Escolha um ícone."                                           |

- [ ] **Step 1: Escrever os testes que falham.** Um helper `Valid()` devolve um rascunho semanal válido. Testes: o rascunho válido não tem erros; um teste por linha da tabela, conferindo `Field` e `Message`; `"   "` como título dá "Informe um título."; um título de 80 caracteres após o trim é válido e um de 81 não; `Once` com `Target=2` dá erro em `Target`; rascunho com dois problemas retorna os dois erros.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar `GoalValidator.Validate` acumulando todos os erros (não para no primeiro).**
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** — `feat(core): valida rascunho de meta com mensagens em português`

## Task 9: `IGoalRepository` e `GoalService`

**Files:**
- Create: `src/SlimeGoals.Core/Abstractions/IGoalRepository.cs`, `src/SlimeGoals.Core/Services/GoalService.cs`, `src/SlimeGoals.Core/GoalNotFoundException.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/GoalServiceTests.cs`, `tests/SlimeGoals.Core.Tests/Fakes/InMemoryGoalRepository.cs`
- Add package ao Core.Tests: `Microsoft.Extensions.TimeProvider.Testing`

**Interfaces:**
- Produces:

```csharp
public interface IGoalRepository
{
    Task AddAsync(Goal goal, CancellationToken ct = default);
    Task UpdateAsync(Goal goal, CancellationToken ct = default);          // lança GoalNotFoundException se o id não existe
    Task<Goal?> GetAsync(Guid id, CancellationToken ct = default);        // null se não existe ou DeletedAt != null
    Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default); // sem excluídas; ordem CreatedAt
}

public sealed class GoalService(IGoalRepository goals, TimeProvider time)
{
    Task<Goal> CreateAsync(GoalDraft draft, CancellationToken ct = default);
    Task<Goal> UpdateAsync(Guid id, GoalDraft draft, CancellationToken ct = default);
    Task ArchiveAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Goal?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived = false, CancellationToken ct = default);
}
public sealed class GoalNotFoundException(Guid id) : Exception;
```

- Comportamentos: `Create` valida (lança `GoalValidationException`), usa `Id = Guid.CreateVersion7(time.GetUtcNow())`, `CreatedAt = UpdatedAt = time.GetUtcNow()` e `Title.Trim()`. `Update` mantém `Id`, `CreatedAt`, `CompletedAt` e `ArchivedAt`, atualiza `UpdatedAt` e lança `GoalNotFoundException` se não existir. `Archive` preenche `ArchivedAt` e `UpdatedAt`; é idempotente (se já está arquivada, não muda `ArchivedAt`). `Delete` preenche `DeletedAt` e `UpdatedAt`.

- [ ] **Step 1: Escrever o `InMemoryGoalRepository` (fake de teste em memória com dicionário, respeitando o contrato acima) e os testes que falham**, usando `FakeTimeProvider` iniciado em `2026-11-02T12:00Z`:
  - `Create_assigns_id_timestamps_and_trims_title`: título `"  Ler 📚 ação  "` → `"Ler 📚 ação"`, `CreatedAt == UpdatedAt == 2026-11-02T12:00Z`, `Id != Guid.Empty`
  - `Create_with_invalid_draft_throws_and_persists_nothing`
  - `Update_keeps_created_at_and_bumps_updated_at` (avança o relógio 1 h)
  - `Update_unknown_id_throws_GoalNotFoundException`
  - `Archive_is_idempotent` (arquiva, avança o relógio, arquiva de novo → `ArchivedAt` igual ao primeiro)
  - `List_hides_archived_by_default_and_deleted_always`
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar `GoalService`.**
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** — `feat(core): adiciona GoalService com criação, edição, arquivamento e exclusão lógica`

## Task 10: Conexão SQLite e migrações

**Files:**
- Create: `src/SlimeGoals.Infrastructure/Data/{SqliteConnectionFactory,MigrationRunner}.cs`, `src/SlimeGoals.Infrastructure/Data/Migrations/001_init.sql` (`EmbeddedResource`)
- Test: `tests/SlimeGoals.Infrastructure.Tests/Data/MigrationRunnerTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/TestDatabase.cs`
- Add packages ao Infrastructure: `Microsoft.Data.Sqlite`, `Dapper`

**Interfaces:**
- Produces:
  - `sealed class SqliteConnectionFactory(string connectionString) { SqliteConnection Open(); }` — abre e executa `PRAGMA foreign_keys = ON;`
  - `static class MigrationRunner { int Migrate(SqliteConnection connection); }` — lê os recursos `*.Migrations.NNN_*.sql` em ordem numérica, aplica, numa transação cada, os que têm número > `PRAGMA user_version`, atualiza `user_version` e retorna a versão final
  - `TestDatabase : IDisposable` (teste): connection string `Data Source=test-{Guid};Mode=Memory;Cache=Shared`, mantém uma conexão-âncora aberta durante o teste e expõe `Factory`
- `001_init.sql` = o DDL exato da spec §8 (`goals`, `checkins`, `reminder_log`, `settings`, os dois índices e os `CHECK`).

- [ ] **Step 1: Escrever os testes que falham**
  - `Migrate_on_empty_db_creates_tables_and_sets_version_1` (`sqlite_master` contém as 4 tabelas; `user_version = 1`)
  - `Migrate_twice_is_a_no_op` (a segunda chamada retorna 1 e não lança)
  - `Once_goal_with_target_2_violates_check` (INSERT direto → `SqliteException`)
  - `Unknown_recurrence_violates_check`
  - `Checkin_with_unknown_goal_violates_foreign_key`
- [ ] **Step 2: Rodar e ver falhar.** `dotnet test tests/SlimeGoals.Infrastructure.Tests`
- [ ] **Step 3: Implementar a fábrica, o runner e o script.**
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** — `feat(infra): adiciona conexão SQLite e migrações versionadas por user_version`

## Task 11: `GoalRepository` com Dapper

**Files:**
- Create: `src/SlimeGoals.Infrastructure/Data/{GoalRow,GoalRepository}.cs`
- Test: `tests/SlimeGoals.Infrastructure.Tests/Data/GoalRepositoryTests.cs`

**Interfaces:**
- Consumes: `IGoalRepository`, `Goal` (Core); `SqliteConnectionFactory` (Task 10).
- Produces: `sealed class GoalRepository(SqliteConnectionFactory db) : IGoalRepository`.
- Mapeamento explícito via `GoalRow` (todas as colunas como `string`/`long`/`string?`, com `ToDomain()` e `static FromDomain(Goal)`). Isso evita os handlers de tipo do Dapper e o **GUID gravado como BLOB**: o id vai como `ToString("D")`; a recorrência como `"once"|"daily"|"weekly"`; a cor como `"purple"|"pink"|"amber"|"green"`; horários como `"HH:mm"`; datas como `"yyyy-MM-dd"`; instantes como `ToUniversalTime().ToString("O")`; dias via `ToStorage`/`ParseStorage`. Usar `CultureInfo.InvariantCulture` em toda formatação. Abrir uma conexão por operação.

- [ ] **Step 1: Escrever os testes que falham**
  - `Add_then_Get_round_trips_every_field` — meta com título `"Ler 📚 ação"`, `Recurrence.Daily`, `Target=8`, `Color=Pink`, `ActiveFrom=09:00`, `ActiveUntil=18:30`, `ActiveDays=Monday|Sunday`, `StartDate=2026-11-01`, `EndDate=null`, `CompletedAt=null`, instantes com fração de segundo → `Should().Be(original)` (igualdade de record)
  - `Get_finds_goal_by_id_after_add` (o caso do GUID como BLOB)
  - `Update_persists_changes`; `Update_unknown_id_throws_GoalNotFoundException`
  - `Get_returns_null_for_soft_deleted`; `List_excludes_archived_unless_requested_and_orders_by_created_at`
  - `Data_survives_reopening_file_database` — usa um arquivo em `Path.GetTempPath()`; grava, descarta a fábrica, cria nova fábrica + `Migrate` → a meta continua lá e `user_version` segue 1 (critério da Fase 1). Chamar `SqliteConnection.ClearAllPools()` antes de apagar o arquivo.
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar `GoalRow` e `GoalRepository`.**
- [ ] **Step 4: Rodar e ver passar.**
- [ ] **Step 5: Commit** — `feat(infra): persiste metas no SQLite com Dapper`

## Task 12: Caminho XDG e composição no Desktop

**Files:**
- Create: `src/SlimeGoals.Platform.Linux/XdgPaths.cs`, `src/SlimeGoals.Infrastructure/InfrastructureServiceCollectionExtensions.cs`, `src/SlimeGoals.Desktop/CompositionRoot.cs`
- Modify: `src/SlimeGoals.Desktop/Program.cs`
- Test: `tests/SlimeGoals.Infrastructure.Tests/` (extensão de DI); criar `tests/SlimeGoals.Platform.Linux.Tests/XdgPathsTests.cs` (novo projeto de teste, adicionado ao `.slnx`)

**Interfaces:**
- Produces:
  - `static class XdgPaths { string DatabasePath(Func<string, string?> getEnv, string home); }` — `$XDG_DATA_HOME/SlimeGoals/slimegoals.db`; se a variável estiver vazia ou ausente, usa `{home}/.local/share/SlimeGoals/slimegoals.db`. As dependências são parâmetros para facilitar o teste.
  - `IServiceCollection AddSlimeGoalsInfrastructure(this IServiceCollection s, string databasePath)` — cria o diretório se não existir; registra `SqliteConnectionFactory` (singleton, `Data Source={path}`) e `IGoalRepository → GoalRepository`.
  - `static class CompositionRoot { ServiceProvider Build(string databasePath); }` no Desktop — registra `TimeProvider.System`, `GoalService` (singleton), chama `AddSlimeGoalsInfrastructure`, registra páginas e shell da Task 2. O Core continua sem nenhuma dependência: a composição fica só no head (spec §5).

- [ ] **Step 1: Escrever os testes que falham**
  - `Uses_xdg_data_home_when_set` → `/x/data/SlimeGoals/slimegoals.db`
  - `Falls_back_to_home_local_share_when_unset_or_empty` → `/home/u/.local/share/SlimeGoals/slimegoals.db`
  - `AddSlimeGoalsInfrastructure_creates_missing_directory_and_resolves_repository` (diretório temporário inexistente; resolve `IGoalRepository`)
- [ ] **Step 2: Rodar e ver falhar.**
- [ ] **Step 3: Implementar.** Em `Program.cs`: `CompositionRoot.Build(XdgPaths.DatabasePath(Environment.GetEnvironmentVariable, home))` e `MigrationRunner.Migrate` em uma conexão da fábrica antes de iniciar o Avalonia; o `ShellViewModel` vem do provider.
- [ ] **Step 4: Rodar tudo.** `dotnet test SlimeGoals.slnx` → todos aprovados; `dotnet build` sem warnings.
- [ ] **Step 5: Verificação manual.** `dotnet run --project src/SlimeGoals.Desktop`; depois `sqlite3 ~/.local/share/SlimeGoals/slimegoals.db "PRAGMA user_version; .tables"` → `1` e as 4 tabelas. Rodar o app de novo → sem erro e `user_version` continua 1.
- [ ] **Step 6: Commit e merge (após aprovação do autor)**

```bash
git add -A && git commit -m "feat(desktop): compõe Core e Infrastructure com banco no diretório XDG"
git switch main && git merge --no-ff feat/fase-1-metas-dominio -m "feat: integra domínio e persistência de metas (Fase 1)" && git branch -d feat/fase-1-metas-dominio
```

**Critério de pronto da Fase 1 (spec §12):** CRUD e `GoalPeriod` passam nos testes; os dados persistem após reiniciar (teste `Data_survives_reopening_file_database` e verificação manual da Task 12).

---

## Fora deste plano (fases seguintes)

Telas de metas e arquivamento automático ao fim (Fase 2); check-ins, `ProgressService` e conclusão de tarefa única (Fase 3); lembretes (Fase 4); relatórios e sequências (Fase 5); projetos `Platform.Android` e `SlimeGoals.Android` (Fase 7a).
