# Fase 2 — Telas de metas — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Criar, editar, arquivar, desarquivar e excluir metas pela interface (tela Metas + página Editar meta), com validação ao lado de cada campo e arquivamento automático das metas cujo período terminou.

**Architecture:** Regras novas no `Core` (`GoalService`: meta arquivada somente leitura, desarquivar, fim no passado, arquivamento automático). Na `App`, o `GoalsPageViewModel` mostra a lista ou, quando `Editor` não é nulo, a página de edição (`GoalEditViewModel`), que fala só com o `GoalService`. Ícones como dados (path SVG) num catálogo C#, desenhados como traço por um conversor. O fake de repositório passa para um projeto de apoio a testes compartilhado.

**Tech Stack:** as mesmas versões da Fase 1 (Avalonia 12.1.4, CommunityToolkit.Mvvm 8.4.2, xunit.v3.mtp-v2 4.0.2, AwesomeAssertions 9.6.0, Microsoft.Extensions.TimeProvider.Testing 10.11.0). Ícones: `lucide-static` **1.66.0** (ISC), baixado só para gerar o catálogo, sem pacote novo.

**Spec:** `specs/Planejamento_v3.md` — §7 (telas), §11 (tokens e paleta de metas), §15 "Decisões da Fase 2".

## Global Constraints

- Tudo que vale na Fase 1 continua: `net10.0`, `Nullable`, `TreatWarningsAsErrors`, versões centralizadas, Core sem dependências, relógio via `TimeProvider`, textos em pt-BR e identificadores em inglês.
- "Hoje" no Core = `DateOnly.FromDateTime(time.GetLocalNow().DateTime)` (fuso local do `TimeProvider`; nos testes, `FakeTimeProvider.SetLocalTimeZone`).
- ViewModels não referenciam tipos visuais do Avalonia (nada de `IBrush`, `Geometry`); conversão para visual só em conversores/Views.
- Cores e raios só via `DynamicResource` (tokens de `Theme.axaml`, inclusive `Goal{Cor}Bg/Fg`); bindings compilados (`x:DataType` em toda View).
- Alvos de toque ≥ 44 px; foco visível com `PrimarySoft` (spec §11).
- Branch `feat/fase-2-telas-metas`; Conventional Commits (`core`, `app`, `build`, `specs`); commits terminam com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; integração por PR.
- Comando de testes: `dotnet test --solution SlimeGoals.slnx`.

## Review Focus

1. **Trocar o tipo várias vezes no formulário** (diária alvo 5 → única → semanal): em `once` o alvo vira 1 e o campo some; ao voltar para diária/semanal o alvo volta ao último valor digitado (5), não fica preso em 1. Coberto na Task 5.
2. **Meta arquivada aberta para edição por qualquer caminho** (comando chamado na lista arquivada, edição concorrente): o serviço recusa com `GoalStateException`, e a lista arquivada não oferece "Editar". Coberto nas Tasks 2 e 6.
3. **Ícone desconhecido vindo do banco** (chave removida do catálogo ou digitada à mão): a tela mostra o ícone `target` e o formulário marca `target`, sem exceção. Coberto nas Tasks 3 e 5.
4. **Horário ou data apagados no seletor** (`TimePicker`/`CalendarDatePicker` devolvem `null`): horário vazio vira erro "Informe o horário." no campo; data vazia significa "sem início/fim". Coberto na Task 5.
5. **Meta criada com fim hoje e reaberta amanhã**: fim hoje é aceito; no dia seguinte, ao abrir Metas, ela é arquivada automaticamente e aparece em "Arquivadas", sem poder ser desarquivada. Coberto nas Tasks 2 e 6.

---

## Mapa de arquivos

```
src/SlimeGoals.Core/
  GoalStateException.cs                         (novo)
  Services/GoalService.cs                       (alterado: Update/Unarchive/ArchiveExpired/fim no passado)
src/SlimeGoals.App/
  Goals/GoalIcons.cs                            (novo: catálogo de 16 ícones, path data Lucide)
  Goals/GoalFormatting.cs                       (novo: textos de resumo em pt-BR)
  Converters/IconKeyToGeometryConverter.cs      (novo)
  Converters/GoalColorToBrushConverter.cs       (novo)
  ViewModels/PageViewModel.cs                   (alterado: Activate())
  ViewModels/ShellViewModel.cs                  (alterado: chama Activate())
  ViewModels/GoalsPageViewModel.cs              (reescrito)
  ViewModels/GoalItemViewModel.cs               (novo)
  ViewModels/GoalEditViewModel.cs               (novo)
  ViewModels/OptionViewModels.cs                (novo: DayOption, IconOption, ColorOption, IntervalOption)
  Views/GoalsView.axaml(.cs)                    (reescrito)
  Views/GoalEditView.axaml(.cs)                 (novo)
  Styles/Controls.axaml                         (alterado: estilos de cartão, campo, chip, botão)
  App.axaml                                     (alterado: DataTemplate do editor, conversores)
src/SlimeGoals.Desktop/CompositionRoot.cs       (sem mudança de código; o DI já tem GoalService e TimeProvider)
tests/SlimeGoals.Testing/                       (novo projeto: InMemoryGoalRepository público)
tests/SlimeGoals.Core.Tests/                    (Fakes/ removido; usa SlimeGoals.Testing)
tests/SlimeGoals.App.Tests/                     (novos testes de ViewModel, GoalIcons, GoalFormatting)
THIRD-PARTY-NOTICES.md                          (Lucide, ISC)
```

---

## Task 1: Projeto de apoio a testes compartilhado

**Files:**
- Create: `tests/SlimeGoals.Testing/SlimeGoals.Testing.csproj` (classe de biblioteca, `IsPackable=false`, `IsTestProject=false`, referencia `SlimeGoals.Core`), `tests/SlimeGoals.Testing/InMemoryGoalRepository.cs`
- Delete: `tests/SlimeGoals.Core.Tests/Fakes/InMemoryGoalRepository.cs`
- Modify: `SlimeGoals.slnx` (pasta `/tests/`), `tests/SlimeGoals.Core.Tests/*.csproj` e `tests/SlimeGoals.App.Tests/*.csproj` (referência ao `SlimeGoals.Testing`; App.Tests ganha `Microsoft.Extensions.TimeProvider.Testing`)

**Interfaces:**
- Produces: `public sealed class SlimeGoals.Testing.InMemoryGoalRepository : IGoalRepository` (mesmo comportamento do fake atual, agora `public`, com `public int Count`).

- [ ] **Step 1:** Mover o fake para o projeto novo (namespace `SlimeGoals.Testing`), trocar os `using` dos testes do Core e adicionar as referências.
- [ ] **Step 2:** Rodar `dotnet test --solution SlimeGoals.slnx`. Expected: os 74 testes passam (refatoração, sem comportamento novo).
- [ ] **Step 3: Commit** — `build(tests): extrai InMemoryGoalRepository para projeto de apoio compartilhado`

---

## Task 2: Regras novas do `GoalService`

**Files:**
- Create: `src/SlimeGoals.Core/GoalStateException.cs`
- Modify: `src/SlimeGoals.Core/Services/GoalService.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/GoalServiceTests.cs` (novos testes; relógio `FakeTimeProvider` em `2026-11-02T12:00Z` e fuso local `America/Sao_Paulo` → hoje = 02/11/2026)

**Interfaces:**
- Produces:

```csharp
/// Operação impossível no estado atual da meta; Message em pt-BR, pronta para a tela.
public sealed class GoalStateException(Guid goalId, string message) : Exception(message) { public Guid GoalId { get; } }

// GoalService (acréscimos)
Task<Goal> UpdateAsync(Guid id, GoalDraft draft, CancellationToken ct = default);   // agora recusa arquivada
Task UnarchiveAsync(Guid id, CancellationToken ct = default);
Task<int> ArchiveExpiredAsync(CancellationToken ct = default);                      // devolve quantas arquivou
```

- Regras (mensagens exatas):
  - `Update` de meta arquivada → `GoalStateException`: `"Meta arquivada não pode ser editada. Desarquive-a primeiro."`
  - `Create`/`Update` com `EndDate < hoje` → `GoalValidationException` com `ValidationError(nameof(GoalDraft.EndDate), "A data de fim não pode estar no passado.")` (acumulada aos erros do `GoalValidator`; fim **igual** a hoje é aceito).
  - `Unarchive` de meta não arquivada → nada (idempotente). De `once` com `CompletedAt` → `GoalStateException`: `"Tarefa concluída não pode ser desarquivada."` De meta com `EndDate < hoje` → `GoalStateException`: `"O período desta meta terminou em {EndDate:dd/MM/yyyy}. Crie uma nova meta para continuar."` Caso válido: `ArchivedAt = null`, `UpdatedAt = agora`.
  - `ArchiveExpired`: toda meta ativa (não arquivada) com `EndDate < hoje` recebe `ArchivedAt = UpdatedAt = agora`.

- [ ] **Step 1: Testes que falham:**
  - `Update_of_archived_goal_throws_GoalStateException`
  - `Create_with_end_date_in_the_past_reports_EndDate_error` (fim 01/11/2026) e `Create_with_end_date_today_is_accepted` (fim 02/11/2026)
  - `Past_end_date_error_is_reported_together_with_validator_errors` (título vazio + fim no passado → campos `Title` e `EndDate`)
  - `Unarchive_restores_goal_and_bumps_updated_at`
  - `Unarchive_of_active_goal_does_nothing`
  - `Unarchive_of_completed_once_throws` e `Unarchive_of_expired_goal_throws` (mensagem com `"01/11/2026"`)
  - `ArchiveExpired_archives_only_active_goals_whose_end_passed` (fim ontem → arquivada; fim hoje e sem fim → intocadas; já arquivada → `ArchivedAt` original; retorno = 1)
  - `Today_uses_local_time_zone` (relógio `2026-11-03T01:30Z`, fuso São Paulo → hoje ainda é 02/11; fim 02/11 aceito)
- [ ] **Step 2:** Rodar e ver falhar (tipos e métodos inexistentes).
- [ ] **Step 3:** Implementar. Meta com `CompletedAt` é criada direto nos testes via repositório (o `CheckinService` só vem na Fase 3).
- [ ] **Step 4:** Rodar a suíte inteira. Expected: tudo passa.
- [ ] **Step 5: Commit** — `feat(core): meta arquivada somente leitura, desarquivar e arquivamento automático por fim do período`

---

## Task 3: Catálogo de ícones e textos de resumo

**Files:**
- Create: `src/SlimeGoals.App/Goals/GoalIcons.cs`, `src/SlimeGoals.App/Goals/GoalFormatting.cs`
- Modify: `THIRD-PARTY-NOTICES.md` (Lucide 1.66.0, ISC, copyright Lucide Contributors, com o texto da licença)
- Test: `tests/SlimeGoals.App.Tests/Goals/GoalIconsTests.cs`, `tests/SlimeGoals.App.Tests/Goals/GoalFormattingTests.cs`

**Interfaces:**

```csharp
public sealed record GoalIcon(string Key, string Name, string PathData);   // Name em pt-BR (acessibilidade)
public static class GoalIcons
{
    public const string DefaultKey = "target";
    public static IReadOnlyList<GoalIcon> All { get; }       // na ordem abaixo
    public static GoalIcon Get(string? key);                 // chave desconhecida/nula → DefaultKey
}

public static class GoalFormatting
{
    public static string Target(Recurrence r, int target);              // "Uma vez" | "1× por dia" | "5× por semana"
    public static string Interval(int minutes);                         // 30 → "30 min"; 60 → "1 h"; 90 → "1 h 30 min"; 1440 → "24 h"
    public static string Window(TimeOnly from, TimeOnly until);          // "09:00–18:00"
    public static string Days(WeekDays days);                           // All → "Todos os dias"; Weekdays → "Seg a sex"; Saturday|Sunday → "Fim de semana"; senão "Seg, Qua, Sex"
    public static string Period(DateOnly? start, DateOnly? end);        // "" | "De 05/11" | "Até 15/11" | "05/11 a 15/11"
    public static string Summary(Goal goal);                            // "5× por semana · a cada 2 h · 09:00–18:00 · Seg a sex" (+ " · Até 15/11" se houver período)
}
```

- Ícones (chave → arquivo Lucide → nome): `book`→`book-open` "Livro"; `run`→`footprints` "Caminhada"; `water`→`droplet` "Água"; `workout`→`dumbbell` "Exercício"; `sleep`→`bed` "Sono"; `health`→`heart` "Saúde"; `language`→`languages` "Idiomas"; `code`→`code` "Código"; `music`→`music` "Música"; `art`→`paintbrush` "Arte"; `money`→`wallet` "Dinheiro"; `nature`→`leaf` "Natureza"; `coffee`→`coffee` "Pausa"; `medicine`→`pill` "Remédio"; `star`→`star` "Favorito"; `target`→`target` "Objetivo".
- `PathData`: todos os elementos do SVG (viewBox 24×24) convertidos para a mini-linguagem de path (`path d` como está; `circle` → dois arcos `A`; `line` → `M L`; `polyline`/`polygon` → `M L…` (+`Z`); `rect` com `rx` → path com arcos), concatenados numa string só. Gerar com um script descartável no scratchpad (não versionado), baixando `https://unpkg.com/lucide-static@1.66.0/icons/<arquivo>.svg`; o arquivo C# leva um comentário com a origem, a versão e a licença.

- [ ] **Step 1: Testes que falham:** `All_has_16_unique_keys_in_order`, `Every_icon_has_name_and_path_data` (começa com `M` ou `m`), `Get_unknown_or_null_key_returns_target`; `GoalFormatting`: um `[Theory]` por função com os exemplos dos comentários acima e `Summary_includes_period_only_when_set`.
- [ ] **Step 2:** Rodar e ver falhar.
- [ ] **Step 3:** Gerar o path data, implementar as duas classes e atualizar os avisos de terceiros.
- [ ] **Step 4:** Rodar e ver passar.
- [ ] **Step 5: Commit** — `feat(app): catálogo de ícones Lucide e textos de resumo de metas`

---

## Task 4: Ativação de páginas pelo shell

**Files:**
- Modify: `src/SlimeGoals.App/ViewModels/PageViewModel.cs`, `src/SlimeGoals.App/ViewModels/ShellViewModel.cs`
- Test: `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs`

**Interfaces:**
- Produces: `public virtual void Activate() { }` em `PageViewModel`. O `ShellViewModel` chama `Activate()` na página inicial (no construtor) e sempre que `CurrentPage` muda para **outra** página (setar a mesma página ou `null` não ativa).

- [ ] **Step 1: Testes que falham** (com uma `PageViewModel` de teste que conta ativações): `Initial_page_is_activated_once`, `Navigating_activates_target_page`, `Reselecting_current_page_or_null_does_not_activate`. Ajustar a criação do shell nos testes existentes conforme o novo construtor do `GoalsPageViewModel` (Task 6) só quando a Task 6 chegar; aqui use páginas de teste.
- [ ] **Step 2:** Rodar e ver falhar. **Step 3:** Implementar. **Step 4:** Rodar e ver passar.
- [ ] **Step 5: Commit** — `feat(app): shell ativa a página ao navegar`

---

## Task 5: `GoalEditViewModel`

**Files:**
- Create: `src/SlimeGoals.App/ViewModels/GoalEditViewModel.cs`, `src/SlimeGoals.App/ViewModels/OptionViewModels.cs`
- Test: `tests/SlimeGoals.App.Tests/ViewModels/GoalEditViewModelTests.cs`

**Interfaces:**

```csharp
public sealed partial class GoalEditViewModel : ObservableObject
{
    // existing == null → criação
    public GoalEditViewModel(GoalService service, Goal? existing, Action<bool> close);  // close(saved)

    public string PageTitle { get; }                    // "Nova meta" | "Editar meta"
    public bool IsNew { get; }
    [ObservableProperty] string Title;
    [ObservableProperty] Recurrence Recurrence;
    [ObservableProperty] int Target;                    // NumericUpDown 1–99
    public bool ShowsTarget { get; }                    // Recurrence != Once
    public string TargetSuffix { get; }                 // "por dia" | "por semana"
    public IReadOnlyList<IconOption> Icons { get; }     // de GoalIcons.All; SelectedIcon
    [ObservableProperty] IconOption SelectedIcon;
    public IReadOnlyList<ColorOption> Colors { get; }   // Purple, Pink, Amber, Green
    [ObservableProperty] ColorOption SelectedColor;
    public IReadOnlyList<IntervalOption> Intervals { get; }  // 15, 30, 60, 90, 120, 180, 240, 360, 720, 1440 min (+ o valor atual, se fora da lista, em ordem)
    [ObservableProperty] IntervalOption SelectedInterval;
    [ObservableProperty] TimeSpan? ActiveFrom;          // TimePicker
    [ObservableProperty] TimeSpan? ActiveUntil;
    public IReadOnlyList<DayOption> Days { get; }       // Seg…Dom, rótulos "S","T","Q","Q","S","S","D", nome completo para acessibilidade, IsSelected
    [ObservableProperty] DateTime? StartDate;           // CalendarDatePicker
    [ObservableProperty] DateTime? EndDate;
    public string? ErrorFor(string field);              // + propriedades TitleError, TargetError, ReminderError, ActiveFromError, ActiveUntilError, ActiveDaysError, EndDateError, IconError
    [ObservableProperty] string? GeneralError;          // GoalStateException/GoalNotFoundException
    [ObservableProperty] bool IsConfirmingDelete;
    IAsyncRelayCommand SaveCommand; IRelayCommand CancelCommand;
    IRelayCommand DeleteCommand;                        // só pede confirmação (IsConfirmingDelete = true); oculto em IsNew
    IAsyncRelayCommand ConfirmDeleteCommand; IRelayCommand CancelDeleteCommand;
}
```

- Padrões de uma meta nova: `Daily`, alvo 1, ícone `target`, cor `Purple`, a cada 60 min, 08:00–20:00, todos os dias, sem início/fim.
- Ao trocar para `Once`, o alvo vira 1 e o anterior é lembrado; ao sair de `Once`, o alvo lembrado volta (padrão 1).
- Os erros só aparecem depois da primeira tentativa de salvar; a partir daí, qualquer mudança revalida na hora (`GoalValidator.Validate` + "Informe o horário." para horário nulo). Erros de `GoalValidationException` vindos do serviço (ex.: fim no passado) também vão para o campo.
- Salvar com sucesso chama `close(true)`; cancelar chama `close(false)`; excluir confirmado chama `DeleteAsync` e `close(true)`.

- [ ] **Step 1: Testes que falham** (com `GoalService` real sobre `InMemoryGoalRepository` e `FakeTimeProvider`):
  - `New_goal_starts_with_defaults`
  - `Editing_loads_all_fields_from_goal` (inclusive intervalo de 45 min fora da lista → aparece entre 30 e 60, selecionado)
  - `Unknown_icon_key_selects_target`
  - `Switching_to_once_forces_target_1_and_hides_it_then_restores_previous_target`
  - `Target_suffix_follows_recurrence`
  - `Errors_appear_only_after_first_save_attempt_then_update_live` (título vazio: sem erro → salvar → `TitleError` = "Informe um título." → digitar título → erro some)
  - `Cleared_time_reports_informe_o_horario`
  - `Save_new_creates_goal_and_closes_with_true`; `Save_existing_updates_goal_and_closes_with_true`
  - `Past_end_date_from_service_shows_on_end_date_field`
  - `Delete_requires_confirmation_then_deletes_and_closes`; `Cancel_closes_with_false_and_persists_nothing`
  - `Selected_days_map_to_WeekDays_and_none_selected_reports_error`
- [ ] **Step 2:** Rodar e ver falhar. **Step 3:** Implementar. **Step 4:** Rodar a suíte inteira e ver passar.
- [ ] **Step 5: Commit** — `feat(app): GoalEditViewModel com validação por campo e confirmação de exclusão`

---

## Task 6: `GoalsPageViewModel` e itens da lista

**Files:**
- Create: `src/SlimeGoals.App/ViewModels/GoalItemViewModel.cs`
- Modify: `src/SlimeGoals.App/ViewModels/GoalsPageViewModel.cs`, `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs` (novo construtor)
- Test: `tests/SlimeGoals.App.Tests/ViewModels/GoalsPageViewModelTests.cs`

**Interfaces:**

```csharp
public sealed partial class GoalItemViewModel(Goal goal, TimeZoneInfo zone)
{
    public Goal Goal { get; }
    public string Title; public string IconKey; public GoalColor Color;
    public string Summary;              // GoalFormatting.Summary
    public bool IsArchived;
    public bool CanUnarchive;           // arquivada e não (once concluída)
    public string? Status;              // once concluída: "Concluída em dd/MM"; arquivada: "Arquivada em dd/MM" (datas locais)
}

public sealed partial class GoalsPageViewModel(GoalService service, TimeProvider time) : PageViewModel   // fuso dos itens = time.LocalTimeZone
{
    public override string Title => "Metas";
    public ObservableCollection<GoalItemViewModel> Goals { get; }
    [ObservableProperty] bool ShowArchived;             // muda → recarrega
    [ObservableProperty] GoalEditViewModel? Editor;     // != null → página de edição
    [ObservableProperty] string? ErrorMessage;
    public bool IsEmpty { get; }                        // "Nenhuma meta ativa. Crie a primeira!" | "Nenhuma meta arquivada."
    public string EmptyMessage { get; }
    IAsyncRelayCommand LoadCommand;                      // ArchiveExpiredAsync + ListAsync; Activate() o executa
    IRelayCommand NewCommand; IRelayCommand<GoalItemViewModel> EditCommand;   // Edit só para não arquivadas
    IAsyncRelayCommand<GoalItemViewModel> ArchiveCommand, UnarchiveCommand;
}
```

- Lista ativa = não arquivadas; lista arquivada = só arquivadas (de `ListAsync(includeArchived: true)`), mais recentes primeiro por `ArchivedAt`. Ao fechar o editor (`close(saved)`), `Editor = null` e, se `saved`, recarrega. `GoalStateException` vira `ErrorMessage`; qualquer ação nova limpa a mensagem.

- [ ] **Step 1: Testes que falham:**
  - `Activate_archives_expired_goals_and_lists_active_ones`
  - `ShowArchived_lists_only_archived_newest_first`
  - `Edit_is_not_available_for_archived_items` (`EditCommand.CanExecute` falso)
  - `Archive_moves_item_out_of_active_list`
  - `Unarchive_of_completed_once_is_not_offered` (`CanUnarchive` falso) e `Unarchive_error_from_service_shows_ErrorMessage` (meta expirada)
  - `New_opens_editor_and_save_closes_it_and_reloads`
  - `Empty_state_message_depends_on_filter`
  - `Item_summary_and_status_texts`
- [ ] **Step 2:** Rodar e ver falhar. **Step 3:** Implementar; atualizar `ShellViewModelTests` para `new GoalsPageViewModel(service, time)`. **Step 4:** Rodar a suíte inteira e ver passar.
- [ ] **Step 5: Commit** — `feat(app): GoalsPageViewModel com filtro, arquivar, desarquivar e editor`

---

## Task 7: Views, estilos e verificação visual

**Files:**
- Create: `src/SlimeGoals.App/Views/GoalEditView.axaml(.cs)`, `src/SlimeGoals.App/Converters/IconKeyToGeometryConverter.cs`, `src/SlimeGoals.App/Converters/GoalColorToBrushConverter.cs`
- Modify: `src/SlimeGoals.App/Views/GoalsView.axaml`, `src/SlimeGoals.App/App.axaml`, `src/SlimeGoals.App/Styles/Controls.axaml`

**Interfaces:**
- `IconKeyToGeometryConverter`: `string` → `Geometry` via `GoalIcons.Get(key).PathData`, com cache por chave (`StreamGeometry.Parse`).
- `GoalColorToBrushConverter`: `GoalColor` + `ConverterParameter` `"Bg"`/`"Fg"` → recurso `Goal{Cor}{Bg|Fg}` de `Application.Current`.
- Ícone desenhado como `Path` com `Stroke` = Fg, `StrokeThickness=2`, pontas/juntas arredondadas, sem `Fill`, dentro de `Viewbox` 24×24, sobre `Border` com fundo Bg e raio 14.

**Layout (tokens da spec §11, layout bento v4):**
- `GoalsView`: título "Metas"; à direita botão primário "Nova meta". Abaixo, alternador segmentado "Ativas | Arquivadas" (`RadioButton`s em estilo de segmento, `SelectedStrong` no ativo). Lista de cartões (`Tile`, borda `Border`, raio 22): tile do ícone 44×44, título (16, SemiBold), resumo (`Muted`, 13), status, e ações à direita: "Editar" e "Arquivar" (ativas) ou "Desarquivar" quando `CanUnarchive` (arquivadas). Estado vazio centralizado. `ErrorMessage` numa faixa `Warning` acima da lista. Quando `Editor != null`, um `ContentControl` mostra a `GoalEditView` no lugar da lista.
- `GoalEditView`: "← Metas" (cancelar) e o título da página; campos com rótulo acima e erro abaixo em `Warning` (12): Título (`TextBox`, `MaxLength=80`); Tipo (segmento Única/Diária/Semanal); Alvo (`NumericUpDown` 1–99 + sufixo; oculto em única); Ícone (grade de 16 botões 44×44, selecionado com borda `PrimarySoft`, `AutomationProperties.Name` = nome do ícone); Cor (4 círculos 32 px, selecionado com anel); Lembrar a cada (`ComboBox` com `IntervalOption`); Horário (dois `TimePicker` 24 h, "Das … às …"); Dias (7 `ToggleButton` circulares 40 px); Início e Fim (`CalendarDatePicker`, formato dd/MM/yyyy, botão limpar). Rodapé: "Excluir" (texto `Warning`, só edição; confirmação inline "Excluir esta meta? [Cancelar] [Excluir]"), "Cancelar" e "Salvar" (primário). Conteúdo em `ScrollViewer`, largura máxima 640.

- [ ] **Step 1:** Implementar conversores, estilos e Views; registrar `DataTemplate` de `GoalEditViewModel` → `GoalEditView` e os conversores como recursos em `App.axaml`.
- [ ] **Step 2:** `dotnet build SlimeGoals.slnx` sem avisos e `dotnet test --solution SlimeGoals.slnx` verde.
- [ ] **Step 3: Verificação manual com o autor** (`dotnet run --project src/SlimeGoals.Desktop`): criar uma meta de cada tipo; trocar o tipo e ver o alvo; salvar com erro e ver as mensagens nos campos; editar; arquivar; ver em "Arquivadas"; desarquivar; excluir com confirmação; fechar e reabrir o app e conferir que tudo persistiu; navegação só por teclado (Tab/Enter/Espaço) com foco visível. Prints comparados com os tokens do v4.
- [ ] **Step 4: Commit** — `feat(app): telas Metas e Editar meta`

---

## Fora deste plano

Check-ins, progresso e tela Hoje (Fase 3); virada de dia em tempo real e lembretes (Fase 4); layout adaptativo < 720 px e navegação inferior (Fase 7a). Pendências menores da Fase 1 continuam adiadas (cores fixas no `Controls.axaml`, emoji no limite do título, `Enum.Parse` permissivo de cor).
