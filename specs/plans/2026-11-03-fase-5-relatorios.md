# Fase 5 — Relatórios — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A tela Relatórios mostra, para a semana ou o mês escolhido:
- o progresso de cada meta, inclusive das arquivadas no período;
- barras por dia (semana) ou por semana (mês);
- os lembretes disparados e respondidos (semana);
- as sequências em dias ou semanas (mês).

A tela Hoje ganha o tile **Sequência**.

**Architecture:**
- **Core:** três peças puras e um serviço.
  - `DailySums` agrupa os registros por meta e dia local.
  - `GoalHistory` lista os **períodos contados** de uma meta: dias para a diária, semanas para a semanal.
  - `Streaks` calcula a sequência atual e a maior.
  - `ReportService` usa as três e as portas que já existem (`IGoalRepository`, `ICheckinRepository`, `IReminderLogRepository`).
  - Não há tabela nem consulta nova (spec §10: query + Core).
- **App:**
  - `ReportsPageViewModel` alterna semana/mês, navega entre períodos e monta barras e linhas já formatadas;
  - `ReportsView` desenha as barras com `Border` em XAML;
  - o `TodayPageViewModel` ganha o destaque de sequência.
- **Desktop:** registra o `ReportService`.

**Tech Stack:** as mesmas versões da Fase 4 (Avalonia 12.1.4, CommunityToolkit.Mvvm, Dapper, xUnit v3, AwesomeAssertions, FakeTimeProvider). Nenhum pacote novo.

**Spec:** `specs/Planejamento_v3.md` v1.4:
- RF06 (relatório semanal e mensal, % cumprido, sequências) e RF09 (arquivada mantém histórico);
- §7 (tela Relatórios; sequência no resumo do Hoje);
- §10 (regras dos relatórios);
- §12 linha 5 e §13 (testes de sequências e `ReportService`).

**Fora deste plano:**
- exportar relatório;
- calendário mensal de check-ins e comparação com a semana anterior (spec §11: fora do MVP);
- relatórios no Android (Fase 7a reaproveita o ViewModel).

## Decisões deste plano (o autor confirma na revisão)

1. **Período contado.** Uma meta conta num dia `d` quando:
   - `d` está entre a data local da criação e a do arquivamento, ambas inclusive;
   - `d` está dentro do início e fim da meta (`GoalPeriod.IsInPeriod`);
   - `d` não é futuro.

   Por tipo:
   - **Diária:** só os dias ativos contam.
   - **Semanal:** a semana conta se ao menos um dia dela conta. A soma usa o período de avaliação recortado (`GoalPeriod.EvaluationPeriod`).
   - **Período em andamento:** o dia de hoje (diária) ou a semana atual (semanal) só entra na contagem **quando já foi cumprido**. Ainda em aberto, ele não reduz o % nem quebra a sequência.
2. **% cumprido:**
   - **diária:** dias cumpridos ÷ dias contados;
   - **semanal na semana:** soma ÷ alvo, até 100%. A semana em andamento mostra o parcial;
   - **semanal no mês:** semanas cumpridas ÷ semanas contadas;
   - sem período contado, o % é "—";
   - o % geral do relatório é a média dos % das metas que têm valor;
   - arredondamento `MidpointRounding.AwayFromZero`.

   Esta regra é diferente do % do Hoje: lá a diária conta a fração parcial de cada dia; aqui conta dia cumprido ou não. É o que a spec §10 pede: "dias cumpridos / dias ativos".
3. **Mês = as semanas cuja segunda-feira cai no mês, inteiras, e isso vale também para as metas diárias.**
   - Novembro de 2026 vai de 02/11 a 06/12.
   - Por isso, domingo 01/11 pertence a outubro.
   - Assim os dias do mês somam exatamente as barras por semana.
   - O cabeçalho mostra o intervalo real.
4. **Barras da semana:** registros por dia local, contando só os **registros com valor positivo**; os ajustes negativos (Desmarcar, −1) são correções.
5. **Barras do mês:** a média dos % das metas em cada semana. Na semana, cada meta segue o item 2:
   - diária: dias cumpridos ÷ dias contados daquela semana;
   - semanal: soma ÷ alvo, até 100%.
6. **Lembretes da semana:** disparados = entradas do log no intervalo. Respondidos = Feito ou Adiar. "Ignorar" e a expiração ficam gravados do mesmo jeito (spec §9), por isso não contam como resposta.
7. **Sequências:**
   - são calculadas sobre todo o histórico da meta até o fim do período mostrado, ou até hoje, o que vier antes;
   - num mês passado, aparecem "como estavam" no fim daquele mês;
   - dias inativos da diária não quebram a sequência;
   - meta arquivada para no dia do arquivamento.
8. **Melhor dia** (só para meta semanal): o dia de maior soma positiva da semana; no empate, o mais cedo.
9. **Tile "Sequência" do Hoje:**
   - mostra a maior sequência atual entre as metas ativas, com o título da meta embaixo;
   - para comparar dias com semanas, uma semana vale 7 dias; o empate fica com a meta mais antiga;
   - sem nenhuma sequência, mostra "0 dias" sem título.
10. **Navegação:**
    - abre na semana ou no mês atual;
    - **Próximo** fica desabilitado no período atual: não há período futuro;
    - **Anterior** não tem limite;
    - períodos sem metas mostram "Nenhuma meta neste período.".

## Global Constraints

- Tudo das Fases 1–4 continua valendo:
  - `net10.0`, `Nullable`, `TreatWarningsAsErrors`, versões centralizadas;
  - Core sem dependências (`ArchitectureTests`) e App sem referência a Platform;
  - relógio via `TimeProvider`; instantes em UTC e datas locais, convertidas com `DateRange.ToUtc` e o fuso de `TimeProvider.LocalTimeZone`;
  - semana de segunda a domingo (`WeekMath.StartOfWeek`);
  - textos em pt-BR e identificadores em inglês;
  - ViewModels sem tipos visuais do Avalonia, bindings compilados, cores e raios só por `DynamicResource`, alvos de toque ≥ 44 px.
- Os relatórios ignoram registros com `deleted_at` e metas excluídas; os repositórios já fazem isso. Incluem metas arquivadas pela regra da decisão 1.
- Textos exatos:
  - **Controles:**
    - segmentos `"Semana"` / `"Mês"`;
    - botões com dica `"Período anterior"` / `"Próximo período"`;
    - vazio `"Nenhuma meta neste período."`;
    - erro `"Não foi possível carregar o relatório."`.
  - **Títulos de seção:** `"Registros por dia"`, `"Evolução por semana"`, `"Metas"`.
  - **Rótulos:** `"Cumprido"`, `"Lembretes"`; tag `"Arquivada"`; tile do Hoje `"Sequência"`.
  - Os demais textos estão nas Tasks 4 e 6.
- Branch `feat/fase-5-relatorios`.
  - Commits: Conventional Commits com escopo `core`, `infra`, `app`, `desktop` ou `specs`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  - Integração por PR.
- Comando de testes: `dotnet test --solution SlimeGoals.slnx`.
- Teste de fumaça só com `XDG_DATA_HOME` temporário, nunca no banco real.

## Review Focus

1. **Meta arquivada no meio do período:**
   - aparece com a tag "Arquivada";
   - conta só até o dia do arquivamento;
   - some dos períodos posteriores;
   - meta excluída nunca aparece.

   Coberto nas Tasks 2, 3 e 5 (integração com SQLite).
2. **Registro perto da meia-noite local** (23:30 em São Paulo = 02:30 UTC do dia seguinte): entra no dia local, tanto nas barras quanto no dia cumprido. Coberto nas Tasks 1 e 2.
3. **Hoje ainda não cumprido:**
   - não derruba o % do dia nem quebra a sequência;
   - cumprir hoje aumenta a sequência na hora (recarga pelo `DataChangeNotifier`).

   Coberto nas Tasks 1, 4 e 6.
4. **Semanas na virada do mês:**
   - a semana de 26/10 a 01/11 aparece em outubro, não em novembro;
   - fevereiro de 2027 começa numa segunda e tem exatamente 4 semanas.

   Coberto na Task 3.
5. **Ajustes negativos:**
   - um +1 seguido de −1 no mesmo dia não cumpre o dia;
   - nas barras, conta 1 registro;
   - o melhor dia usa a soma líquida.

   Coberto nas Tasks 1 e 2.

---

## Mapa de arquivos

```
src/SlimeGoals.Core/
  Domain/DailySums.cs                 (novo)
  Domain/GoalHistory.cs               (novo: PeriodResult, GoalHistory)
  Domain/Streaks.cs                   (novo: Streak, Streaks)
  Services/ReportService.cs           (novo: registros do relatório + serviço)
src/SlimeGoals.App/
  Goals/ReportFormatting.cs           (novo: textos dos relatórios)
  ViewModels/ReportsPageViewModel.cs  (reescrito)
  ViewModels/ReportItemViewModels.cs  (novo: BarViewModel, ReportRowViewModel)
  Views/ReportsView.axaml             (reescrito)
  Styles/Controls.axaml               (alterado: estilos das barras)
  ViewModels/TodayPageViewModel.cs, Views/TodayView.axaml (alterados: tile Sequência)
src/SlimeGoals.Desktop/CompositionRoot.cs (alterado: ReportService)
tests/SlimeGoals.Core.Tests/Domain/{DailySumsTests,GoalHistoryTests,StreaksTests}.cs (novos)
tests/SlimeGoals.Core.Tests/Services/{ReportServiceWeekTests,ReportServiceMonthTests}.cs (novos)
tests/SlimeGoals.Infrastructure.Tests/Data/ReportServiceSqliteTests.cs (novo)
tests/SlimeGoals.App.Tests/Goals/ReportFormattingTests.cs (novo)
tests/SlimeGoals.App.Tests/ViewModels/ReportsPageViewModelTests.cs (novo)
tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs, ShellViewModelTests.cs (alterados)
specs/Planejamento_v3.md               (alterado)
```

**Relógio dos testes:** os testes de Core e App usam o mesmo relógio dos testes de progresso: `Now` = quarta, 04/11/2026, 12:00 em `America/Sao_Paulo` (15:00 UTC). A semana atual vai de 02/11 a 08/11.

Registros "do dia X" são gravados às 10:00 locais (13:00 UTC), salvo quando o teste diz outra hora. Nos testes puros, as metas vêm de `Support.Goals.G`, criadas em 01/10/2026 às 09:00 locais.

---

### Task 1: Peças puras de histórico — `DailySums`, `GoalHistory`, `Streaks`

**Files:**
- Create: `src/SlimeGoals.Core/Domain/DailySums.cs`, `src/SlimeGoals.Core/Domain/GoalHistory.cs`, `src/SlimeGoals.Core/Domain/Streaks.cs`
- Test: `tests/SlimeGoals.Core.Tests/Domain/DailySumsTests.cs`, `GoalHistoryTests.cs`, `StreaksTests.cs`

**Interfaces:**
- Consumes: `GoalPeriod.IsInPeriod`, `GoalPeriod.EvaluationPeriod`, `WeekMath.StartOfWeek`, `DateRange`, `Checkin`, `Goal`.
- Produces:

```csharp
namespace SlimeGoals.Core.Domain;

/// Somas com sinal por (meta, dia local).
public sealed class DailySums
{
    public DailySums(IEnumerable<Checkin> checkins, TimeZoneInfo zone);
    public int On(Guid goalId, DateOnly day);
    public int In(Guid goalId, DateRange range);          // soma dos dias em [Start, EndExclusive)
    public int PositiveCountOn(DateOnly day, IReadOnlySet<Guid> goalIds); // registros com Amount > 0 no dia
}

/// Um período contado (dia da diária, semana da semanal). Range da semana = segunda a segunda.
public sealed record PeriodResult(DateRange Range, int Done, bool Met);

public static class GoalHistory
{
    public static DateOnly CreatedOn(Goal goal, TimeZoneInfo zone);
    public static DateOnly? ArchivedOn(Goal goal, TimeZoneInfo zone);
    /// Decisão 1, sem o filtro de dia ativo: criação ≤ day ≤ arquivamento e IsInPeriod.
    public static bool ExistedOn(Goal goal, DateOnly day, TimeZoneInfo zone);
    /// Períodos contados que intersectam range, em ordem, sem dias depois de today;
    /// o período que contém today só entra se Met.
    public static IReadOnlyList<PeriodResult> Periods(Goal goal, DateRange range, DateOnly today,
        TimeZoneInfo zone, DailySums sums);
}

public readonly record struct Streak(int Current, int Best);

public static class Streaks
{
    /// Best = maior sequência de Met; Current = sequência de Met no fim da lista.
    public static Streak From(IReadOnlyList<PeriodResult> periods);
}
```

Regras de `Periods`:
- **Diária:** cada dia `d` de `range` com `d ≤ today`, `ExistedOn` verdadeiro e `ActiveDays.Includes(d.DayOfWeek)`. `Done = sums.On(id, d)`; `Met = Done ≥ Target`.
- **Semanal:** cada semana com segunda `m` de `WeekMath.StartOfWeek(range.Start)` até antes de `range.EndExclusive`.
  - A semana conta se existe um dia `d` nela, com `d ≤ today`, em que `ExistedOn` é verdadeiro.
  - `Done = sums.In(id, GoalPeriod.EvaluationPeriod(goal, d)!.Value)`, onde `d` é o primeiro desses dias.
  - `Range = new DateRange(m, m.AddDays(7))`.
- **Hoje:** na diária, o dia `today`, e na semanal, a semana que contém `today`, entram só se `Met`.

- [ ] **Step 1: Write the failing tests**

`DailySumsTests` (fuso `America/Sao_Paulo`):
- `Groups_by_local_day_with_signed_sums`: registros da meta A em 03/11 10:00 (+2), 03/11 23:30 locais, ou seja 04/11 02:30 UTC (+1), e 04/11 (−1).
  - `On(A, 03/11) == 3`
  - `On(A, 04/11) == -1`
  - `In(A, new(03/11, 05/11)) == 2`
  - `On(B, 03/11) == 0`
- `PositiveCountOn_counts_only_positive_records_of_given_goals`: em 03/11, A tem +1, +1 e −1, e B tem +5.
  - `PositiveCountOn(03/11, {A}) == 2`
  - `PositiveCountOn(03/11, {A, B}) == 3`

`GoalHistoryTests`:
- **`Daily_periods_skip_inactive_days_before_creation_and_after_today`:**
  - Meta diária, alvo 1, dias Seg|Qua|Sex, `CreatedAt` = 27/10 12:00 UTC.
  - Range: 26/10 a 09/11; `today` = 04/11; registros +1 em 28/10, 30/10 e 02/11.
  - `Periods` devolve os dias 28/10, 30/10 e 02/11, todos `Met`.
  - Hoje (qua, 04/11) não entra, porque não foi cumprido; 26/10 fica antes da criação.
  - Com +1 também em 04/11, a lista termina em 04/11 `Met`.
- **`Daily_met_uses_net_sum`:** alvo 1; em 02/11, +1 e −1.
  - O período de 02/11 tem `Done == 0` e `Met == false`.
- **`Daily_counts_until_archive_day_inclusive`:**
  - `ArchivedAt` = 03/11 01:00 UTC, que é 02/11 22:00 local.
  - Com range de 02/11 a 09/11 e today = 04/11, só sobra o dia 02/11.
  - `ArchivedOn == 02/11`.
- **`Weekly_periods_use_clipped_evaluation_and_skip_unmet_current_week`:**
  - Meta semanal, alvo 3, `StartDate` = 21/10 (quarta); range de 19/10 a 09/11, today = 04/11.
  - Registros: +2 em 20/10 (antes do início, não conta), +1 em 22/10, +3 em 27/10, +1 em 03/11.
  - Resultado: as semanas de 19/10 (Done 1, não Met) e de 26/10 (Done 3, Met).
  - A semana de 02/11 (Done 1, em andamento) não entra.
- **`Weekly_counts_week_of_creation`:**
  - Semanal criada em quinta, 05/11 (12:00 UTC); today = 06/11; range = semana de 02/11; +1 em 05/11 com alvo 1.
  - Resultado: uma semana `Met`, com Range de 02/11 a 09/11.

`StreaksTests`:
- `Empty_is_zero`: `From([]) == new Streak(0, 0)`.
- `Current_is_trailing_run_and_best_is_longest`: Met = [T, T, T, F, T, T] → `(2, 3)`.
- `Trailing_miss_resets_current`: [T, T, F] → `(0, 2)`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --solution SlimeGoals.slnx`. Expected: falha de compilação, porque os tipos ainda não existem.

- [ ] **Step 3: Implement the three types** in the files above, with Portuguese XML docs that follow the existing style. `DailySums` keeps a `Dictionary<(Guid, DateOnly), int>` of sums and a positive count per `(Guid, DateOnly)`.

- [ ] **Step 4: Run tests** — Expected: PASS (todos, inclusive os antigos).

- [ ] **Step 5: Commit** — `feat(core): histórico de períodos e sequências das metas`

---

### Task 2: `ReportService` — relatório semanal

**Files:**
- Create: `src/SlimeGoals.Core/Services/ReportService.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/ReportServiceWeekTests.cs`

**Interfaces:**
- Consumes: Task 1; `IGoalRepository.ListAsync(includeArchived: true)`, `ICheckinRepository.ListAsync(fromUtc, toUtc)`, `IReminderLogRepository.ListAsync(fromUtc, toUtc)`.
- Produces (a Task 3 completa os registros do mês no mesmo arquivo):

```csharp
namespace SlimeGoals.Core.Services;

public sealed record DayBar(DateOnly Day, int Records);

/// Done: soma da semana (semanal: período de avaliação recortado). Percent null = sem período contado.
/// BestDay só para semanal.
public sealed record WeekGoalRow(Goal Goal, int Done, int MetPeriods, int CountedPeriods, int? Percent, DateOnly? BestDay);

public sealed record WeekReport(DateRange Week, IReadOnlyList<DayBar> Days, IReadOnlyList<WeekGoalRow> Goals,
    int? Percent, int RemindersFired, int RemindersAnswered);

public sealed class ReportService(IGoalRepository goals, ICheckinRepository checkins, IReminderLogRepository log,
    TimeProvider time)
{
    /// Semana (segunda a domingo) que contém day.
    public Task<WeekReport> GetWeekAsync(DateOnly day, CancellationToken ct = default);
}
```

Regras:
- **Linhas:** as metas (arquivadas incluídas) com `ExistedOn` em algum dia da semana `≤ today`, em ordem de `CreatedAt`.
- **Uma consulta de registros só**, da segunda local até a segunda seguinte, convertida para UTC.
- **Diária:**
  - `Met`/`Counted` = `Periods(goal, semana)`;
  - `Percent = Counted == 0 ? null : round(Met × 100 / Counted)`;
  - `BestDay = null`.
- **Semanal:**
  - `Done` = soma no `EvaluationPeriod` do primeiro dia existente da semana;
  - `Percent = round(min(1, Done / Target) × 100)`, sempre com valor, porque a linha só aparece se a meta existiu;
  - `Met`/`Counted` vêm de `Periods`, de 0 a 1.
- **Barras:** `Days` tem 7 entradas, de segunda a domingo, com `Records = sums.PositiveCountOn(dia, ids das linhas)`.
- **Lembretes:**
  - entradas do log da semana cujo `GoalId` está nas linhas;
  - respondidos = `Response is Done or Snooze`.
- **% geral:** média dos `Percent` com valor, arredondada; `null` se não houver nenhum.

- [ ] **Step 1: Write the failing tests** (`ReportServiceWeekTests`; repositórios em memória de `SlimeGoals.Testing`; um helper `Create(...)` como o de `ProgressServiceTests`, que ajusta `CreatedAt`; padrão: criada em 01/10)
  - **`Daily_row_counts_met_days_and_skips_unmet_today`:**
    - Diária, alvo 2; +2 em 02/11 e +1 em 03/11.
    - Linha: `(Met 1, Counted 2, Percent 50)`.
    - Depois de +2 em 04/11: `(2, 3, 67)`.
  - **`Daily_row_counts_only_active_days_from_creation`:**
    - Diária, alvo 1, dias Seg|Qua, criada em 01/10; +1 em 02/11; hoje (qua) sem registro → `(1, 1, 100)`.
    - Outra diária, todos os dias, criada em 03/11, sem registros → `(0, 1, 0)`. Ela conta só 03/11, porque hoje está em aberto.
  - **`Weekly_row_shows_net_sum_capped_percent_and_earliest_best_day`:**
    - Semanal de tempo, alvo 300.
    - Registros: +60 em 02/11; +120 e −30 em 03/11; +90 em 04/11.
    - Resultado: `Done 240`, `Percent 80`, `BestDay == 03/11` (empate com 04/11; vence o mais cedo).
    - Depois de +200 em 04/11: `Percent 100` e `BestDay == 04/11`.
  - **`Includes_goal_archived_during_week_and_excludes_earlier_archived_and_deleted`:**
    - Diária, alvo 1, com +1 em 02/11 e arquivada em 03/11 15:00 UTC: linha presente, `(1, 2, 50)`.
    - Diária arquivada em 30/10: ausente.
    - Meta excluída com registros na semana: ausente.
    - Registro excluído (`DeletedAt` preenchido) da primeira meta em 03/11: ignorado.
  - **`Day_bars_count_positive_records_by_local_day`:**
    - Registros: +1, +1 e −1 em 02/11; +1 em 03/11 às 23:30 locais (04/11 02:30 UTC).
    - Barras: `Days.Select(d => d.Records)` igual a `[2, 1, 0, 0, 0, 0, 0]`.
    - Datas das barras: de 02/11 a 08/11.
  - **`Counts_reminders_fired_and_answered_in_week`:**
    - Na semana: entradas com resposta Done, Snooze, Ignore e uma pendente.
    - Também uma entrada em 01/11 23:00 local, de fora da semana.
    - Resultado: `(Fired 4, Answered 2)`.
  - **`Overall_percent_averages_rows_with_value`:**
    - Linhas: diária 50%, semanal 80% e uma diária criada hoje sem registro (`Percent null`).
    - `Percent == 65`; sem nenhuma meta, `Percent == null`.
  - **`Past_week_counts_all_seven_days`:**
    - `GetWeekAsync(26/10)`, com diária alvo 1 cumprida em 5 dos 7 dias.
    - Resultado: `(5, 7, 71)`; `Week == new(26/10, 02/11)`.

- [ ] **Step 2: Run to verify they fail** — Expected: falha de compilação.
- [ ] **Step 3: Implement `GetWeekAsync`** with the rules above.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(core): relatório semanal por meta`

---

### Task 3: `ReportService` — relatório mensal, sequências e integração com SQLite

**Files:**
- Modify: `src/SlimeGoals.Core/Services/ReportService.cs`
- Test: `tests/SlimeGoals.Core.Tests/Services/ReportServiceMonthTests.cs`, `tests/SlimeGoals.Infrastructure.Tests/Data/ReportServiceSqliteTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2.
- Produces:

```csharp
public sealed record WeekBar(DateOnly Monday, int? Percent);

/// WeekPercents: um valor por semana de Range (null = meta não existiu naquela semana ou semana futura).
public sealed record MonthGoalRow(Goal Goal, int MetPeriods, int CountedPeriods, int? Percent,
    IReadOnlyList<int?> WeekPercents, Streak Streak);

public sealed record MonthReport(int Year, int Month, DateRange Range, IReadOnlyList<WeekBar> Weeks,
    IReadOnlyList<MonthGoalRow> Goals, int? Percent);

/// Destaque do Hoje: Length na unidade do tipo da meta (dias ou semanas).
public sealed record StreakHighlight(Goal Goal, int Length);

// em ReportService:
public static DateRange MonthRange(int year, int month);   // da 1ª segunda do mês à segunda seguinte à última
public Task<MonthReport> GetMonthAsync(int year, int month, CancellationToken ct = default);
public Task<StreakHighlight?> GetTopStreakAsync(CancellationToken ct = default);
```

Regras:
- **Linhas:** metas com `ExistedOn` em algum dia de `Range` `≤ today`, em ordem de `CreatedAt`.
  - `Met`/`Counted` = `Periods(goal, Range)`.
  - `Percent = Counted == 0 ? null : round(Met × 100 / Counted)`.
- **`WeekPercents`,** por segunda `m` de `Range`:
  - diária: a fração de `Periods(goal, semana m)`, `null` se vazia;
  - semanal: `round(min(1, soma / alvo) × 100)` se a meta existiu na semana até hoje, `null` se não;
  - o parcial da semana atual aparece aqui, mas não em `Percent`.
- **`Weeks[i].Percent`:** média dos `WeekPercents[i]` com valor.
- **`Percent` geral:** média dos `Percent` com valor.
- **Sequência:**
  - `cutoff = min(Range.EndExclusive − 1 dia, today)`;
  - `Streak = Streaks.From(Periods(goal, new(CreatedOn, cutoff + 1), today, …))`.
- **Consulta de registros:** uma só, da menor data local de criação entre as metas da lista (ou de `Range.Start`, se for menor) até `Range.EndExclusive`.
- **`GetTopStreakAsync`:**
  - metas ativas (`includeArchived: false`), com histórico da criação até hoje;
  - escolhe o maior `Current × (Weekly ? 7 : 1)`; no empate, a de `CreatedAt` menor;
  - devolve `null` se todos os `Current` forem 0.

- [ ] **Step 1: Write the failing tests** (`ReportServiceMonthTests`; mesmo helper da Task 2)
  - **`MonthRange_uses_weeks_whose_monday_is_in_month`:**
    - nov/2026 → (02/11, 07/12);
    - out/2026 → (05/10, 02/11);
    - fev/2027 → (01/02, 01/03).
  - **`Daily_month_counts_days_until_today`** (novembro):
    - Diária, alvo 1; +1 em 02/11 e 03/11.
    - Linha: `(2, 2, 100)`; `WeekPercents == [100, null, null, null, null]`.
    - `Weeks.Count == 5`.
  - **`Weekly_month_counts_weeks_by_monday`** (outubro; semanal, alvo 3, criada em 01/10):
    - Somas: 3 em 01/10 (semana de setembro); 3 na semana de 05/10; 1 na de 12/10; 5 na de 19/10; 0 na de 26/10.
    - Linha: `(2, 4, 50)`.
    - `WeekPercents == [100, 33, 100, 0]`; `Weeks.Select(w => w.Percent)` dá os mesmos valores.
  - **`Daily_streaks_skip_inactive_days`:**
    - Diária, Seg–Sex, alvo 1, criada em 01/10.
    - Cumprida nos dias úteis de 05/10 a 20/10 (12 dias) e de 22/10 a 30/10 (7 dias); não cumprida em 21/10. Cumprida também em 02/11 e 03/11. Hoje em aberto.
    - Novembro: `Streak == (9, 12)`.
    - Outubro: `Streak == (7, 12)`.
  - **`Weekly_streaks_in_weeks_ignore_open_current_week`:**
    - Semanal, alvo 1, conclusão, criada em 01/10.
    - Cumprida nas semanas de 05/10, 12/10 e 26/10; vazia na de 19/10 e, até agora, na de 02/11.
    - Novembro: `Streak == (1, 2)`.
  - **`Archived_goal_counts_only_while_active`:**
    - Diária arquivada em 15/10 18:00 UTC, cumprida de 05/10 a 15/10.
    - Outubro: `(11, 11, 100)`, `Streak (11, 11)`.
    - Novembro: a meta está ausente.
  - **`Top_streak_compares_weeks_as_seven_days`:**
    - Diária com sequência atual de 9 dias; semanal com 2 semanas.
    - `GetTopStreakAsync()` devolve a semanal com `Length 2`.
    - Sem registros: `null`.
    - Meta arquivada com sequência maior é ignorada.

  `ReportServiceSqliteTests` (repositórios reais sobre `TestDatabase`; `FakeTimeProvider` com o mesmo `Now` e fuso):
  - **`Reports_match_checkins_including_archived_goal_and_deleted_records`:**
    - Diária alvo 1, criada em 01/10, cumprida em 02/11 e 03/11.
    - Registro excluído em 01/11.
    - Meta arquivada em 03/11, com +1 em 02/11.
    - Uma meta excluída com registros.
    - Na semana atual: duas linhas; a primeira `(2, 2, 100)`, a arquivada `(1, 2, 50)`.
    - No mês de novembro: os mesmos `Met`/`Counted`.

- [ ] **Step 2: Run to verify they fail** — Expected: falha de compilação.
- [ ] **Step 3: Implement** `MonthRange`, `GetMonthAsync`, `GetTopStreakAsync`.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(core): relatório mensal e sequências`, depois `test(infra): relatórios conferem com os registros no SQLite` (podem ser um commit só se o teste de integração vier junto).

---

### Task 4: `ReportFormatting` e `ReportsPageViewModel`

**Files:**
- Create: `src/SlimeGoals.App/Goals/ReportFormatting.cs`, `src/SlimeGoals.App/ViewModels/ReportItemViewModels.cs`
- Modify: `src/SlimeGoals.App/ViewModels/ReportsPageViewModel.cs`, `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs` (construir a página com o `ReportService` em memória)
- Test: `tests/SlimeGoals.App.Tests/Goals/ReportFormattingTests.cs`, `tests/SlimeGoals.App.Tests/ViewModels/ReportsPageViewModelTests.cs`

**Interfaces:**
- Consumes: Tasks 2–3; `GoalFormatting.Duration`, `GoalFormatting.UnitFor` (torne `UnitFor` `public` se ainda não for); `DataChangeNotifier`.
- Produces:

```csharp
namespace SlimeGoals.App.Goals;
public static class ReportFormatting
{
    public static string WeekTitle(DateRange week);          // "2 a 8 nov 2026", "26 out a 1 nov 2026", "29 dez 2025 a 4 jan 2026"
    public static string MonthTitle(int year, int month);   // "Novembro de 2026"
    public static string MonthSubtitle(DateRange range);    // "Semanas de 2 nov a 6 dez"
    public static string Percent(int? percent);             // 80 → "80%", null → "—"
    public static string Count(int n, string singular, string plural); // 1 → "1 dia", 2 → "2 dias"
    public static string WeekDetail(WeekGoalRow row);
    public static string MonthDetail(MonthGoalRow row);
    public static string Streak(MonthGoalRow row);
    public static string? BestDay(WeekGoalRow row);
    public static string Reminders(int fired, int answered);
}

namespace SlimeGoals.App.ViewModels;
public enum ReportMode { Week, Month }
/// Fraction 0..1 já normalizada pela maior barra (registros) ou por 100 (%).
public sealed record BarViewModel(string Label, string Value, double Fraction, bool IsCurrent)
{
    /// Altura em px da barra na área de 150 px (número, não tipo visual).
    public double BarHeight => Math.Max(4, Fraction * 120);
}
public sealed record ReportRowViewModel(string Title, string IconKey, GoalColor Color, string Detail,
    string PercentText, double PercentFraction, string? Extra, bool IsArchived);
```

**Textos exatos** (meses abreviados em minúsculas: jan fev mar abr mai jun jul ago set out nov dez; nomes completos com inicial maiúscula):

- **`WeekDetail`:**
  - diária: `"4 de 6 dias"` (`Count`: `"1 dia"`, `"0 dias"`);
  - semanal de quantidade: `"6 de 8 copos"`;
  - semanal de tempo: `"4 h de 5 h"` (com `Duration`);
  - semanal de conclusão: `"Feito"` quando cumprida, senão `"Pendente"`.
- **`MonthDetail`:** diária `"18 de 25 dias"`; semanal `"3 de 4 semanas"` (`"1 semana"`).
- **`BestDay`:** `"Melhor dia: qua"` (seg ter qua qui sex sáb dom); `null` sem melhor dia.
- **`Streak`:** `"Sequência: 5 dias · maior: 12 dias"`, ou `"Sequência: 1 semana · maior: 2 semanas"`.
- **`Reminders`:** `"12 disparados · 7 respondidos"`, ou `"1 disparado · 0 respondidos"`.

**`ReportsPageViewModel`** recebe `(ReportService reports, TimeProvider time, DataChangeNotifier changes)` e herda `Title => "Relatórios"`.

- **Estado da tela:**
  - `[ObservableProperty] ReportMode Mode`;
  - `IsWeek` / `IsMonth` (bool, para os segmentos `RadioButton`, two-way);
  - `PeriodTitle`, `PeriodSubtitle` (null na semana), `PercentText`, `RemindersText` (null no mês), `BarsTitle` (`"Registros por dia"` / `"Evolução por semana"`), `ErrorMessage`;
  - `IsEmpty`, `EmptyMessage = "Nenhuma meta neste período."`;
  - `ObservableCollection<BarViewModel> Bars`, `ObservableCollection<ReportRowViewModel> Rows`.
- **Comandos:**
  - `LoadCommand`, que segue o padrão do `TodayPageViewModel`: `_loadVersion` e `_ui`, assina `changes.Changed` uma vez e captura exceções;
  - `PreviousCommand`, `NextCommand` (`CanExecute` falso no período atual);
  - `Activate()` volta para o período atual do modo e recarrega.
- **Barras:**
  - **Semana:**
    - rótulos `"Seg"…"Dom"`;
    - `Value` = número de registros;
    - `Fraction = records / max(1, maior)`;
    - `IsCurrent` = dia de hoje.
  - **Mês:**
    - rótulo `"2 nov"`;
    - `Value = Percent(...)`;
    - `Fraction = (percent ?? 0) / 100`;
    - `IsCurrent` = semana de hoje.
- **Linhas:** `Extra` = `BestDay` na semana e `Streak` no mês; `PercentFraction = (percent ?? 0) / 100`; `IsArchived = Goal.ArchivedAt is not null`.

- [ ] **Step 1: Write the failing tests**
  - **`ReportFormattingTests`:** cada exemplo acima, com estes casos-limite:
    - `WeekTitle` nas três formas: mesmo mês, meses diferentes, anos diferentes;
    - `Count` no singular;
    - `Percent(null) == "—"`;
    - conclusão semanal: Feito e Pendente.
  - **`ReportsPageViewModelTests`** (`Now` padrão; repositórios em memória):
    - **`Opens_on_current_week_with_bars_and_rows`** (diária alvo 1, cumprida em 02/11 e 03/11):
      - `PeriodTitle == "2 a 8 nov 2026"`;
      - `Bars.Count == 7`, `Bars[2].IsCurrent`;
      - a linha da diária mostra `Detail == "2 de 2 dias"` e `PercentText == "100%"`;
      - `RemindersText == "0 disparados · 0 respondidos"`;
      - `NextCommand.CanExecute(null) == false`.
    - **`Previous_then_next_navigates_weeks`:**
      - Anterior: `"26 out a 1 nov 2026"`, e `Next` volta a ser possível.
      - Próximo: volta para `"2 a 8 nov 2026"`.
    - **`Month_mode_shows_week_bars_streak_and_subtitle`:**
      - `IsMonth = true` leva a `PeriodTitle == "Novembro de 2026"` e `PeriodSubtitle == "Semanas de 2 nov a 6 dez"`;
      - `Bars.Count == 5`, `Bars[0].Label == "2 nov"`;
      - `Rows[0].Extra` começa com `"Sequência: "`;
      - `RemindersText == null`.
    - **`Previous_month_from_november_is_october`:** `"Outubro de 2026"`.
    - **`Archived_goal_row_is_flagged`:** `IsArchived` é verdadeiro.
    - **`Empty_period_shows_message`:** sem metas, `IsEmpty` é verdadeiro.
    - **`Reloads_on_data_change`:** `AdjustAsync` (do `CheckinService` com o mesmo `DataChangeNotifier`) atualiza `Rows[0].Detail`.
    - **`Load_failure_sets_error_message`:** repositório de metas que lança exceção leva a `ErrorMessage == "Não foi possível carregar o relatório."`.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** `ReportFormatting`, the item records and `ReportsPageViewModel`; update `ShellViewModelTests`.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** — `feat(app): ViewModel e textos dos relatórios`

---

### Task 5: `ReportsView` e composição

**Files:**
- Modify: `src/SlimeGoals.App/Views/ReportsView.axaml`, `src/SlimeGoals.App/Styles/Controls.axaml`, `src/SlimeGoals.Desktop/CompositionRoot.cs`

**Interfaces:**
- Consumes: Task 4 (propriedades e comandos do `ReportsPageViewModel`); estilos existentes `page-title`, `Border.segment` / `RadioButton.segment`, `Border.stat`, `stat-label`, `stat-value`, `section-title`, `Border.card`, `Border.icon-tile`, `Path.goal-icon`, `ProgressBar.goal`, `Button.icon`, `Path.btn-icon`, `warning-band`; conversores `GoalColorToBrushConverter`, `IconKeyToGeometryConverter`; ícones novos `IconChevronLeft` (`M 15 18 L 9 12 L 15 6`) e `IconChevronRight` (`M 9 18 L 15 12 L 9 6`), do Lucide, em `Theme.axaml`.

Layout, de cima para baixo, como na referência v3-semana:
- **Cabeçalho:**
  - título da página;
  - à direita, o segmento `"Semana"` / `"Mês"`.
- **Linha de navegação:**
  - botão-ícone ‹ com `ToolTip.Tip="Período anterior"`;
  - `PeriodTitle`, com `PeriodSubtitle` em `Muted` abaixo, quando houver;
  - botão-ícone › com `ToolTip.Tip="Próximo período"`.
- **Faixa de erro** (`warning-band`).
- **Dois tiles `stat`:**
  - `"Cumprido"` com `PercentText`;
  - `"Lembretes"` com `RemindersText`, visível só na semana.
- **Cartão das barras:**
  - título `BarsTitle`;
  - `ItemsControl` horizontal com `UniformGrid Rows=1` e área de 150 px;
  - cada barra é um `Border` de 30 px de largura e cantos de 12, alinhado embaixo, com altura `BarHeight`;
  - cor `BarIdle`, ou `Primary` quando `IsCurrent`;
  - valor acima e rótulo abaixo da barra, em `Muted` (`Text` quando `IsCurrent`).

- **Seção `"Metas"`:** um cartão por linha com:
  - ícone e cor;
  - título e a tag `"Arquivada"` quando for o caso;
  - `Detail`;
  - `ProgressBar.goal` com `PercentFraction`;
  - `PercentText` à direita;
  - `Extra` em `Muted`.
- **Texto vazio,** quando `IsEmpty`.
- **Rolagem:** tudo dentro de um `ScrollViewer`.

Composição: `services.AddSingleton<ReportService>();` na seção Core do `CompositionRoot`.

- [ ] **Step 1: Implement the view, styles (`Border.bar`, `Border.bar.current`, `TextBlock.bar-label`, `Border.tag`) and DI.**
- [ ] **Step 2: Build** — Run: `dotnet build SlimeGoals.slnx`. Expected: 0 avisos e 0 erros. Os bindings compilados pegam nomes errados.
- [ ] **Step 3: Run tests** — Expected: PASS. O `ValidateOnBuild` do `CompositionRoot` só roda quando o app abre; o Step 4 confere.
- [ ] **Step 4: Smoke run:**
  - abra o app com `XDG_DATA_HOME` temporário: `XDG_DATA_HOME=<scratch>/xdg-5 dotnet run --project src/SlimeGoals.Desktop`;
  - crie uma diária e uma semanal, registre progresso e abra Relatórios;
  - alterne Semana/Mês e navegue Anterior/Próximo;
  - o app não pode lançar exceção (veja a saída); encerre com Sair.
- [ ] **Step 5: Commit** — `feat(app): tela de relatórios com barras e tabela por meta`

---

### Task 6: Tile "Sequência" na tela Hoje

**Files:**
- Modify: `src/SlimeGoals.App/ViewModels/TodayPageViewModel.cs`, `src/SlimeGoals.App/Views/TodayView.axaml`, `tests/SlimeGoals.App.Tests/ViewModels/TodayPageViewModelTests.cs`

**Interfaces:**
- Consumes: `ReportService.GetTopStreakAsync` (Task 3).
- Produces:
  - parâmetro opcional novo no fim do construtor do `TodayPageViewModel`: `ReportService? reports = null` (o DI preenche; os testes antigos continuam compilando);
  - propriedades `StreakValue` (`int`), `StreakUnit` (`string`), `StreakGoal` (`string?`).

Regras:
- **Valores:**
  - `StreakValue = highlight?.Length ?? 0`;
  - `StreakUnit`: `"dias"`, `"dia"` (1), `"semanas"` ou `"semana"` (1), conforme a recorrência da meta; sem destaque, `"dias"`;
  - `StreakGoal` = título da meta, ou `null` sem destaque.
- **Carga:** depois do instantâneo e antes dos próximos lembretes. A carga é isolada: em falha, registra em `Trace` e zera o tile, como em `LoadUpcomingAsync`.
- **View:**
  - o resumo passa de 3 para 4 colunas;
  - o 4º tile tem rótulo `"Sequência"`, `stat-value` com `StreakValue` e `stat-unit` com `StreakUnit`;
  - o título da meta fica abaixo, em `Muted`, visível só quando não é nulo;
  - troque o comentário `"sequência chega na Fase 5"` por `"spec §7"`.

- [ ] **Step 1: Write the failing tests** (em `TodayPageViewModelTests`; `NewPage()` passa um `ReportService` com os mesmos repositórios):
  - **`Streak_tile_shows_top_current_streak_and_updates_when_today_is_met`:**
    - Diária "Água", alvo 1, com `CreatedAt` ajustado para 31/10 no repositório e registros em 01/11, 02/11 e 03/11.
    - Primeira carga: `(StreakValue 3, StreakUnit "dias", StreakGoal "Água")`.
    - `CompleteCommand` no cartão leva a `StreakValue 4`.
  - **`Streak_tile_without_streak_shows_zero_days`:** sem registros, `(0, "dias", null)`.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** VM + view.
- [ ] **Step 4: Run tests + build** — Expected: PASS, 0 avisos.
- [ ] **Step 5: Commit** — `feat(app): sequência no resumo da tela Hoje`

---

### Task 7: Spec

**Files:**
- Modify: `specs/Planejamento_v3.md`

- [ ] **Step 1: Update:**
  - **§7:** a linha da tela Hoje diz que o resumo tem a sequência (Fase 5), e a linha de Relatórios descreve a tela entregue.
  - **§10:**
    - substitua a query ilustrativa pela descrição de como os relatórios são calculados: uma leitura de registros por relatório, agrupada por dia local no Core;
    - registre as decisões 1–8 deste plano em texto corrido, curto.
  - **§12, linha 5:** "Implementado" com o resumo.
  - **§13:** cite `GoalHistory`/`Streaks`.
  - **§15:** novo bloco **"Decisões da Fase 5 (03/11/2026)"** com as decisões 1–10 em uma linha cada.
- [ ] **Step 2: Commit** — `docs(specs): relatórios e sequências da Fase 5`

---

## Verificação manual (autor, depois do PR aberto)

Use um banco temporário com dados semeados pelo próprio app:
1. Crie uma diária (alvo 1) e uma semanal de tempo.
   - Registre progresso hoje.
   - Na semana, a diária mostra "1 de 1 dias", 100%; a semanal mostra o parcial e o melhor dia.
2. Arquive a diária.
   - Ela continua na semana com a tag "Arquivada".
   - No Hoje, o tile Sequência muda para outra meta ou zera.
3. Mude para Mês.
   - O título é o do mês atual e o subtítulo mostra o intervalo das semanas.
   - As barras mostram as semanas.
   - Volte um mês e confira o título e o botão "Próximo período" habilitado.
4. Clique em Feito numa diária no Hoje e volte para Relatórios: o número atualiza sem reabrir o app.
