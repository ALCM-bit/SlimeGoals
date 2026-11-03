# Fase 7a — Android: interface — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O SlimeGoals roda no Android (emulador `slimegoals-api36`) com todas as telas usáveis no celular:
- Hoje, Metas (com o formulário), Relatórios e Configurações;
- navegação inferior abaixo de 720 px de largura;
- botão Voltar do sistema;
- dados num SQLite privado do app.

Os lembretes no Android ficam para a 7b. Nesta fase, o app só não mostra o que ainda não funciona lá.

**Architecture:**
- **App (compartilhado):**
  - `PlatformCapabilities` diz o que cada plataforma oferece; os ViewModels escondem o que falta;
  - o `ShellView` fica adaptativo: barra lateral a partir de 720 px, navegação inferior abaixo disso, marcando a raiz com a classe `compact`;
  - as Views ajustam o layout por estilos `.compact …` em `Controls.axaml`;
  - `ShellViewModel.GoBack()` define o que o Voltar faz;
  - a `App` ganha o ramo `IActivityApplicationLifetime` (Android).
- **`src/SlimeGoals.Android`** (novo head, `net10.0-android`):
  - `Application` monta o container;
  - `MainActivity` (Avalonia);
  - composição própria: Core, Infrastructure e App, sem Platform.Linux;
  - recursos (tema escuro, ícone do slime, splash).
- **`tools/SlimeGoals.Render`** (novo, fora da solução principal): renderiza as páginas com Avalonia.Headless em tamanhos de celular e desktop. Serve para conferir layouts sem abrir janela, numa revisão ou num teste de fumaça.

**Tech Stack:**
- as mesmas versões das Fases 1–6 (Avalonia 12.1.4, CommunityToolkit.Mvvm, Dapper, Microsoft.Data.Sqlite, xUnit v3, AwesomeAssertions);
- pacotes novos: `Avalonia.Android` 12.1.4 e `Xamarin.AndroidX.Core.SplashScreen` 1.0.1.15, as versões do spike; `Avalonia.Headless` e `Avalonia.Skia` 12.1.4, só na ferramenta de renderização;
- toolchain do spike `specs/spikes/android-alarme.md`, já instalado nesta máquina: workload `android` 36.1.118, JDK 21, `~/Android/Sdk`, AVD `slimegoals-api36`.

**Spec:** `specs/Planejamento_v3.md` v1.4:
- §1 e §3 (Android no MVP; Core e UI compartilhados);
- §5 e §6 (head Android, composição nos heads);
- §7 (navegação inferior no Android, a v3, com Hoje, Metas, Relatórios e Configurações; ViewModels compartilhados; breakpoint de 720 px);
- §11 (tokens e alvos de toque ≥ 44 px);
- §12, linha 7a ("Head Android, shell com navegação inferior (v3), layouts adaptados"; critério de pronto: "Todas as telas usáveis no celular");
- `specs/spikes/android-alarme.md` ("Outras descobertas", "Decisões resultantes" 1, 4 e 5).

**Fora deste plano:**
- lembretes, notificações e permissões no Android (7b);
- APK assinado, versão mínima confirmada num aparelho real e licenças no pacote (7c);
- modo paisagem caprichado: funciona, mas sem layout próprio;
- tablets: acima de 720 px valem os layouts do desktop.

## Decisões deste plano (o autor confirma na revisão)

1. **`PlatformCapabilities(Reminders, Autostart, WindowClose, Quit)`**, um registro por head.
   - **Desktop:** tudo verdadeiro.
   - **Android 7a:** tudo falso. A 7b liga `Reminders`.
   - **Hoje:** sem `Reminders`, esconde "Próximos lembretes" e o botão "Pausar lembretes".
   - **Configurações:** sem `Autostart`, esconde a seção "Geral"; sem `WindowClose`, esconde "Janela". A seção "Lembretes" (adiar) continua, porque já vale na 7b.
   - **Barra:** sem `Quit`, o "Sair" não aparece.
2. **Layout adaptativo por largura, não por plataforma.**
   - Abaixo de 720 px de largura disponível, o `ShellView` usa a classe `compact`: navegação inferior com ícone e rótulo, sem a barra lateral, e margens menores.
   - Uma janela estreita no desktop também fica `compact`.
   - O tamanho mínimo da janela no desktop (900 px) continua: no Linux, o `compact` só aparece se o autor mudar isso, o que fica fora deste plano.
3. **Navegação inferior:** os quatro itens da spec, cada um com ícone do Lucide (ISC) e rótulo:
   - Hoje: "sun";
   - Metas: "list-checks";
   - Relatórios: "chart-column";
   - Configurações: "sliders-horizontal".

   O item ativo usa `Selected` e `PrimarySoft`, como o item ativo da barra lateral.
4. **Voltar (Android):** `ShellViewModel.GoBack()`:
   - fecha o editor de meta aberto;
   - se não houver editor e a página não for Hoje, volta para Hoje;
   - em Hoje, devolve falso, e o sistema manda o app para segundo plano (comportamento padrão).
   - No desktop, ninguém chama esse método.
5. **Dados no Android:** `<FilesDir>/slimegoals.db`, no armazenamento privado. As migrações rodam ao abrir. Desinstalar o app apaga os dados (padrão do Android).
6. **Lembretes na 7a:**
   - o Android usa um `INotificationService` que não faz nada (`NoopNotificationService`, no head Android) só para satisfazer o `ReminderService`, que a tela Hoje usa;
   - não há `ReminderHost` nem agendador;
   - a 7b troca isso pelo `AlarmManager`.
7. **Identidade:** `ApplicationId` = `dev.alcm.slimegoals`, rótulo "SlimeGoals", `SupportedOSPlatformVersion` = 26 (decisão 4 do spike), APK Debug.
   - O ícone é o slime, gerado nos tamanhos `mipmap-*` a partir de `src/SlimeGoals.App/Assets/icon.svg`.
   - Tema escuro com barras do sistema em `#0F0D15` (token `Bg`).
   - Orientação livre; mudar a configuração não recria a Activity.
   - O teclado redimensiona a tela (`AdjustResize`).
8. **Solução:** o head Android fica fora de `SlimeGoals.slnx`, para o `dotnet test` do dia a dia não exigir o workload `android`. Ele entra numa nova `SlimeGoals.Android.slnx` (Core, Infrastructure, App e Android).
9. **Ferramenta de renderização:**
   - fica em `tools/SlimeGoals.Render`, fora das duas soluções;
   - semeia um banco temporário com metas de exemplo;
   - grava PNGs de cada página em 412×915 (celular) e 1100×720 (desktop);
   - a revisão usa essas imagens para conferir os layouts.

## Global Constraints

- Tudo das Fases 1–6 continua valendo:
  - `TreatWarningsAsErrors`, `Nullable`, versões centralizadas;
  - Core sem dependências (`ArchitectureTests`); App sem referência a Platform.Linux nem a Android; o head Android não referencia Platform.Linux;
  - textos em pt-BR e identificadores em inglês;
  - ViewModels sem tipos visuais do Avalonia, bindings compilados, cores e raios só por `DynamicResource`, alvos de toque ≥ 44 px.
- O head Android herda o `Directory.Build.props` da raiz e só sobrescreve `TargetFramework` com `net10.0-android` (spike, "Outras descobertas").
- Textos exatos:
  - rótulos da navegação inferior: `"Hoje"`, `"Metas"`, `"Relatórios"`, `"Configurações"` (os `Title` das páginas);
  - nome do app: `"SlimeGoals"`.
- Breakpoint: `720` px (largura do `ShellView`).
- Branch `feat/fase-7a-android-ui`.
  - Commits: Conventional Commits com escopo `app`, `android`, `tools`, `build` ou `specs`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  - Integração por PR.
- **Testes:**
  - `dotnet test --solution SlimeGoals.slnx`, que não compila o Android;
  - `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`.
- Emulador: AVD `slimegoals-api36`. Capturas com `adb exec-out screencap -p > arquivo.png`, em pastas temporárias. Nunca use o banco real do Linux.

## Review Focus

1. **Girar a tela, ou voltar ao app depois de muito tempo** (o sistema pode recriar a Activity): a navegação, a página atual e os dados continuam. Nada é recriado de forma que duplique metas ou perca um cronômetro rodando. Coberto nas Tasks 4 e 5 (fumaça no emulador).
2. **Voltar com o formulário de meta aberto e alterações não salvas:** fecha o formulário sem salvar, como o botão de voltar da própria tela. Coberto na Task 1.
3. **Teclado na tela ao editar uma meta:** o campo em foco continua visível e o botão Salvar fica alcançável por rolagem. Coberto nas Tasks 3 e 5.
4. **Tela estreita com textos longos** (título de meta grande, "5 disparados · 3 respondidos", rótulos da navegação): nada fica cortado no meio nem sobreposto em 412 px. Coberto na Task 3 (renderizações).
5. **Abrir o app pela primeira vez no Android:** sem banco, ele cria o banco, roda as migrações e mostra "Nenhuma meta para hoje. Crie uma em Metas.", sem erro. Coberto na Task 5.

---

## Mapa de arquivos

```
Directory.Packages.props                              (alterado: Avalonia.Android, SplashScreen, Avalonia.Headless, Avalonia.Skia)
SlimeGoals.Android.slnx                               (novo)
src/SlimeGoals.App/
  Hosting/PlatformCapabilities.cs                     (novo)
  ViewModels/ShellViewModel.cs, TodayPageViewModel.cs, SettingsPageViewModel.cs (alterados)
  Views/ShellView.axaml(.cs)                          (alterados: adaptativo, Voltar)
  Views/TodayView.axaml, GoalsView.axaml, GoalEditView.axaml, ReportsView.axaml, SettingsView.axaml (alterados: compact)
  Styles/Controls.axaml, Styles/Theme.axaml           (alterados: estilos compact, ícones da navegação)
  App.axaml.cs                                        (alterado: IActivityApplicationLifetime)
src/SlimeGoals.Desktop/CompositionRoot.cs             (alterado: PlatformCapabilities)
src/SlimeGoals.Android/                               (novo head)
  SlimeGoals.Android.csproj, AndroidApplication.cs, MainActivity.cs, AndroidCompositionRoot.cs,
  NoopNotificationService.cs, Properties/AndroidManifest.xml, Resources/…, make-android-icons.sh
tools/SlimeGoals.Render/                              (novo)
tests/SlimeGoals.App.Tests/…                          (alterados/novos)
README.md, THIRD-PARTY-NOTICES.md, specs/Planejamento_v3.md (alterados)
```

---

### Task 1: `PlatformCapabilities` e Voltar no `ShellViewModel`

**Files:**
- Create: `src/SlimeGoals.App/Hosting/PlatformCapabilities.cs`
- Modify: `src/SlimeGoals.App/ViewModels/ShellViewModel.cs`, `TodayPageViewModel.cs`, `SettingsPageViewModel.cs`, `src/SlimeGoals.Desktop/CompositionRoot.cs`
- Test: `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs`, `ViewModels/TodayPageViewModelTests.cs`, `ViewModels/SettingsPageViewModelTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace SlimeGoals.App.Hosting;
/// O que a plataforma oferece; os ViewModels escondem o que não existe nela.
public sealed record PlatformCapabilities(bool Reminders, bool Autostart, bool WindowClose, bool Quit)
{
    public static PlatformCapabilities Desktop { get; } = new(true, true, true, true);
}
```

- **`ShellViewModel`:**
  - parâmetro opcional novo no fim do construtor: `PlatformCapabilities? capabilities = null`, que vale `Desktop` quando nulo;
  - `public bool ShowQuit => capabilities.Quit`;
  - `public bool GoBack()`, com as regras abaixo.
- **`TodayPageViewModel`:** parâmetro opcional no fim, `PlatformCapabilities? capabilities = null` (nulo vale `Desktop`).
  - `public bool ShowReminders`.
  - Sem lembretes, `CanPause` é falso, `Upcoming` fica vazio, `HasUpcoming` é falso e `LoadUpcomingAsync` não é chamado.
- **`SettingsPageViewModel`:** parâmetro opcional `PlatformCapabilities? capabilities = null`.
  - `public bool ShowGeneral` (`Autostart`) e `public bool ShowWindow` (`WindowClose`).
  - A seção "Lembretes" aparece sempre.
- **Composição do desktop:** `services.AddSingleton(PlatformCapabilities.Desktop)`.

Regras do `GoBack()`:
- se `CurrentPage` é `GoalsPageViewModel` com `Editor` aberto, fecha o editor sem salvar, pelo mesmo caminho do botão de voltar do formulário, e devolve verdadeiro;
- se não, se `CurrentPage` não é a primeira página (Hoje), navega para `Pages[0]` e devolve verdadeiro;
- se não, devolve falso.

Para fechar o editor, exponha no `GoalsPageViewModel` um método `public bool CloseEditorIfOpen()` que chama o `CloseEditor(saved: false)` existente.

- [ ] **Step 1: Write the failing tests**
  - `ShellViewModelTests`:
    - `GoBack_closes_goal_editor_first`: em Metas, com o editor aberto pelo `NewCommand`, `GoBack()` devolve verdadeiro, `Editor` fica nulo e a página continua Metas.
    - `GoBack_returns_to_today_then_lets_system_handle`: em Relatórios, `GoBack()` devolve verdadeiro e a página vira Hoje; um segundo `GoBack()` devolve falso.
    - `ShowQuit_follows_capabilities`: `new PlatformCapabilities(false, false, false, false)` leva a `ShowQuit` falso; o padrão é verdadeiro.
  - `TodayPageViewModelTests`:
    - `Without_reminders_hides_upcoming_and_pause`: com `Reminders=false` e uma meta com lembrete hoje, `Upcoming` fica vazio, `HasUpcoming`, `CanPause` e `ShowReminders` são falsos.
  - `SettingsPageViewModelTests`:
    - `Sections_follow_capabilities`: `Autostart=false` e `WindowClose=false` levam a `ShowGeneral` e `ShowWindow` falsos; o padrão é verdadeiro nos dois.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement**, e nas Views ligue as visibilidades:
  - "Sair" do `ShellView` → `ShowQuit`;
  - "Próximos lembretes" e o botão Pausar do `TodayView` → `ShowReminders`;
  - seções do `SettingsView` → `ShowGeneral` e `ShowWindow`.

  Registre `PlatformCapabilities.Desktop` no `CompositionRoot`.
- [ ] **Step 4: Run tests + build** — Expected: PASS, 0 avisos.
- [ ] **Step 5: Commit** — `feat(app): recursos por plataforma e Voltar na navegação`

---

### Task 2: Ferramenta de renderização headless

**Files:**
- Create: `tools/SlimeGoals.Render/SlimeGoals.Render.csproj`, `tools/SlimeGoals.Render/Program.cs`, `tools/SlimeGoals.Render/README.md`
- Modify: `Directory.Packages.props` (`Avalonia.Headless`, `Avalonia.Skia` 12.1.4)

**Interfaces:**
- Consumes: as Views e os ViewModels do App; repositórios da Infrastructure; serviços do Core.
- Produces: `dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all]`, que grava um PNG por página e tamanho:
  - nomes: `today-phone.png`, `goals-phone.png`, `goal-edit-phone.png`, `reports-week-phone.png`, `reports-month-phone.png`, `settings-phone.png`, mais as mesmas variações com `-desktop`;
  - cada imagem é o `ShellView` inteiro, com a página atual escolhida, em 412×915 (phone) e 1100×720 (desktop).

Como funciona:
- **Aplicação:** uma `Application` própria (`RenderApp`) carrega o tema e os estilos do App: `Theme.axaml` e `Controls.axaml` por `avares://SlimeGoals.App/...`, os conversores `IconGeometry` e `GoalColorBrush`, e os `DataTemplates` das páginas, iguais aos do `App.axaml`.
- **Plataforma:** `UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`.
- **Dados:**
  - banco SQLite temporário em `Path.GetTempPath()/slimegoals-render-<guid>.db`, com `MigrationRunner`;
  - quatro metas de exemplo criadas pelo `GoalService`: diária de quantidade "Beber água", 8 copos; diária de tempo "Leitura", 30 min; semanal de tempo "Treino", 5 h; semanal de conclusão "Aula de inglês";
  - registros dos últimos 10 dias, gravados direto no `CheckinRepository`;
  - o relógio é `TimeProvider.System`.
- **Telas:**
  - o editor sai do `GoalsPageViewModel.NewCommand`;
  - para cada página, ajuste `CurrentPage`, rode `Dispatcher.UIThread.RunJobs()` até a carga terminar e capture com `CaptureRenderedFrame()`;
  - um projeto `net10.0` com `TreatWarningsAsErrors`; ele herda o `Directory.Build.props`.
- **Capacidades:** a variação phone usa as capacidades do Android (`new PlatformCapabilities(false, false, false, false)`), e a desktop usa `PlatformCapabilities.Desktop`.

- [ ] **Step 1: Implement** o projeto e o README, com o comando de uso e o que ele grava.
- [ ] **Step 2: Run** `dotnet run --project tools/SlimeGoals.Render -- <scratch>/render all`. Expected: 12 PNGs.
  - Abra `today-desktop.png` e `reports-week-desktop.png` com a ferramenta de leitura de imagens e confira que batem com o app de hoje: barra lateral, tiles e barras.
  - Os PNGs de celular ainda mostram o layout de desktop espremido. É o "antes" da Task 3: anexe dois deles no relatório.
- [ ] **Step 3: Confirm** que `dotnet test --solution SlimeGoals.slnx` não compila a ferramenta, porque ela está fora da solução.
- [ ] **Step 4: Commit** — `build(tools): renderização headless das telas para conferência visual`

---

### Task 3: Shell adaptativo e layouts `compact`

**Files:**
- Modify: `src/SlimeGoals.App/Views/ShellView.axaml(.cs)`, `TodayView.axaml`, `GoalsView.axaml`, `GoalEditView.axaml`, `ReportsView.axaml`, `SettingsView.axaml`, `Styles/Controls.axaml`, `Styles/Theme.axaml`, `THIRD-PARTY-NOTICES.md`
- Test: renderizações da Task 2, mais `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs` se for preciso expor algo para a navegação inferior.

**Interfaces:**
- Consumes: `ShellViewModel.Pages`, `CurrentPage`, `NavigateCommand` e `ShowQuit` (Task 1).
- Produces:
  - o `ShellView` marca a raiz com a classe `compact` quando `Bounds.Width < 720`; o code-behind recalcula em `SizeChanged`;
  - `PageViewModel` ganha `public virtual string IconKey => ""`, e as quatro páginas devolvem `"NavToday"`, `"NavGoals"`, `"NavReports"` e `"NavSettings"`;
  - um `IValueConverter` já existente ou novo resolve essas chaves para a `StreamGeometry` de mesmo nome (`IconNavToday` …) do `Theme.axaml`.

Ícones (Lucide, ISC, traço 24×24; confira o desenho na renderização):

```
IconNavToday    = M 12 8 A 4 4 0 1 0 12 16 A 4 4 0 1 0 12 8 Z M 12 2 V 4 M 12 20 V 22 M 4.93 4.93 L 6.34 6.34 M 17.66 17.66 L 19.07 19.07 M 2 12 H 4 M 20 12 H 22 M 6.34 17.66 L 4.93 19.07 M 19.07 4.93 L 17.66 6.34
IconNavGoals    = M 3 17 L 5 19 L 9 15 M 3 7 L 5 9 L 9 5 M 13 6 H 21 M 13 12 H 21 M 13 18 H 21
IconNavReports  = M 3 3 V 19 A 2 2 0 0 0 5 21 H 21 M 18 17 V 9 M 13 17 V 5 M 8 17 V 14
IconNavSettings = M 21 4 H 14 M 10 4 H 3 M 21 12 H 12 M 8 12 H 3 M 21 20 H 16 M 12 20 H 3 M 14 2 V 6 M 8 10 V 14 M 16 18 V 22
```

Layout `compact` (`ShellView` abaixo de 720 px):
- **Barras:** some a barra lateral; aparece embaixo uma barra de navegação com fundo `Panel`, borda superior `Border` e 4 colunas iguais.
  - Cada item é um botão com o ícone (22 px) em cima e o rótulo (12 px) embaixo, com altura de pelo menos 56 px.
  - Ativo = fundo `Selected` em pílula atrás do ícone e texto `PrimarySoft`; inativo = `Muted`.
- **Margens:** o conteúdo ganha margem de 12 px (desktop: 14 px).
- **Título da página** com 22 px (desktop: o atual).
- **Hoje:**
  - os 4 tiles do resumo viram uma grade 2×2; troque o `Grid` de 4 colunas por um `UniformGrid` com `Columns` 4, ou 2 em `.compact`;
  - "Próximos lembretes" e os cartões em coluna única, como já são.
- **Metas:** o cabeçalho (título e botão "Nova meta") fica numa linha, com o botão alinhado à direita, e os cartões ocupam a largura toda.
- **Formulário de meta:**
  - largura fluida em `.compact`: tire o `Width="640"` e o `MaxWidth` fixo e use `MaxWidth` 640 só fora do `compact`;
  - os campos lado a lado ficam um embaixo do outro;
  - a barra de ações (Salvar e Excluir) vai junto com a rolagem, e o `ScrollViewer` envolve todo o formulário.
- **Relatórios:**
  - os tiles "Cumprido" e "Lembretes" ficam em coluna única (`UniformGrid` com `Columns` 2, ou 1 em `.compact`);
  - o segmento Semana/Mês vai para baixo do título;
  - as barras seguem com 7 colunas, com largura de barra de 22 px em `.compact`.
- **Configurações:** os cartões ocupam a largura toda.

Como fazer: os estilos vão em `Controls.axaml`, com seletores de descendente a partir da raiz, como `UserControl.compact UniformGrid.tiles` ou `.compact Border.page`. Use classes nas Views, e não `x:Name`, para os estilos as encontrarem. Nada de lógica de layout nos ViewModels.

- [ ] **Step 1: Implement** shell, ícones, `IconKey` e estilos e Views.
- [ ] **Step 2: Render** `dotnet run --project tools/SlimeGoals.Render -- <scratch>/render all` e confira com a ferramenta de leitura de imagens.
  - **Celular, cada um dos 6 PNGs:**
    - navegação inferior com 4 itens e o ativo destacado;
    - nada cortado ou sobreposto em 412 px;
    - tiles do Hoje em 2×2;
    - formulário usável.
  - **Desktop:** igual ao de antes da tarefa; compare com os PNGs da Task 2.
  - Itere até ficar certo e anexe a lista de arquivos conferidos no relatório.
- [ ] **Step 3: Tests + build** (`dotnet test --solution SlimeGoals.slnx`) — Expected: PASS, 0 avisos.
- [ ] **Step 4:** `THIRD-PARTY-NOTICES.md` lista os 4 ícones da navegação junto dos outros do Lucide.
- [ ] **Step 5: Commit** — `feat(app): navegação inferior e layouts para tela estreita`

---

### Task 4: `App` com o ciclo de vida do Android e Voltar no `ShellView`

**Files:**
- Modify: `src/SlimeGoals.App/App.axaml.cs`, `src/SlimeGoals.App/Views/ShellView.axaml.cs`

**Interfaces:**
- Consumes: `ShellViewModel.GoBack()` (Task 1).
- Produces:
  - **`App.OnFrameworkInitializationCompleted`**, novo ramo `IActivityApplicationLifetime activity`, antes do `ISingleViewApplicationLifetime`:
    - `activity.MainViewFactory = () => new ShellView { DataContext = shell }`;
    - os `IAppHostedService` iniciam como no desktop, uma vez por processo;
    - sem bandeja.
  - **`ShellView`:** ao entrar na árvore visual (`OnAttachedToVisualTree`), assina `TopLevel.GetTopLevel(this)!.BackRequested` e define `e.Handled = (DataContext as ShellViewModel)?.GoBack() == true`; ao sair (`OnDetachedFromVisualTree`), desassina.

Recriar a Activity chama a fábrica de novo. O `ShellViewModel` é singleton do container, então a página atual e o estado se mantêm, e cada `ShellView` novo assina o Voltar de novo.

- [ ] **Step 1: Implement.** Confira os nomes exatos das APIs do Avalonia 12.1.4 (`IActivityApplicationLifetime.MainViewFactory`, `TopLevel.BackRequested`, `RoutedEventArgs.Handled`) no pacote; o spike usou `MainViewFactory`.
- [ ] **Step 2: Build + tests** — Expected: PASS, 0 avisos. O desktop não muda: rode o app com `XDG_DATA_HOME` temporário por 10 s, sem exceção.
- [ ] **Step 3: Commit** — `feat(app): ciclo de vida do Android e botão Voltar`

---

### Task 5: Head Android

**Files:**
- Create:
  - `src/SlimeGoals.Android/SlimeGoals.Android.csproj`, `AndroidApplication.cs`, `MainActivity.cs`, `AndroidCompositionRoot.cs`, `NoopNotificationService.cs`, `Properties/AndroidManifest.xml`;
  - `Resources/values/colors.xml`, `Resources/values/styles.xml`, `Resources/values-v31/styles.xml`, `Resources/drawable/splash_screen.xml`;
  - `Resources/mipmap-{mdpi,hdpi,xhdpi,xxhdpi,xxxhdpi}/ic_launcher.png`, `make-android-icons.sh`;
  - `SlimeGoals.Android.slnx`.
- Modify: `Directory.Packages.props` (`Avalonia.Android` 12.1.4, `Xamarin.AndroidX.Core.SplashScreen` 1.0.1.15)

**Interfaces:**
- Consumes: Core, Infrastructure (`AddSlimeGoalsInfrastructure(databasePath)`, `SqliteConnectionFactory`, `MigrationRunner`), App (`App`, ViewModels, `PlatformCapabilities`, `ReminderFormatter`).
- Produces: o APK Debug `dev.alcm.slimegoals`, que abre na tela Hoje.

Especificação:
- **csproj:**
  - `OutputType` Exe, `TargetFramework` `net10.0-android`, `SupportedOSPlatformVersion` 26;
  - `ApplicationId` `dev.alcm.slimegoals`, `ApplicationTitle` "SlimeGoals", `ApplicationVersion` 1, `ApplicationDisplayVersion` = `$(Version)`;
  - `AndroidPackageFormat` apk;
  - referências: Core, Infrastructure, App, `Avalonia.Android` e o SplashScreen.
- **`AndroidApplication : AvaloniaAndroidApplication<App>`**, com `[Application]`:
  - monta o container (`AndroidCompositionRoot.Build(dbPath)`) e roda as migrações no `OnCreate`, antes do `base.OnCreate()`;
  - sobrescreve `CreateAppBuilder()` para `AppBuilder.Configure(() => new App(_services))`. A `App` não tem construtor sem parâmetros: confira a forma certa no pacote. O spike usou `CustomizeAppBuilder`;
  - `dbPath = Path.Combine(FilesDir!.AbsolutePath, "slimegoals.db")`.
- **`MainActivity : AvaloniaMainActivity`**, com `[Activity(Label = "SlimeGoals", Theme = "@style/SlimeGoals.Theme", Icon = "@mipmap/ic_launcher", MainLauncher = true, ConfigurationChanges = Orientation | ScreenSize | UiMode | ScreenLayout | SmallestScreenSize, WindowSoftInputMode = SoftInput.AdjustResize)]`.
- **`AndroidCompositionRoot.Build(string databasePath)`:**
  - as mesmas linhas Core, App e Infrastructure do `CompositionRoot` do desktop, menos Platform.Linux, autostart, logind, bandeja, `MainWindowActivator`, `DesktopAppLifetime`, `ReminderHost`, `WindowCloseBehavior` e `SleepResumeHost`;
  - mais `INotificationService` → `NoopNotificationService` e `PlatformCapabilities` → `new(false, false, false, false)`;
  - `ValidateOnBuild = true`.
- **`NoopNotificationService`:** implementa a porta sem fazer nada, com um comentário dizendo que a 7b o substitui.
- **Tema:**
  - fundo `#0F0D15`, `android:statusBarColor` e `android:navigationBarColor` `#0F0D15`, ícones claros nas barras;
  - splash com fundo `#0F0D15` e o ícone;
  - siga o padrão de estilos do spike (`MyTheme.NoActionBar` e `values-v31`) com nomes próprios.
- **Ícone:** `make-android-icons.sh` gera os `ic_launcher.png` de 48, 72, 96, 144 e 192 px a partir de `src/SlimeGoals.App/Assets/icon.svg`, com `rsvg-convert`.
- **Manifesto:** sem permissões nesta fase; `android:label="SlimeGoals"`, `android:icon="@mipmap/ic_launcher"`.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Build** — `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`. Expected: 0 avisos e 0 erros.
- [ ] **Step 3: Fumaça no emulador** (comandos do spike):
  - **Subir e instalar:**
    - suba o AVD `slimegoals-api36` (`-no-boot-anim -no-audio`, em segundo plano) e espere `adb shell getprop sys.boot_completed` = 1;
    - `dotnet build src/SlimeGoals.Android -t:Install -f net10.0-android …`;
    - `adb shell am start -W -n dev.alcm.slimegoals/<atividade>` (pegue o nome com `adb shell cmd package resolve-activity --brief dev.alcm.slimegoals`); anote o `TotalTime`.
  - **Navegar, capturando cada tela** com `adb exec-out screencap -p`:
    - Hoje vazio;
    - Metas, depois "Nova meta": preencha um título com `adb shell input text` e salve; a meta aparece em Hoje;
    - Relatórios;
    - Configurações (só "Lembretes").
  - **Voltar e girar:**
    - `adb shell input keyevent KEYCODE_BACK` com o formulário aberto fecha o formulário; em Relatórios, volta para Hoje;
    - gire com `adb shell settings put system user_rotation 1` (com `accelerometer_rotation` 0): a página e os dados continuam; volte para 0.
  - **Matar e reabrir:** `adb shell am kill dev.alcm.slimegoals`, abra de novo; a meta criada continua lá.
  - Confira as capturas com a ferramenta de leitura de imagens e liste no relatório o que cada uma mostra.
  - Se o emulador não subir nesse ambiente, faça build e install e diga exatamente o que ficou sem verificar.
- [ ] **Step 4: Commit** — `feat(android): head Android com as telas do app`

---

### Task 6: Spec e README

**Files:**
- Modify: `specs/Planejamento_v3.md`, `README.md`

- [ ] **Step 1:** README ganha a seção "Rodar no Android (desenvolvimento)": pré-requisitos (os do spike), build, emulador, install e abertura, e onde ficam os dados.
- [ ] **Step 2:** Spec:
  - **§6:** o head Android e `tools/SlimeGoals.Render`.
  - **§7:** a navegação inferior com ícones, o `compact` abaixo de 720 px, o Voltar e o que fica escondido por plataforma.
  - **§12, linha 7a:** "Implementado (verificação manual pelo autor pendente)", com o resumo.
  - **§14:** a linha "Suporte mobile do Avalonia" ganha o resultado da 7a, com o `TotalTime` medido.
  - **§15:** novo bloco "Decisões da Fase 7a (03/11/2026)" com as decisões 1–9, uma linha cada.
- [ ] **Step 3: Commit** — `docs(specs): interface do Android da Fase 7a`

---

## Verificação manual (autor, depois do PR aberto)

1. **Abrir o emulador e o app** (comandos do README).
   - O ícone do slime aparece na grade de apps.
   - O app abre em Hoje, com a navegação inferior.
2. **Criar metas dos três tipos de medida.**
   - Registre progresso: Feito, +1 e o cronômetro com ▶/■.
   - Confira em Relatórios, na semana e no mês, com o clique nas barras.
3. **Voltar e girar:**
   - o Voltar com o formulário aberto fecha o formulário;
   - em outra página, volta para Hoje;
   - em Hoje, manda o app para segundo plano;
   - girar o aparelho mantém tudo.
4. **Teclado:** editar o título de uma meta não esconde o campo, e o Salvar continua alcançável.
5. **No Linux, o desktop continua igual:** barra lateral, "Sair", lembretes e "Próximos lembretes".
