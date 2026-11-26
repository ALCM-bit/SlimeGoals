# Rolagem do Hoje: plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: superpowers:executing-plans. Passos com caixas (`- [ ]`).

**Objetivo:** a tela Hoje rola inteira abaixo do título, em vez de prender o destaque, os blocos, as faixas e o filtro e rolar só os cartões.

**Arquitetura:** só a view `TodayView.axaml`. O título fica fixo, como nas outras páginas. Todo o resto entra num único `ScrollViewer.page`. O `DockPanel x:Name="PageRoot"` continua sendo a raiz, porque o código da view (e o do PR #91) põe nele a classe `keyboard`.

**Stack:** Avalonia 12 (XAML).

**Spec:** `specs/Planejamento_v3.md`, Interface no Android, decisão 14.

## Restrições globais

- Não mexer em `TodayView.axaml.cs`: o PR #91 muda esse arquivo.
- Com o teclado aberto, o destaque e os blocos continuam escondidos. O seletor passa de filho direto (`DockPanel.keyboard > Border.hero`) para descendente (`DockPanel.keyboard Border.hero`, idem `UniformGrid.tiles`).
- `TreatWarningsAsErrors`: o build sai sem avisos.

## Foco da revisão

1. **Arrastar em cima dos chips.** Arrastar na vertical rola a página, e arrastar na horizontal rola só os chips.
2. **Teclado num cartão do fim da lista.** O campo continua visível acima do teclado, porque o `BringIntoView` agora age na rolagem da página.
3. **Hoje sem metas.** O aviso aparece abaixo do cabeçalho, sem a lista e sem "Próximos lembretes".
4. **Filtro sem resultado.** "Nenhuma meta de hoje com essas tags." aparece abaixo do filtro.
5. **Faixas de aviso.** Com o Pomodoro, as permissões, a sincronização e a pausa, nada sobrepõe nada e tudo rola junto.

Não há teste automático de layout de view no projeto: os itens são conferidos na Render e no emulador (Task 1).

---

### Task 1: uma rolagem só no Hoje

**Arquivos:**
- Modificar: `src/SlimeGoals.App/Views/TodayView.axaml`

- [ ] **Passo 1:** a Render antes da mudança (`dotnet run --project tools/SlimeGoals.Render -- <pasta>/antes all`), como referência.
- [ ] **Passo 2:** reestruturar.
  - Dentro do `DockPanel PageRoot`, ficam o título (`Dock="Top"`) e um `ScrollViewer Classes="page"` com um `StackPanel`.
  - O `StackPanel` leva, na ordem: o destaque, os blocos, as quatro faixas, o cabeçalho "Metas de hoje", o filtro, o aviso de lista vazia (centralizado, com `Margin="0,24"`) e um `StackPanel IsVisible="{Binding !IsEmpty}"` com os cartões, o aviso do filtro vazio e "Próximos lembretes".
  - Saem os `DockPanel.Dock` dos elementos que mudaram de lugar.
  - O seletor do teclado passa a ser de descendente.
- [ ] **Passo 3:** `dotnet build SlimeGoals.slnx` sai com 0 avisos, e `dotnet test --solution SlimeGoals.slnx` passa inteiro.
- [ ] **Passo 4:** a Render depois (`all`), comparada com a de antes.
  - Esperado: o Hoje do celular (`today-phone`, os de 320 e 360 px, filtro, cronômetro, faixa do Pomodoro) mostra as mesmas peças, sem a janela de rolagem.
  - As outras páginas saem idênticas.
- [ ] **Passo 5:** no emulador (`-s emulator-5554`), conferir os itens 1 a 5 do foco da revisão.
- [ ] **Passo 6:** commit `fix(app): Hoje rola inteiro abaixo do título`.
