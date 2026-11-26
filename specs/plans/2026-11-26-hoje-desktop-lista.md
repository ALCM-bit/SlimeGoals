# Hoje no desktop em lista + painel lateral: plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: superpowers:executing-plans. Passos com caixas (`- [ ]`).

**Objetivo:** fora da classe `compact`, o Hoje mostra as metas em linhas e o resumo (destaque, blocos e "Esta semana") numa coluna à direita, ou numa faixa acima da lista quando a página é estreita. O celular não muda.

**Arquitetura:** o Core não muda. O `TodayPageViewModel` ganha as barras da semana (de `ReportService.GetWeekAsync`). Na view:
- os botões por medida, a tarefa do cronômetro e os painéis da meta saem para três `UserControl`, usados pelo cartão (celular) e pela linha (desktop);
- uma função pura escolhe o modo (`Phone`, `Top`, `Side`) pela largura;
- o code-behind aplica o modo como classe e ajusta as colunas das grades;
- estilos com a classe do modo trocam o modelo da meta e o lugar de cada peça.

**Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit v3.

**Spec:** `specs/Planejamento_v3.md`, decisão 16 da interface no Android (a lista das decisões 14 e 15).

## Restrições globais

- Modo `Phone` quando a janela (`TopLevel.ClientSize.Width`) é menor que `ShellView.CompactBreakpoint` (720), a mesma regra da classe `compact`.
- Fora dele: `Side` com a página a partir de 980 px de largura; abaixo, `Top`.
- No `Side`, a coluna do resumo tem 320 px, separada da lista por 16 px.
- As barras da semana usam a mesma conta dos Relatórios (metas com progresso no dia, normalizadas pela maior). Altura máxima de 80 px e mínima de 4 px; o dia de hoje fica destacado.
- `TreatWarningsAsErrors`: o build sai sem avisos.

## Foco da revisão

1. **Celular idêntico.** A extração dos controles não pode mudar um pixel do cartão; conferir a Render `phone` e `narrow` antes e depois com `magick compare`.
2. **Comandos dentro dos controles extraídos.** `$parent[UserControl]` passaria a achar o controle novo. Usar `$parent[views:TodayView]` em todos os comandos da página.
3. **Handlers do campo da tarefa.** Enter/Esc, o ✎ que põe o foco e a escolha de sugestão continuam valendo. Eles ficam no `TodayView` por eventos roteados (`Button.ClickEvent` e `AutoCompleteBox.SelectionChangedEvent`), filtrados pela classe do controle.
4. **Troca de modo com painel aberto.** Redimensionar a janela com o painel de valor ou um trecho em edição troca o modelo. O estado mora no ViewModel, então o painel reaparece aberto; conferir à mão.
5. **Falha ao ler a semana.** O cartão "Esta semana" some, e o resto do Hoje continua.

---

### Task 1: barras da semana no Hoje

**Arquivos:**
- Modificar: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`
- Teste: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**
- Produz:
  - `public sealed record TodayWeekBarViewModel(string Label, double BarHeight, bool IsToday)`, no arquivo do `TodayPageViewModel`;
  - `ObservableCollection<TodayWeekBarViewModel> WeekBars`;
  - `bool HasWeekBars` (observável).

- [ ] **Passo 1:** testes que falham:
  - `Week_bars_count_goals_with_progress_per_day`: duas metas com progresso hoje e uma ontem; a barra de hoje tem `BarHeight` 80 e a de ontem 40. São 7 barras, com os rótulos `S T Q Q S S D`.
  - `Week_bar_of_today_is_marked`: só a barra do dia atual tem `IsToday`.
  - `Week_bars_without_progress_have_minimum_height`: dia sem progresso tem `BarHeight` 4.
- [ ] **Passo 2:** `dotnet test --project tests/SlimeGoals.App.Tests`. Esperado: falha de compilação (`WeekBars` não existe).
- [ ] **Passo 3:** implementar `LoadWeekBarsAsync(int version)`, chamado depois de `LoadStreakAsync`, com a mesma guarda de `version`. Uma falha deixa a coleção vazia e `HasWeekBars` falso, sem `ErrorMessage`. Os rótulos vêm da mesma tabela dos Relatórios (`WeekLabels`); torná-la `internal static` se for preciso.
- [ ] **Passo 4:** os testes do App passam.
- [ ] **Passo 5:** commit `feat(app): barras da semana no Hoje`.

### Task 2: modo do layout e controles compartilhados da meta

**Arquivos:**
- Criar:
  - `src/SlimeGoals.App/Views/TodayLayout.cs`;
  - `src/SlimeGoals.App/Views/TodayGoalActions.axaml(.cs)`: o `StackPanel` dos botões por medida, com o tempo corrido `wide`;
  - `src/SlimeGoals.App/Views/TodayTimerTask.axaml(.cs)`: o bloco "Tarefa do cronômetro rodando";
  - `src/SlimeGoals.App/Views/TodayGoalPanels.axaml(.cs)`: os trechos, o painel de valor e o cronômetro parado.
- Modificar: `src/SlimeGoals.App/Views/TodayView.axaml`, `src/SlimeGoals.App/Views/TodayView.axaml.cs`.
- Teste: `tests/SlimeGoals.App.Tests/Views/TodayLayoutTests.cs`.

**Interfaces:**
- Produz:
  - `public enum TodayLayoutMode { Phone, Top, Side }`;
  - `public static class TodayLayout { public const double SideBreakpoint = 980; public static TodayLayoutMode For(double windowWidth, double pageWidth); }`.

- [ ] **Passo 1:** testes que falham, como `[Theory]` em `TodayLayoutTests`:
  - (412, 412) e (719, 600) → `Phone`;
  - (720, 500) e (1100, 838) → `Top`;
  - (1220, 980) e (1440, 1178) → `Side`.
- [ ] **Passo 2:** os testes falham por compilação.
- [ ] **Passo 3:** implementar `TodayLayout.For` e ver os testes passarem.
- [ ] **Passo 4:** antes de mexer na view, gravar a referência: `dotnet run --project tools/SlimeGoals.Render -- <scratch>/antes all`, mais `narrow`.
- [ ] **Passo 5:** extrair os três controles do cartão, sem mudar o XAML de dentro, só os comandos para `$parent[views:TodayView]`. Os handlers `OnEditTaskClick` e `OnTaskSelectionChanged` passam a `AddHandler` no construtor do `TodayView`:
  - o ✎ ganha a classe `edit-task`;
  - o filtro da sugestão continua pela classe `timer-task`;
  - o `OnEditTaskClick` procura o campo dentro do `TodayTimerTask` de origem, e não mais pelo `Grid` pai.
- [ ] **Passo 6:** gravar a Render de novo (`depois`) e comparar cada PNG com `magick compare -metric AE`. Esperado: 0 em todos.
- [ ] **Passo 7:** suíte inteira e build sem avisos; commit `refactor(app): controles compartilhados da meta no Hoje`.

### Task 3: linha da meta e resumo ao lado no desktop

**Arquivos:**
- Modificar:
  - `src/SlimeGoals.App/Views/TodayView.axaml` e `.axaml.cs`;
  - `src/SlimeGoals.App/Styles/Controls.axaml`;
  - `tools/SlimeGoals.Render/Program.cs`;
  - `docs/desenvolvimento.md`.

**Interfaces:**
- Consome: `TodayLayout.For` e os três controles (Task 2); `WeekBars` e `HasWeekBars` (Task 1).

- [ ] **Passo 1:** a estrutura e o code-behind.
  - **Estrutura:** dentro do `ScrollViewer`, uma grade `PageGrid` com o resumo (`Grid` `Summary`: o destaque, os `UniformGrid.tiles` e o cartão "Esta semana") e a coluna principal (faixas, cabeçalho, filtro, aviso de vazio, lista).
  - **Code-behind:** no `SizeChanged`, pôr no `PageRoot` a classe do modo (`phone`, `top`, `side`) e ajustar as grades:
    - `PageGrid`: `*` ou, no `Side`, `*,320`, com 16 px entre as colunas;
    - `Summary`: no `Top`, `1.5*,3*`.
  - **Estilos por modo:** a linha e a coluna de cada peça, os espaçamentos e o `IsVisible` do cartão da semana (só no `Side`).
- [ ] **Passo 2:** dois `DataTemplate` em recursos, `TodayGoalCard` (o cartão de hoje, com os controles da Task 2) e `TodayGoalRow`. O `ItemsControl` das metas tem a classe `today-goals`, e um estilo `.phone ItemsControl.today-goals` põe o cartão; o padrão é a linha.
  - **A linha:**
    - grade `Auto,1.2*,1.5*,Auto,Auto`: o ícone (38 px); o título (com o ✓ de cumprida) e as tags pequenas numa linha; o `ProgressText` e a barra; o sino e a hora; o `TodayGoalActions`;
    - abaixo, `TodayTimerTask` e `TodayGoalPanels`;
    - linha de 1 px embaixo, e o conjunto num `Border` de cartão (`goal-list`) só fora do `phone`.
- [ ] **Passo 3:** a Render ganha o modo `wide` (o Hoje em 1440×900 e 1920×1080, como no esboço). Atualizar a lista de modos no `docs/desenvolvimento.md`.
- [ ] **Passo 4:** conferir os PNGs:
  - `wide` (Side);
  - `desktop`, 1100×720 (Top);
  - `phone`, `narrow` e `today-filter` (celular igual à Task 2);
  - `RenderTodayWithTimer(1100)` (cronômetro rodando na linha).

  Ajustar espaçamentos até ficar simétrico.
- [ ] **Passo 5:** suíte inteira e build sem avisos.
- [ ] **Passo 6:** rodar o app com `XDG_DATA_HOME` próprio e redimensionar a janela de 900 a 1920 px, com o painel de valor aberto.
- [ ] **Passo 7:** commit `feat(app): Hoje do desktop em lista com o resumo ao lado`.
