# Próximo lembrete no cartão: plano de implementação

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: superpowers:executing-plans. Passos com caixas (`- [ ]`).

**Objetivo:** cada cartão do Hoje mostra o horário do próximo lembrete de hoje, e a lista "Próximos lembretes" do fim da página sai.

**Arquitetura:** o Core não muda: `ReminderService.UpcomingTodayAsync` continua sendo a fonte. O `TodayPageViewModel` deixa de montar a coleção `Upcoming` e passa a pôr a hora em cada `TodayGoalViewModel` (pelo `Goal.Id`). Na view, um sino e a hora entram na coluna dos botões, numa segunda linha alinhada à direita.

**Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit v3.

**Spec:** `specs/Planejamento_v3.md`, Interface no Android, decisão 15.

## Restrições globais

- Formato da hora: `HH:mm` local, cultura invariante (o mesmo de hoje).
- Pausado ou sem `Reminders` na plataforma: nenhum cartão mostra a hora.
- `TreatWarningsAsErrors`: o build sai sem avisos.

## Foco da revisão

1. **Meta cumprida.** `UpcomingTodayAsync` já a exclui; o cartão não mostra a hora.
2. **Recarga.** Os cartões são recriados a cada carga; a hora vem depois deles e respeita a `version` da carga.
3. **Falha ao ler os lembretes.** Os cartões ficam sem hora, e o resto da tela continua.
4. **Largura de 320 px.** A segunda linha da coluna dos botões não aperta o título; conferir na Render.
5. **Cronômetro rodando.** Sem lembrete para a meta, a linha não aparece e o cartão não ganha altura.

---

### Task 1: hora no cartão, sem a lista

**Arquivos:**
- Modificar: `src/SlimeGoals.App/ViewModels/TodayGoalViewModel.cs`, `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`, `src/SlimeGoals.App/Views/TodayView.axaml`
- Teste: `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**
- Produz: `TodayGoalViewModel.NextReminder` (`string?`, observável) e `HasNextReminder` (`bool`).
- Remove: `TodayPageViewModel.Upcoming`, `HasUpcoming` e o registro `UpcomingReminderViewModel`.

- [ ] **Passo 1:** ajustar os testes:
  - `Today_lists_upcoming_reminders_of_today` vira `Card_shows_next_reminder_of_today`: `Card(page, "Água").NextReminder` é `"08:00"` e `HasNextReminder` é verdadeiro.
  - `Today_without_upcoming_hides_section` vira `Card_without_reminder_today_shows_no_time`: 21:00, depois da janela, `NextReminder` nulo.
  - Pausa e sem `Reminders`: trocar `HasUpcoming` e `Upcoming.Count` por `Card(...).HasNextReminder` falso.
  - Novo `Met_goal_card_shows_no_time`: meta de conclusão cumprida às 07:00 fica sem hora.
- [ ] **Passo 2:** `dotnet test --project tests/SlimeGoals.App.Tests`. Esperado: falha de compilação (`NextReminder` não existe).
- [ ] **Passo 3:** implementar.
  - Propriedade no cartão.
  - `LoadUpcomingAsync` põe a hora nos cartões pelo `Goal.Id`.
  - Sai a coleção, e na view sai a seção do fim.
  - Na view, o cartão ganha `RowDefinitions="Auto,Auto"`: o ícone e o texto ocupam as duas linhas, e a segunda linha da coluna 2 tem o sino (Lucide `bell`, `StreamGeometry` `IconBell` no `Theme.axaml`) e a hora.
- [ ] **Passo 4:** os testes do App passam; `dotnet build SlimeGoals.slnx` sai com 0 avisos; a suíte inteira passa.
- [ ] **Passo 5:** a Render (`phone`, `narrow`, `today-filter`), mais o emulador com as metas de exemplo.
- [ ] **Passo 6:** commit `feat(app): próximo lembrete no cartão da meta`.
