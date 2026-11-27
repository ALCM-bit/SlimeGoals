# Hoje do desktop igual ao mockup — plano de implementação

> **Para agentes:** use superpowers:executing-plans para seguir este plano tarefa a tarefa. Os passos usam caixas (`- [ ]`).

**Objetivo:** deixar o Hoje do desktop (formas `top` e `side`) e a barra lateral iguais ao mockup do autor, sem tirar funções.

**Arquitetura:** os textos novos (data, próxima notificação, rótulo dos blocos, tag principal) nascem nos ViewModels, com testes. O resto é XAML: estilos sob `DockPanel.top`/`DockPanel.side` no `TodayView` e a barra lateral do `ShellView`. O celular (`DockPanel.phone`/classe `compact`) não muda.

**Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit v3 + FluentAssertions, `FakeTimeProvider`.

**Spec:** `specs/Planejamento_v3.md`, decisão 18 da interface (com as 16 e 17).

## Restrições globais

- `TreatWarningsAsErrors`: build sem warnings.
- Datas e horas na hora local do `TimeProvider`; nomes de dia e mês em pt-BR (`CultureInfo.GetCultureInfo("pt-BR")`).
- Os segundos continuam nos textos de tempo.
- O celular não muda: toda regra visual nova fica sob `DockPanel.top`/`DockPanel.side` (Hoje) ou fora de `compact` (shell).
- Conferir as telas com `tools/SlimeGoals.Render` nos modos `desktop`, `wide` e `phone`.

## Foco da revisão

1. Lembretes pausados: o cartão "Próxima notificação" some e a bolinha do sino também.
2. Lembrete que passa com a tela aberta: no minuto seguinte, o cartão mostra o próximo (ou some), sem recarregar a página.
3. Meta com muitas tags: só uma tag e "+N"; meta sem tag não mostra nada (nem "+0").
4. Título longo com o selo "Concluída": o título corta com reticências, e o selo não é empurrado para fora.
5. Teclado: o Tab até "−"/"⋯" deixa esses botões visíveis (`:focus-within`).

---

### Tarefa 1: textos do cabeçalho e dos blocos no `TodayPageViewModel`

**Arquivos:**
- Modificar: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`
- Teste: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces produzidas:**
- `public string DateText { get; }`: "Quarta-feira, 4 de novembro" (pt-BR, primeira letra maiúscula), calculado no `Load`.
- `public string CompletedLabel`: "Concluída" com 0 ou 1, "Concluídas" com 2 ou mais; notifica junto com `Completed`.
- `public string? NextNotificationTitle`, `public string? NextNotificationIn`, `public bool HasNextNotification`.
- `internal static string RelativeIn(TimeSpan span)`: "agora" (< 1 min), "em N min", "em H h" ou "em H h M min".

- [ ] **Passo 1: testes que falham**
  - `Date_text_is_long_weekday_and_month`: `Now` (quarta, 04/11 12:00 em SP) → `"Quarta-feira, 4 de novembro"`.
  - `Completed_label_agrees_with_count`: 1 cumprida → `"Concluída"`; 2 cumpridas → `"Concluídas"`.
  - `Next_notification_is_the_earliest_reminder_today`: duas metas, "Água" às 08:00 e outra às 09:00; às 07:00 → `("Água", "em 1 h", true)`.
  - `Next_notification_hidden_when_paused`: pausado → `HasNextNotification == false`.
  - `Next_notification_moves_on_each_minute`: às 07:49, mostra "em 11 min"; `_time.Advance(1 min)` → `"em 10 min"`; depois de 08:00, mostra a próxima ou `HasNextNotification == false`.
  - `RelativeIn` em `[Theory]`: 30 s → "agora"; 11 min → "em 11 min"; 60 min → "em 1 h"; 75 min → "em 1 h 15 min".
- [ ] **Passo 2:** `dotnet test --project tests/SlimeGoals.App.Tests --filter-class "*TodayPageViewModelTests"`, que deve falhar na compilação.
- [ ] **Passo 3: implementar.** Em `LoadUpcomingAsync`, guardar a lista `upcoming` em `_upcoming` e chamar `UpdateNextNotification()`. Esse método pega o menor `At > agora` e preenche os três campos (nada se pausado). Um `ITimer` de 30 s (`_minuteTicker`), criado no `Load` quando `_reminders` está ligado e descartado com o outro, chama `UpdateNextNotification` via `OnUi`.
- [ ] **Passo 4:** rodar os testes, que devem passar.
- [ ] **Passo 5:** commit `feat(app): data, próxima notificação e rótulo dos blocos no Hoje`.

### Tarefa 2: tag principal e nome capitalizado

**Arquivos:**
- Modificar: `src/SlimeGoals.App/ViewModels/TagPickerViewModel.cs` (`TagChipViewModel`) e `src/SlimeGoals.App/ViewModels/TodayGoalViewModel.cs`
- Teste: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces produzidas:**
- `TagChipViewModel.DisplayName`: o `Name` com a primeira letra maiúscula (pt-BR).
- `TodayGoalViewModel.PrimaryTag` (`TagChipViewModel?`, a primeira de `Tags`), `MoreTagsText` (`"+5"` ou null) e `HasMoreTags`.

- [ ] **Passo 1: testes que falham**
  - `Card_shows_first_tag_and_count_of_the_rest`: meta com tags `saúde`, `treino`, `rotina` → `PrimaryTag.DisplayName` é `"Saúde"` (a primeira de `Tags`) e `MoreTagsText` é `"+2"`.
  - `Card_without_tags_has_no_primary_tag`: `(PrimaryTag, HasMoreTags)` é `(null, false)`.
  - `Card_with_one_tag_has_no_more_text`: `MoreTagsText` é `null`.
- [ ] **Passos 2 a 4:** falhar, implementar e passar, como na tarefa 1.
- [ ] **Passo 5:** commit `feat(app): tag principal da meta no Hoje`.

### Tarefa 3: barra lateral

**Arquivos:**
- Modificar: `src/SlimeGoals.App/Views/ShellView.axaml`, `src/SlimeGoals.App/Styles/Controls.axaml` (estilos `Border.sidebar` e `ListBox.nav`) e `src/SlimeGoals.App/Styles/Theme.axaml` (`IconLogout`)

- [ ] **Topo e bordas:** o `SlimeMascot` de 40 px e "SlimeGoals" (20, ExtraBold) no topo. A barra encosta na borda, sem raio nem margem: fundo `Panel` e só a borda direita (`Border` 1 px).
- [ ] **Itens:** cada item com ícone (`NavIconConverter`) de 22 px e o título (16), em altura 52. O item marcado ganha fundo `PrimarySoft` a 35% e raio 12.
- [ ] **"Sair":** botão sem fundo, com `IconLogout` e o texto, no rodapé, abaixo de uma divisória (`Border` 1 px).
- [ ] **Conferir:** `dotnet run --project tools/SlimeGoals.Render -- <pasta> desktop` e ler `today-desktop.png`, `goals-desktop.png` e `settings-desktop.png`; `phone` sem mudança.
- [ ] **Commit:** `feat(app): barra lateral do desktop com ícones`.

### Tarefa 4: cabeçalho, filtro, blocos e destaque do Hoje

**Arquivos:**
- Modificar: `src/SlimeGoals.App/Views/TodayView.axaml` e `src/SlimeGoals.App/Views/TagChip.axaml`

- [ ] **Cabeçalho:** grade com "Hoje" e `DateText` embaixo (16, `Muted`). À direita, um botão de sino (ícone 26, bolinha `Primary` de 8 px quando `HasNextNotification`) com `Flyout`/`MenuFlyout`: "Pausar lembretes" ou "Retomar lembretes" (`PauseButtonText`, `TogglePauseCommand`). Ao lado dele, o cartão "Próxima notificação" (rótulo 12 `Muted`, título 16 SemiBold) e a pílula `NextNotificationIn` (fundo `PrimarySoft`). A data, o sino e o cartão ficam só em `top`/`side`. No celular, o título continua sozinho e o "Pausar lembretes" fica onde está.
- [ ] **Lista e filtro:** "Suas metas de hoje" em `top`/`side` e "Metas de hoje" no celular. No desktop, chips com altura 48, raio 14, padding 18, fonte 15, nome `DisplayName`, sem contagem (o slot da contagem some por estilo) e o marcado com fundo `PrimarySoft` e borda `Primary`.
- [ ] **Blocos:** círculo de 40 px com ícone (▶ em `PrimarySoft`; ✓ em verde suave) e, ao lado, o número (28) sobre o rótulo (13). `CompletedLabel` no lugar de "Concluídas hoje" em `top`/`side`.
- [ ] **Destaque:** em `top`/`side`, sem o balão. O slime tem 150 px em `side` e 90 px em `top`, com três traços curtos acima e à direita (decoração, `Path` branco a 80%).
- [ ] **Conferir:** Render `wide` (1440, 1920 e 2544) e `desktop`; comparar com o mockup.
- [ ] **Commit:** `feat(app): cabeçalho, filtro e resumo do Hoje como no mockup`.

### Tarefa 5: linha da meta

**Arquivos:**
- Modificar: `src/SlimeGoals.App/Views/TodayView.axaml` (`TodayGoalRow`) e `src/SlimeGoals.App/Views/TodayGoalActions.axaml`

- [ ] **Coluna do título:** o título (17 SemiBold, com reticências) e o selo "✓ Concluída" (pílula verde suave, texto verde 13) quando `IsMet`. Embaixo, a `PrimaryTag` como pílula (ponto + `DisplayName`) e o `MoreTagsText` em `Muted`.
- [ ] **Ícone e cartão:** ícone de 64 px, raio 16; cartão com padding 22,18 e raio 18.
- [ ] **Ações:** "−", "✕" e "⋯" levam a classe `reveal`, e o sino com a hora vai para a mesma linha horizontal, à esquerda dos botões. Em `Border.today-row`, `.reveal` tem `Opacity` 0. Ela volta a 1 em `Border.today-row:pointerover` e `:focus-within`, com transição de 120 ms. O botão principal fica à direita e ocupa uma linha só, sem a segunda linha do sino.
- [ ] **Trechos:** na linha, os painéis aparecem com `IsPanelOpen || IsTimerStopped || IsChunksOpen`, e o `OpenPanelCommand` do "⋯" mostra junto a faixa dos trechos (se `HasChunks`). O cartão do celular continua com `HasChunks`.
- [ ] **Teste (VM):** `Opening_panel_keeps_chunks_toggle_reachable`. Com o painel aberto numa meta com trechos, `IsPanelOpen && HasChunks`. Se precisar de propriedade nova, que seja `ShowsRowPanels` no `TodayGoalViewModel`.
- [ ] **Conferir:** Render `wide`, `desktop` e `narrow` (a linha mais apertada é `today-timer-1100`); `phone` sem mudança.
- [ ] **Commit:** `feat(app): linha da meta do Hoje como no mockup`.

### Tarefa 6: verificação e documentação

- [ ] `dotnet build SlimeGoals.slnx` e `dotnet test --solution SlimeGoals.slnx` sem warning nem falha.
- [ ] Atualizar `tools/SlimeGoals.Render/README.md` se algum nome de arquivo mudar.
- [ ] Popular `/tmp/slimegoals-dev/data` (modo `seed`) e abrir o app para o autor.
- [ ] Commit `docs(specs): Hoje do desktop igual ao mockup` (decisão 18 e este plano).
