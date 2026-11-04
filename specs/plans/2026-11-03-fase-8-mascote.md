# Fase 8 — Mascote — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O slime aparece na tela Hoje, num cartão de destaque com o progresso da semana, e muda de pose conforme o dia: dormindo, parado, feliz ou comemorando. Critério de pronto da §12: "Imagem muda conforme o progresso".

**Architecture:**
- **App, ViewModel:** `MascotState.For(total, done, paused)` é uma função pura que escolhe a pose e a frase do balão. O `TodayPageViewModel` expõe `Mascot` e o recalcula ao fim de cada carga, que já acontece a cada mudança de dados e ao pausar ou retomar.
- **App, View:** um controle `SlimeMascot` desenha o slime do design em XAML (Path, Ellipse, gradiente), com a propriedade `Pose`. Cada pose liga as partes certas (olhos, boca, bochechas, zzz, confete) e as animações leves do design, por pseudo-classes e `Style.Animations`.
- **Hoje:** o tile "Progresso da semana" vira um cartão de destaque em gradiente, com o %, a barra, o balão e o slime. Os outros três tiles ficam numa linha abaixo.

**Tech Stack:** as mesmas versões da 7b, sem pacote novo. A animação usa a do Avalonia (`Animation`/`KeyFrame` sobre `RenderTransform` e `Opacity`).

**Spec:** `specs/Planejamento_v3.md` v1.4:
- §7 (tela Hoje);
- §11 (tokens; o `Slime — mascote` é a referência da Fase 8);
- §12, linha 8;
- desenho: `specs/design/Slime — mascote do SlimeGoals.html` (geometria e animações) e `specs/design/v3-hoje-mobile.html` (cartão de destaque com balão).

**Fora deste plano:**
- as poses "Lembrete" (alerta) e "Anotando" do design: a spec pede 3–4 poses na tela Hoje;
- o slime em outras telas, nas notificações ou no ícone (o ícone já é o slime desde a Fase 6);
- uma opção para desligar a animação (o Avalonia não expõe "reduzir movimento" de forma portátil);
- o bug do teclado da 7a (tela deslocada depois do formulário de meta).

## Decisões deste plano (o autor confirma na revisão)

1. **Quatro poses pelo dia,** contando as metas da lista "Metas de hoje" (`Goals`). `total` é quantas há, `done` quantas estão cumpridas (`IsMet`). A regra vale nesta ordem:
   1. sem metas hoje → **Dormindo**;
   2. todas cumpridas → **Comemorando** (vale mesmo com os lembretes pausados);
   3. lembretes pausados → **Dormindo**;
   4. nenhuma cumprida → **Parado**;
   5. senão → **Feliz**.
2. **Frase do balão por pose:**
   - Dormindo: "Zzz…";
   - Parado: "Vamos começar?";
   - Feliz: "Falta pouco!" quando falta uma meta, senão "Continue assim!";
   - Comemorando: "Tudo feito hoje!".
3. **Cartão de destaque no lugar do tile "Progresso da semana",** como no design v3:
   - gradiente `HeroBrush`;
   - à esquerda: "Progresso da semana", o % grande e uma barra branca;
   - à direita: o balão em cima e o slime embaixo, sem sobreposição;
   - "Em andamento", "Concluídas hoje" e "Sequência" ficam numa linha de 3 tiles, no desktop e no celular.
4. **Animação leve, tirada do design:**
   - Parado: o corpo achata e estica devagar (2,6 s, sem fim) e pisca (4 s);
   - Feliz: três pulinhos ao entrar na pose e depois pisca (3 s);
   - Dormindo: respira (4 s) e solta dois "z" (3 s, o segundo 1 s depois);
   - Comemorando: três pulinhos ao entrar e confete caindo sem fim (1,6 s, com atrasos de 0,4 s e 0,9 s).
   A animação só reinicia quando a pose muda: recarregar o Hoje com a mesma pose não repete os pulinhos. A animação roda por até 60 s depois de entrar no Hoje, mudar de pose ou reativar a janela (pseudo-classe `:live`, desligada por um temporizador de disparo único); para antes com a janela oculta ou inativa e com o app em segundo plano no Android. Depois fica o quadro parado da pose, sem gastar CPU.
5. **As cores do desenho ficam nos recursos do `SlimeMascot`** (gradiente `#B9A3F0` → `#9B7BEA` → `#6F56C4`, olhos e boca `#1A1226`, bochechas `#E58FB5`, confete `#7FD1AE`/`#E8B86D`/`#B9A3F0`, "z" `#B9A3F0`). São parte da arte, não do tema. Os textos do cartão usam tokens novos no `Theme.axaml`.
6. **Acessibilidade:** o slime tem `AutomationProperties.Name` "Slime dormindo", "Slime parado", "Slime feliz" ou "Slime comemorando". O balão é texto comum.

## Global Constraints

- Tudo das Fases 1–7b continua valendo:
  - `TreatWarningsAsErrors`, `Nullable`, versões centralizadas;
  - Core sem dependências; App sem referência a Platform.Linux nem ao Android;
  - textos em pt-BR e identificadores em inglês;
  - ViewModels sem tipos visuais, bindings compilados, cores do tema só por `DynamicResource`, alvos de toque ≥ 44 px.
- Textos exatos:
  - balão: `"Zzz…"`, `"Vamos começar?"`, `"Falta pouco!"`, `"Continue assim!"`, `"Tudo feito hoje!"`;
  - cartão: `"Progresso da semana"`;
  - nomes acessíveis: `"Slime dormindo"`, `"Slime parado"`, `"Slime feliz"`, `"Slime comemorando"`.
- Tokens novos em `Theme.axaml`: `HeroText` `#FFFFFF`, `HeroMuted` `#EDE6FD`, `HeroTrack` `#40FFFFFF`, `HeroBubble` `#FFFFFF`, `HeroBubbleText` `#2B2150`.
- Branch `feat/fase-8-mascote`. Commits: Conventional Commits com escopo `app` ou `specs`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Integração por PR.
- **Testes:** `dotnet test --solution SlimeGoals.slnx` e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, os dois sem avisos.
- **Imagens:** `dotnet run --project tools/SlimeGoals.Render -- <pasta no scratchpad> [phone|desktop|all|mascot]`. Nunca use o banco real do Linux nem mexa no app instalado em `~/.local`.

## Review Focus

1. **Lista de hoje vazia porque o dia é inativo,** mas com metas semanais ou diárias em outros dias: `Goals` vazio → Dormindo, sem divisão por zero e sem "Falta pouco!". Coberto na Task 1.
2. **Desmarcar a última meta cumprida:** sai de Comemorando para Feliz (ou Parado), na mesma carga, sem precisar trocar de tela. Coberto na Task 1.
3. **Pausar e retomar com o Hoje aberto:** a pose muda para Dormindo e volta, pela recarga que o `DataChangeNotifier` já dispara. Coberto na Task 1.
4. **Celular estreito (412 px):** o cartão de destaque e a linha de 3 tiles cabem sem cortar o % nem os rótulos; os títulos longos da Sequência continuam truncados. Coberto na Task 3 (Render `phone`).
5. **Carga com erro:** `ErrorMessage` aparece e a pose anterior continua, sem exceção na View. Coberto na Task 1.

---

## Mapa de arquivos

```
src/SlimeGoals.App/
  ViewModels/MascotState.cs                           (novo)
  ViewModels/TodayPageViewModel.cs                    (alterado)
  Views/SlimeMascot.axaml, SlimeMascot.axaml.cs       (novos)
  Views/TodayView.axaml                               (alterado)
  Styles/Theme.axaml, Styles/Controls.axaml           (alterados)
tools/SlimeGoals.Render/Program.cs, README.md         (alterados: modo mascot)
tests/SlimeGoals.App.Tests/ViewModels/MascotStateTests.cs (novo)
tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs (alterado)
README.md, specs/Planejamento_v3.md                   (alterados)
```

---

### Task 1: Pose do mascote no ViewModel

**Files:**
- Create: `src/SlimeGoals.App/ViewModels/MascotState.cs`
- Modify: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`
- Test: `tests/SlimeGoals.App.Tests/ViewModels/MascotStateTests.cs`, `TodayPageViewModelTests.cs`, `tests/SlimeGoals.Testing/InMemoryGoalRepository.cs` (`FailReadsWith`)

**Interfaces:**
- Produces:

```csharp
namespace SlimeGoals.App.ViewModels;
public enum MascotPose { Sleeping, Idle, Happy, Celebrating }
/// Pose e frase do slime na tela Hoje (decisões 1 e 2 do plano da Fase 8).
public sealed record MascotState(MascotPose Pose, string Message)
{
    public static MascotState For(int total, int done, bool paused);
}
```

- **`TodayPageViewModel`:** `[ObservableProperty] public partial MascotState Mascot { get; set; }`, que começa em `MascotState.For(0, 0, false)`. Recalcule com `For(Goals.Count, Goals.Count(g => g.IsMet), IsPaused)` no fim do `try` do `LoadAsync`, depois do `LoadUpcomingAsync` (que atualiza `IsPaused`) e só se a carga ainda for a última (`version == _loadVersion`). No `catch`, o valor anterior fica.

- [ ] **Step 1: Write the failing tests**
  - `MascotStateTests` (`[Theory]` com `InlineData`):
    - `(0, 0, false)` → `(Sleeping, "Zzz…")`;
    - `(0, 0, true)` → `(Sleeping, "Zzz…")`;
    - `(3, 0, false)` → `(Idle, "Vamos começar?")`;
    - `(3, 1, false)` → `(Happy, "Continue assim!")`;
    - `(3, 2, false)` → `(Happy, "Falta pouco!")`;
    - `(1, 0, false)` → `(Idle, "Vamos começar?")`;
    - `(3, 3, false)` → `(Celebrating, "Tudo feito hoje!")`;
    - `(3, 3, true)` → `(Celebrating, "Tudo feito hoje!")`;
    - `(3, 1, true)` → `(Sleeping, "Zzz…")`.
  - `TodayPageViewModelTests`, com o fixture existente:
    - `Mascot_follows_today_progress`: sem metas → `Sleeping`. Com "Mesa" (conclusão) e "Água" criadas → `Idle`. Depois de `CompleteCommand` em "Mesa" → `Happy` e `"Falta pouco!"`. Com "Água" até 8 → `Celebrating`. Depois de `UncompleteCommand` em "Mesa" → `Happy`.
    - `Mascot_sleeps_while_paused`: com uma meta pendente, `TogglePauseCommand` e então `LoadCommand` → `Sleeping`; retomar e recarregar → `Idle`.
    - `Mascot_keeps_pose_when_load_fails`: com uma meta cumprida (`Celebrating`), ligue `FailReadsWith = new IOException("x")` no `InMemoryGoalRepository` e recarregue → `ErrorMessage` `"Não foi possível carregar o dia."` e `Mascot.Pose` continua `Celebrating`. `FailReadsWith` é novo em `tests/SlimeGoals.Testing/InMemoryGoalRepository.cs`, no mesmo molde de `FailWritesWith`: as leituras lançam a exceção.
- [ ] **Step 2: Run to verify they fail:** `dotnet test --solution SlimeGoals.slnx`. Expected: falha de compilação (`MascotState` não existe).
- [ ] **Step 3: Implement** `MascotState` e a propriedade no ViewModel.
- [ ] **Step 4: Run tests.** Expected: PASS, 0 avisos.
- [ ] **Step 5: Commit** — `feat(app): pose do mascote conforme o dia`

---

### Task 2: Controle `SlimeMascot`

**Files:**
- Create: `src/SlimeGoals.App/Views/SlimeMascot.axaml`, `SlimeMascot.axaml.cs`
- Modify: `tools/SlimeGoals.Render/Program.cs`, `tools/SlimeGoals.Render/README.md`

**Interfaces:**
- Consumes: `MascotPose` (Task 1).
- Produces: `public partial class SlimeMascot : UserControl` com `public static readonly StyledProperty<MascotPose> PoseProperty` (padrão `Idle`). Quando `Pose` muda, liga uma única pseudo-classe (`:sleeping`, `:idle`, `:happy`, `:celebrating`) e atualiza `AutomationProperties.Name` (Global Constraints). Tamanho pela largura e altura de quem usa: o desenho fica num `Viewbox` com `Canvas` de 200×200.

- [ ] **Step 1: Desenhe o slime** a partir do `<svg>` do design, com as mesmas coordenadas no `Canvas` 200×200:
  - sombra `ellipse 100,177 66×9` preta com opacidade 0,35;
  - grupo `Hop` contendo o grupo `Body`, com `RenderTransformOrigin="50%,100%"`;
  - no corpo: o `Path` do corpo com gradiente radial (centro e origem `40%,30%`, raio 80%; paradas 0 `#B9A3F0`, 0,55 `#9B7BEA`, 1 `#6F56C4`), os dois brilhos, as bochechas (opacidade 0,5) e os olhos abertos (grupo `Eyes`, origem `50%,50%`);
  - também no corpo, os olhos fechados e as bocas `MouthIdle`, `MouthHappy` e `MouthSleep`;
  - fora do corpo: o confete (6 círculos) e os dois "z" (`TextBlock` em negrito, 22 e 16).
  Os `Path` do design valem como mini-linguagem do Avalonia sem conversão. Uma elipse girada vira `Ellipse` com `Canvas.Left/Top` e `RenderTransform="rotate(-28deg)"`.
- [ ] **Step 2: Liga as partes por pose** com `Style` sob `UserControl.Styles` (seletores `local|SlimeMascot:sleeping …`):
  - **Dormindo:** olhos fechados, `MouthSleep` e "z";
  - **Parado:** olhos abertos e `MouthIdle`;
  - **Feliz:** olhos abertos, `MouthHappy` e bochechas a 0,8;
  - **Comemorando:** olhos abertos, `MouthHappy`, bochechas a 0,8 e confete.
- [ ] **Step 3: Animações** (`Style.Animations` nos mesmos seletores; valores de `@keyframes` do design, com `RenderTransform` em `TransformOperations`, como `scale(1.07,0.92)` e `translate(0px,-30px)`):
  - `squash`: 0/100% `scale(1,1)`, 50% `scale(1.07,0.92)`;
  - `breathe`: 50% `scale(1.03,0.96)`;
  - `blink`: 0/92/100% `scale(1,1)`, 96% `scale(1,0.08)`;
  - `hop`: 0/100% `translate(0px,0px)`, 45% `translate(0px,-30px)`;
  - `floatz`: 0% opacidade 0 e `translate(0px,0px)`, 30% opacidade 1, 100% opacidade 0 e `translate(14px,-26px)`;
  - `fall`: 0% `translate(0px,-10px)` e opacidade 0, 15% opacidade 1, 100% `translate(0px,110px)` e opacidade 0.
  Por pose, como na decisão 4:
  - **Parado:** `squash` 2,6 s infinito no `Body`; `blink` 4 s infinito nos `Eyes`;
  - **Feliz:** `hop` 0,7 s ×3 no `Hop` e `squash` 0,7 s ×3 no `Body`; `blink` 3 s infinito;
  - **Dormindo:** `breathe` 4 s infinito; `floatz` 3 s infinito nos "z", o segundo com `Delay` de 1 s;
  - **Comemorando:** `hop` 0,6 s ×3 e `squash` 0,6 s ×3; `fall` 1,6 s infinito no confete, com atraso de 0,4 s nos pares e 0,9 s nos múltiplos de 3.
- [ ] **Step 4: Render:** novo modo `mascot`, que grava `mascot.png` com as quatro poses lado a lado (cada uma com 160×160, fundo `HeroBrush`, legenda embaixo com o nome acessível). Os modos atuais não mudam. Documente o modo no `README.md` da ferramenta.
- [ ] **Step 5: Verify:** `dotnet test --solution SlimeGoals.slnx`, o build do Android e o `mascot` (o quadro mostra o instante zero das animações). Expected: 0 avisos; as quatro poses batem com o design em proporção, olhos, boca, bochechas, zzz e confete.
- [ ] **Step 6: Commit** — `feat(app): slime desenhado com poses e animação`

---

### Task 3: Cartão de destaque no Hoje

**Files:**
- Modify: `src/SlimeGoals.App/Views/TodayView.axaml`, `Styles/Theme.axaml`, `Styles/Controls.axaml`

**Interfaces:**
- Consumes: `TodayPageViewModel.Mascot` (Task 1), `SlimeMascot.Pose` (Task 2).

- [ ] **Step 1: Tokens** novos no `Theme.axaml` (Global Constraints).
- [ ] **Step 2: Cartão** `Border.hero` no topo do Hoje, no lugar do tile "Progresso da semana":
  - `HeroBrush`, cantos `RadiusCard`;
  - `Grid` com duas colunas (`*,Auto`);
  - à esquerda: "Progresso da semana" (`HeroMuted`, 14), `WeekPercent` + "%" (`HeroText`, ExtraBold, 56 e 28) e `ProgressBar.hero` (170 px de largura, 10 px de altura, trilho `HeroTrack`, barra `HeroText`, valor `WeekPercent` de 0 a 100);
  - à direita: o balão (`HeroBubble`, cantos `14,14,4,14`, texto `HeroBubbleText` 13 em negrito, `Mascot.Message`) e, abaixo, o `SlimeMascot` com `Pose="{Binding Mascot.Pose}"`, de 130×130.
  - Altura mínima de 170 px no desktop.
  - Na classe `compact`: % em 44 e 22, slime de 110×110, padding 16 e altura mínima de 150 px.
- [ ] **Step 3: Tiles** "Em andamento", "Concluídas hoje" e "Sequência" numa `UniformGrid.tiles` de 3 colunas, no desktop e no celular (troque o `Columns` 4/2 dos estilos atuais). Na classe `compact`: `Border.stat` com padding `12,10`, `stat-label` com `TextWrapping="Wrap"` e `stat-value` em 24.
- [ ] **Step 4: Render** `all`. Expected:
  - desktop (1100×720) e celular (412×915): cartão inteiro, % sem corte, balão acima do slime sem encostar nele;
  - os 3 tiles na mesma linha, com os rótulos legíveis (até duas linhas no celular) e o título longo da Sequência truncado;
  - o resto do Hoje igual ao de antes.
- [ ] **Step 5: Emulador:** `dotnet build src/SlimeGoals.Android -t:Install -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, abrir o Hoje e marcar metas. Expected: a pose muda na hora e a animação roda lisa.
- [ ] **Step 6: Run tests + build.** Expected: PASS, 0 avisos.
- [ ] **Step 7: Commit** — `feat(app): cartão de destaque com o slime no Hoje`

---

### Task 4: Spec e README

**Files:**
- Modify: `specs/Planejamento_v3.md`, `README.md`

- [ ] **Step 1:** README: uma linha nas funcionalidades sobre o slime no Hoje, que muda de pose conforme o dia.
- [ ] **Step 2:** Spec:
  - **§1 e "Fora do escopo do MVP":** o mascote deixa de ser melhoria futura;
  - **§7, Hoje:** o cartão de destaque com o slime e o balão; os 3 tiles;
  - **§12, linha 8:** "Implementado (verificação manual pelo autor pendente)", com o resumo. A linha 7b perde "(verificação manual pelo autor pendente)", porque o PR #16 foi mesclado depois do teste do autor;
  - **§15:** novo bloco "Decisões da Fase 8 (03/11/2026)" com as decisões 1–6, uma linha cada. Registre que a spec pedia poses estáticas e o autor escolheu a animação leve.
- [ ] **Step 3: Commit** — `docs(specs): mascote no Hoje da Fase 8`

---

## Verificação manual (autor, depois do PR aberto)

1. **Linux:** abra o Hoje sem metas para hoje (Dormindo, "Zzz…"). Crie duas metas (Parado). Marque uma (Feliz, "Falta pouco!", três pulinhos). Marque a outra (Comemorando, confete). Desmarque uma (volta a Feliz).
2. **Pausar lembretes** com metas pendentes: o slime dorme; retomar: volta a Parado ou Feliz.
3. **Celular (emulador):** as mesmas poses; o cartão e os 3 tiles cabem na vertical e o layout lateral continua certo na horizontal.
4. **Animação:** não incomoda depois de alguns minutos na tela. Recarregar o Hoje com a mesma pose não repete os pulinhos.
