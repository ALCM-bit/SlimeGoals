# Gestor de Metas — Especificação e Plano de Implementação (Avalonia + C#)

> Versão 1.3 · Alvos iniciais: Linux (desktop) e Android · .NET 10 LTS · Dark mode roxo suave
>
> Substitui a v1.2 (`Planejamento_v2.md`, mantida como histórico). Mudanças principais: três tipos de recorrência (`once`, `daily`, `weekly`) unificados pelo conceito de **período de avaliação**; semana fixa de segunda a domingo; referência visual v4 (desktop) e v3 (mobile); ícone e cor por meta. Ver seção 15.

## 1. Visão geral

Aplicação multiplataforma para cadastrar metas (tarefas únicas, hábitos diários e metas semanais), receber lembretes periódicos até cumpri-las, registrar o progresso e gerar relatórios semanais e mensais sob demanda. Interface convencional com telas, tema escuro roxo/preto suave. O mascote (slime) fica como melhoria futura.

**Dentro do escopo do MVP:** Linux (desktop, primeiro) e Android (em seguida), dados locais, lembretes, relatórios.

**Fora do escopo do MVP:** Windows e macOS, sincronização entre dispositivos, contas de usuário, mascote animado, tema claro, recursos dos protótipos sem dado correspondente (ver seção 11).

## 2. Requisitos funcionais

| ID   | Requisito                                                                                                                                                                                                                       |
|------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| RF01 | CRUD de metas: título, tipo de recorrência (`once`, `daily`, `weekly`), alvo do período, ícone e cor, intervalo de lembrete, horário e dias ativos, início e fim opcionais                                                       |
| RF02 | Tela "Hoje": metas com progresso **no período atual de cada uma** (dia, semana ou tarefa única pendente) e botão de check-in rápido                                                                                              |
| RF03 | Registrar check-in (quantidade e nota opcional) e desfazer o último                                                                                                                                                             |
| RF04 | Lembretes periódicos por meta, respeitando horário e dias ativos; ações **Feito**, **Adiar**, **Ignorar**                                                                                                                       |
| RF05 | Meta cumprida no período atual não gera mais lembretes até o próximo período (diária: dia seguinte ativo; semanal: próxima semana; única: nunca mais)                                                                            |
| RF06 | Relatório semanal e mensal por solicitação (progresso por meta, % cumprido, sequências na unidade de cada tipo, tarefas únicas concluídas)                                                                                      |
| RF07 | Ícone na bandeja: abrir, pausar lembretes, sair                                                                                                                                                                                 |
| RF08 | Configurações: iniciar com o sistema, duração padrão do "adiar", ao fechar a janela (ocultar em segundo plano, padrão, ou encerrar; só desktop)                                                                                 |
| RF09 | Arquivar meta sem perder histórico                                                                                                                                                                                              |
| RF10 | Período e recorrência: início e fim opcionais. Fora do período, a meta não aparece em "Hoje" nem gera lembretes; ao passar o fim, é arquivada automaticamente. Tarefa única é concluída e arquivada automaticamente no check-in |
| RF11 | No Android, lembretes agendados pelo sistema (`AlarmManager`), com as ações funcionando mesmo com o app fechado                                                                                                                 |

## 3. Requisitos não funcionais

- **Offline e local:** SQLite, sem servidor.
- **Separação de camadas:** nenhuma regra de negócio na UI, para compartilhar Core e UI entre Linux e Android.
- **Testável:** Core sem dependência de Avalonia; relógio injetável (`TimeProvider`).
- **Resposta independente da UI:** tratar a ação de um lembrete (Feito/Adiar/Ignorar) não pode exigir janela nem ViewModel; no Android o processo pode estar morto.
- **Desempenho:** abertura < 2 s; uso de CPU ocioso próximo de zero (sem polling).

## 4. Stack

| Camada        | Tecnologia                                                                                                                                                  |
|---------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Runtime       | .NET 10 (LTS), suportado até nov/2028 (SDK 10.0.113 já instalado no ambiente de desenvolvimento)                                                           |
| UI            | Avalonia 12 (XAML) com MVVM — 12.1.4 na data do plano das Fases 0–1                                                           |
| MVVM          | CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)                                                                                            |
| DI            | Microsoft.Extensions.DependencyInjection                                                                                                                    |
| Banco         | SQLite com Microsoft.Data.Sqlite + Dapper                                                                                                                   |
| Migrações     | Scripts SQL numerados + `PRAGMA user_version`                                                                                                               |
| Gráficos      | Barras desenhadas em XAML no MVP; LiveCharts2 só se surgirem gráficos que o justifiquem                                                                     |
| Fonte         | Figtree (OFL), embutida no app como recurso Avalonia                                                                                                        |
| Testes        | xUnit v3 + AwesomeAssertions (fork Apache-2.0 do FluentAssertions, que passou a ter licença comercial na v8) + `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`)                                                                 |
| Empacotamento | `dotnet publish` por RID (linux-x64); AppImage ou binário *self-contained* com arquivo `.desktop` no Linux (PKGBUILD opcional); APK/AAB assinado no Android |

**Justificativa das escolhas:**

- **C# / .NET 10:** um único runtime para Core, dados e UI; domínio já conhecido pelo autor; o .NET 8 sai de suporte em 10/11/2026.
- **Avalonia:** único framework .NET de UI com Linux e Android no mesmo código; Electron foi descartado pelo consumo de memória de um app residente. O suporte mobile é menos maduro, por isso há spike na Fase 0.
- **SQLite + Dapper:** arquivo único, offline, SQL explícito e fácil de trocar atrás dos repositórios; EF Core traria cerimônia desnecessária para 4 tabelas.
- **CommunityToolkit.Mvvm e DI da Microsoft:** padrões do ecossistema, com source generators e pouca cerimônia.
- **Sem biblioteca de gráficos no início:** menos dependência e menos risco de incompatibilidade com o Avalonia.

**Ambiente:** o workload Android (`dotnet workload install android`), o JDK e o Android SDK ainda não estão instalados; a instalação faz parte do spike Android da Fase 0.

## 5. Arquitetura hexagonal (portas e adaptadores), versão pragmática

    SlimeGoals.Desktop ─┐                        SlimeGoals.Android ─┐
                        ▼                                           ▼
                    SlimeGoals.App (UI Avalonia: Views + ViewModels compartilhados)
                        │
                        ▼
                    SlimeGoals.Core (domínio + serviços + interfaces)
                        ▲
                        │ implementam as interfaces
       SlimeGoals.Infrastructure (SQLite, repositórios)      [portátil]
       SlimeGoals.Platform.Linux   (DBus, autostart, timer)   [net10.0]
       SlimeGoals.Platform.Android (AlarmManager, notificação) [net10.0-android]

**Regra de dependência:** `Core` não referencia ninguém. `Infrastructure`, `Platform.*` e `App` referenciam `Core`. A composição (DI) acontece só nos heads (`Desktop` e `Android`).

**Por que separar `Platform.*`:** código que depende do workload Android não compila para Linux; isolar os adaptadores por plataforma mantém `Core` e `Infrastructure` portáteis.

**Mapeamento:** `Core` é o hexágono; as interfaces abaixo são as portas de saída; `Infrastructure` e `Platform.*` contêm os adaptadores de saída; `App` é o adaptador de entrada (UI).

**Princípio dos lembretes:** o `ReminderPlanner` calcula o próximo disparo e o scheduler registra **um único** disparo. No Linux é um timer do processo; no Android é um alarme do sistema (o processo pode ser encerrado a qualquer momento).

**Simplificações assumidas no MVP:** domínio e serviços ficam no mesmo projeto (`Core`), e os ViewModels chamam serviços concretos, sem interfaces de entrada. Se o projeto crescer, o `Core` pode ser dividido em `Domain` e `Application`.

**Portas previstas para depois do MVP:** `ISyncService` e `IAuthService`. Não serão implementadas agora.

**Interfaces-chave no Core (pontos de variação por plataforma):**

```csharp
public interface IGoalRepository { /* CRUD de metas */ }
public interface ICheckinRepository { /* add, undo, soma por período, query por período */ }
public interface IReminderScheduler { void Reschedule(); void Pause(); void Resume(); }
public interface INotificationService
{
    void Show(Reminder reminder);                                  // não bloqueia
    event EventHandler<ReminderActionEventArgs>? ActionReceived;  // Feito | Adiar | Ignorar (Linux)
}
public interface IAutostartService { bool IsEnabled { get; } void Set(bool enabled); }
public interface IReportService { Task<Report> BuildAsync(ReportPeriod period, DateOnly reference); }
```

**Componentes do Core obrigatórios:**

- `GoalPeriod` (puro, sem I/O): dado uma meta e uma data local, devolve o **período de avaliação** `[início, fim)` em datas locais (ver seção 5.1). É a única fonte da regra "qual intervalo conta para o progresso" e é usado pela tela Hoje, pelo `ReminderPlanner` e pelo `ReportService`.
- `ReminderResponseHandler`: recebe a ação (Feito → check-in; Adiar → +N min; Ignorar → próximo intervalo) e pede reagendamento. Usa só repositórios, sem UI. No Linux é chamado pelo evento `ActionReceived`; no Android, diretamente pelo `BroadcastReceiver`, inclusive com o app fechado.

### 5.1 Tipos de recorrência e período de avaliação

| Tipo               | Período de avaliação                                  | Alvo (`target`)           | Cumprida quando                 | Lembretes param quando                                    | Sequência                                                  |
|--------------------|-------------------------------------------------------|---------------------------|---------------------------------|-----------------------------------------------------------|------------------------------------------------------------|
| `once` (única)     | de `start_date` (ou criação) até `end_date` (ou aberto) | sempre 1 (campo oculto)   | 1 check-in                      | concluída (`completed_at` preenchido e meta arquivada)    | não tem; só "concluída" ou "pendente"                      |
| `daily` (diária)   | o dia local                                           | check-ins por dia (≥ 1)   | soma do dia ≥ `target`          | alvo do dia atingido; voltam no próximo dia ativo         | dias ativos consecutivos cumpridos; dias inativos não quebram |
| `weekly` (semanal) | a semana local, **segunda a domingo** (fixo)          | check-ins por semana (≥ 1) | soma da semana ≥ `target`       | alvo da semana atingido; voltam na próxima semana         | semanas consecutivas cumpridas                             |

Regras comuns:

- `active_days` na meta diária define **quais dias contam** (dias inativos não aparecem em "Hoje", não geram lembretes e não entram no cálculo de sequência). Na semanal, define **em quais dias o app lembra**; check-ins em qualquer dia contam para a semana.
- O período de avaliação é sempre recortado por `start_date`/`end_date`. Semanas e dias parciais **não** proporcionalizam o alvo.
- O período atual (ou o dia de hoje) ainda em andamento não quebra a sequência; ela é avaliada até o último período fechado, somando o atual se já estiver cumprido.
- Meta `once` concluída: o `CheckinService` preenche `completed_at` e `archived_at` na mesma transação. Desfazer o check-in reverte os dois.

## 6. Estrutura da solução

    SlimeGoals/
    ├── specs/                           # especificações e protótipos (versionados)
    ├── src/
    │   ├── SlimeGoals.Core/                 # net10.0
    │   │   ├── Domain/            # Goal, Recurrence, GoalPeriod, Checkin, ReminderLog, Report
    │   │   ├── Services/          # GoalService, CheckinService, ProgressService, ReportService, ReminderPlanner, ReminderResponseHandler
    │   │   └── Abstractions/      # interfaces acima
    │   ├── SlimeGoals.Infrastructure/       # net10.0 (portátil)
    │   │   └── Data/              # Connection, Migrations/*.sql, Repositories
    │   ├── SlimeGoals.Platform.Linux/       # net10.0
    │   │   ├── Scheduling/        # timer único
    │   │   ├── Notifications/     # DBus (freedesktop) + fallback em janela
    │   │   └── Autostart/         # XDG autostart
    │   ├── SlimeGoals.Platform.Android/     # net10.0-android
    │   │   ├── Scheduling/        # AlarmManager + receiver de boot
    │   │   └── Notifications/     # canal, ações, permissões
    │   ├── SlimeGoals.App/                  # biblioteca Avalonia compartilhada
    │   │   ├── Views/             # TodayView, GoalsView, GoalEditView, ReportsView, SettingsView
    │   │   ├── ViewModels/
    │   │   ├── Assets/Fonts/      # Figtree
    │   │   ├── Styles/            # Theme.axaml (tokens), Controls.axaml
    │   │   └── App.axaml
    │   ├── SlimeGoals.Desktop/              # head Linux: Program.cs, DI, bandeja, instância única
    │   └── SlimeGoals.Android/              # head Android: MainActivity, DI, receivers
    ├── tests/
    │   ├── SlimeGoals.Core.Tests/
    │   └── SlimeGoals.Infrastructure.Tests/
    └── SlimeGoals.sln

Ponto de partida: `dotnet new install Avalonia.Templates` e `dotnet new avalonia.xplat -n SlimeGoals` (gera Core de UI + heads desktop/mobile). Depois reorganize para a estrutura acima e aponte todos os projetos para `net10.0` (ou `net10.0-android`).

## 7. Telas

| Tela              | Conteúdo                                                                                                                                                       |
|-------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Hoje**          | Resumo da semana (% e check-ins), metas em andamento/concluídas, sequência; lista "Metas de hoje" com progresso no período atual e botão de check-in; próximos lembretes do dia |
| **Metas**         | Lista (filtro ativas/arquivadas), criar, editar, arquivar                                                                                                       |
| **Editar meta**   | Título, tipo (única/diária/semanal), alvo (oculto em única), ícone e cor, intervalo, horário ativo, dias da semana, início e fim opcionais                      |
| **Relatórios**    | Alternar semanal/mensal, navegar entre períodos, barras por dia/semana + tabela por meta                                                                       |
| **Configurações** | Autostart, duração do "adiar", ao fechar a janela (desktop)                                                                                                     |
| **Lembrete**      | Notificação nativa do SO com as três ações (janela própria do Avalonia só como fallback)                                                                       |

Navegação: barra lateral à esquerda no desktop (v4) e navegação inferior no Android (v3) com **Hoje, Metas, Relatórios, Configurações** (o protótipo v3 usa "Semana" e "Ajustes"; o MVP padroniza os nomes acima). Os ViewModels são compartilhados; só o shell de navegação muda. O breakpoint de 720 px é o ponto de partida do layout adaptativo.

Na tela Hoje, as metas aparecem assim: diária → "3 de 8 hoje"; semanal → "3 de 5 na semana"; única → "Pendente · até 15/11" (ou sem prazo).

## 8. Modelo de dados (SQLite)

```sql
CREATE TABLE goals (
  id TEXT PRIMARY KEY,                -- GUID
  title TEXT NOT NULL,
  recurrence TEXT NOT NULL,           -- 'once' | 'daily' | 'weekly'
  target INTEGER NOT NULL DEFAULT 1,  -- alvo por período; sempre 1 em 'once'
  icon TEXT NOT NULL,                 -- chave de um conjunto fixo de ícones do app
  color TEXT NOT NULL,                -- chave da paleta de metas (seção 11)
  reminder_every_min INTEGER NOT NULL,
  active_from TEXT NOT NULL,          -- 'HH:MM'
  active_until TEXT NOT NULL,
  active_days TEXT NOT NULL,          -- '1,2,3,4,5' (1 = segunda)
  start_date TEXT,                    -- 'YYYY-MM-DD' (data local), opcional
  end_date TEXT,                      -- 'YYYY-MM-DD' (data local), opcional
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  completed_at TEXT,                  -- só 'once': instante da conclusão
  archived_at TEXT,
  deleted_at TEXT,                    -- exclusão lógica
  CHECK (recurrence IN ('once','daily','weekly')),
  CHECK (target >= 1),
  CHECK (recurrence <> 'once' OR target = 1)
);

CREATE TABLE checkins (
  id TEXT PRIMARY KEY,                -- GUID
  goal_id TEXT NOT NULL REFERENCES goals(id),
  done_at TEXT NOT NULL,              -- ISO 8601 (UTC)
  amount INTEGER NOT NULL DEFAULT 1,
  note TEXT,
  updated_at TEXT NOT NULL,
  deleted_at TEXT
);

CREATE TABLE reminder_log (
  id TEXT PRIMARY KEY,                -- GUID
  goal_id TEXT NOT NULL REFERENCES goals(id),
  fired_at TEXT NOT NULL,
  response TEXT,                      -- 'done' | 'snooze' | 'ignore' | NULL
  responded_at TEXT
);

CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE INDEX idx_checkins_goal_date ON checkins(goal_id, done_at);
CREATE INDEX idx_reminder_log_goal ON reminder_log(goal_id, fired_at);
```

Guarde **instantes** (`done_at`, `fired_at`, `created_at`…) em UTC e converta para o fuso local na camada de apresentação e no cálculo de período. `start_date` e `end_date` são **datas de calendário locais**, não instantes: gravá-las em UTC deslocaria o dia em alguns fusos. Para consultar check-ins de um período, o Core converte `[início, fim)` local para UTC (via `GoalPeriod`) e o repositório filtra `done_at` nesse intervalo.

**Persistência e usuários (decisão do MVP):** sem login e sem sincronização. Os dados ficam em um arquivo SQLite na pasta do usuário do sistema operacional, que já isola um usuário do outro. Para não precisar refatorar se a sincronização vier depois, o modelo já usa GUIDs, `updated_at` e exclusão lógica (`deleted_at`); todas as consultas e relatórios devem ignorar registros com `deleted_at` preenchido.

No Linux, o arquivo fica em `$XDG_DATA_HOME` (padrão `~/.local/share/SlimeGoals`); no Android, no armazenamento privado do app.

## 9. Lembretes

1.  `ReminderPlanner` (Core, puro e testável) calcula o próximo disparo de cada meta elegível. Uma meta é elegível se não está arquivada nem excluída, está dentro de `start_date`/`end_date`, o dia é ativo e o período atual **não** está cumprido (seção 5.1).
2.  **Regra do intervalo:** o próximo disparo é `max(último disparo, último check-in) + reminder_every_min`, limitado à janela `active_from`–`active_until` do dia. Se não houver disparo nem check-in na janela de hoje, o primeiro disparo é em `active_from`. Se o horário calculado passar de `active_until`, vai para `active_from` do próximo dia em que a meta for elegível. Adiar substitui o próximo disparo por `agora + adiar`; ignorar segue a regra normal.
3.  O estado do adiamento é reconstruído a partir do `reminder_log` (último disparo + resposta), para sobreviver a reinício.
4.  O scheduler mantém **um único disparo** apontado para o mais próximo entre todas as metas e reagenda ao criar/editar meta, registrar check-in, responder lembrete ou retomar do repouso. **Linux:** timer do processo. **Android:** alarme exato do sistema (`AlarmManager`), reagendado por um receiver de boot.
5.  Ao disparar: grava em `reminder_log` e chama `INotificationService.Show`. A resposta chega depois e é tratada pelo `ReminderResponseHandler`. Notificações não bloqueiam o código, por isso o modelo é baseado em evento (Linux) ou receiver (Android).
6.  Notificação sem resposta conta como ignorada ao expirar (prazo controlado pelo app, porque o GNOME tende a apenas esconder o banner e manter a notificação na central; validar no spike).
7.  **Linux (GNOME):** o app continua em segundo plano com a janela fechada; fechar oculta por padrão (configurável). O GNOME não exibe ícone de bandeja por padrão, então a bandeja é opcional (extensão AppIndicator). Sem ela, o app é reaberto pelo atalho do aplicativo (instância única) ou pela notificação. No Wayland, o compositor pode limitar trazer a janela para a frente; validar no spike e, se necessário, avaliar o portal de segundo plano (`xdg-desktop-portal` Background).
8.  **Android:** exige permissão de notificações (Android 13+), permissão de alarme exato (Android 12+) e tratamento do modo Doze, a validar no spike.
9.  Tratar suspensão do computador (Linux: recalcular ao retomar, via sinal do systemd-logind).

## 10. Relatórios

Calculados por query + Core (`GoalPeriod`), sem tabela própria. Pontos obrigatórios: ignorar registros com `deleted_at`; incluir metas arquivadas quando estavam ativas no período (RF09); converter os limites locais para UTC; semana sempre de segunda a domingo.

**Semanal:**

| Tipo     | O que mostra                                                                       |
|----------|------------------------------------------------------------------------------------|
| `weekly` | check-ins vs. alvo, % cumprido, melhor dia                                         |
| `daily`  | dias cumpridos / dias ativos da semana, %                                          |
| `once`   | tarefas concluídas na semana e pendentes com prazo na semana                       |
| Geral    | check-ins por dia (barras), lembretes disparados vs. respondidos                   |

**Mensal:** por meta, períodos cumpridos no mês (semanas para `weekly`, dias para `daily`), evolução semana a semana, sequência atual e maior sequência na unidade do tipo; tarefas únicas concluídas no mês. Só contam os períodos em que a meta estava ativa. Semanas que cruzam a virada do mês pertencem ao mês da sua segunda-feira.

A query abaixo é **ilustrativa** (base para metas semanais) e será refinada na Fase 5:

```sql
SELECT g.id, g.title, g.recurrence, g.target, COALESCE(SUM(c.amount), 0) AS done
FROM goals g
LEFT JOIN checkins c
       ON c.goal_id = g.id AND c.deleted_at IS NULL
      AND c.done_at >= @start AND c.done_at < @end
WHERE g.deleted_at IS NULL
  AND (g.archived_at IS NULL OR g.archived_at >= @start)
  AND (g.start_date IS NULL OR g.start_date <= @endLocalDate)
GROUP BY g.id;
```

## 11. Design system (tokens)

Referência visual: `design/v4-bento-desktop.html` (desktop) e `design/v3-hoje-mobile.html` / `design/v3-semana-mobile.html` (mobile). O `Metas — Design inicial` fica como histórico; o `Slime — mascote` é referência da Fase 8.

**Cores base:**

| Token            | Valor     | Uso                                                |
|------------------|-----------|----------------------------------------------------|
| `bg`             | `#0F0D15` | Fundo da janela (mobile)                           |
| `bg-deep`        | `#0C0A11` | Fundo do desktop (atrás dos painéis bento)         |
| `panel`          | `#15121C` | Painéis do desktop (barra lateral)                 |
| `surface`        | `#17141F` | Cartões (mobile)                                   |
| `tile`           | `#1A1722` | Tiles do bento (desktop)                           |
| `surface-2`      | `#1F1B2B` | Hover, campos, navegação inferior, botões-ícone    |
| `border`         | `#2A2538` | Divisórias e bordas de tiles                       |
| `text`           | `#E8E4F0` | Texto principal                                    |
| `muted`          | `#A39DB8` | Texto secundário                                   |
| `primary`        | `#9B7BEA` | Ações, progresso, barra em destaque                |
| `primary-soft`   | `#B9A3F0` | Ícones ativos, foco (`outline` 2 px)               |
| `primary-tint`   | `#D6C8FA` | Ícones sobre `surface-2`                           |
| `selected`       | `#2A2145` | Item de navegação ativo                            |
| `selected-strong`| `#3A2F63` | Segmento selecionado                               |
| `bar-idle`       | `#3A3350` | Barras de gráfico não destacadas                   |
| `on-primary`     | `#14101D` | Texto sobre `primary`                              |
| `success`        | `#7FD1AE` | Meta cumprida                                      |
| `warning`        | `#E8B86D` | Atenção                                            |
| `hero`           | gradiente `#7656D0` → `#43347F` (145°) | Cartão de destaque do resumo da semana |

**Paleta de metas (campo `color`):** cada chave tem fundo suave + cor do ícone.

| Chave    | Fundo     | Ícone     |
|----------|-----------|-----------|
| `purple` | `#2A2145` | `#B9A3F0` |
| `pink`   | `#3A2133` | `#F0A3C4` |
| `amber`  | `#382E1A` | `#E8B86D` |
| `green`  | `#1E3B33` | `#7FD1AE` |

**Forma e tipografia:** fonte Figtree (pesos 400–800) embutida; cantos de 14–16 px em controles, 22–28 px em cartões/tiles e navegação; sem sombras fortes; espaçamento em múltiplos de 4; alvos de toque ≥ 44 px. Implementar como `ResourceDictionary` em `Theme.axaml` e usar apenas via `DynamicResource`.

**Elementos dos protótipos fora do MVP** (sem dado correspondente; voltam quando houver): nome/saudação do usuário e avatar, calendário mensal de check-ins, "Onde você mais rende", comparação com a semana anterior ("8% acima"), "Última nota", "Atividade recente", tags. A contagem regressiva do próximo lembrete pode entrar na Fase 4b, pois usa o `ReminderPlanner`.

## 12. Plano de implementação

Linux primeiro, Android em seguida. Cada fase (ou par de fases) recebe um plano de implementação detalhado próprio antes de começar.

| Fase   | Entrega                  | Tarefas principais                                                                                                                                                                                                                                                                                                   | Critério de pronto                                                                               |
|--------|--------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------|
| **0**  | Fundação + spikes        | Repositório git, solução em `net10.0`, projetos, DI, `Theme.axaml` com Figtree, navegação lateral. Spikes descartáveis: (a) notificação com ações no GNOME via DBus, com arquivo `.desktop` e dica `desktop-entry`, testada com a janela oculta e com Não Perturbe ativo; (b) *hello world* Avalonia no Android com alarme + notificação com ação | App abre com o tema; notificação com ação responde no Linux; build Android roda e dispara alarme |
| **1**  | Metas (domínio + dados)  | Entidades, `Recurrence`, `GoalPeriod` (once/daily/weekly, segunda a domingo, UTC↔local), `001_init.sql`, repositórios, `GoalService`, testes                                                                                                                                                                         | CRUD e `GoalPeriod` passam nos testes; dados persistem após reiniciar                            |
| **2**  | Telas de metas           | `GoalsView`, `GoalEditView` (tipo, alvo, ícone/cor, período), validação, arquivamento automático ao fim                                                                                                                                                                                                              | Criar, editar e arquivar pela interface                                                          |
| **3**  | Check-ins e tela Hoje    | `CheckinService` (`amount` soma; `once` conclui e arquiva), `ProgressService`, `TodayView`, desfazer                                                                                                                                                                                                                 | Check-in atualiza o progresso na hora, nos três tipos                                            |
| **4a** | Planejador               | `ReminderPlanner`: regras da seção 9 escritas antes, testes com `FakeTimeProvider` para os três tipos                                                                                                                                                                                                               | Testes cobrem virada de dia e de semana, dias inativos, meta cumprida, adiar e ignorar           |
| **4b** | Agendador + resposta     | Scheduler Linux (timer único), `ReminderResponseHandler`                                                                                                                                                                                                                                                             | Disparo no horário e 3 ações funcionando                                                         |
| **4c** | Notificação nativa Linux | Adaptador DBus + fallback em janela; expiração = ignorado                                                                                                                                                                                                                                                            | Funciona com a janela principal fechada                                                          |
| **4d** | Segundo plano            | Fechar = ocultar, instância única, bandeja opcional (extensão AppIndicator no GNOME)                                                                                                                                                                                                                                 | Reabrir o app não duplica o processo                                                             |
| **5**  | Relatórios               | `ReportService` por tipo, `ReportsView`, barras em XAML, sequências em dias e semanas                                                                                                                                                                                                                                | Números conferem com os check-ins (testes), inclusive meta arquivada                             |
| **6**  | Linux: acabamento        | Configurações, autostart XDG, suspensão, ícones, arquivo `.desktop`, AppImage ou *self-contained*                                                                                                                                                                                                                    | Instalável e estável no GNOME (CachyOS)                                                          |
| **7a** | Android: UI              | Head Android, shell com navegação inferior (v3), layouts adaptados                                                                                                                                                                                                                                                   | Todas as telas usáveis no celular                                                                |
| **7b** | Android: lembretes       | `AlarmManager`, notificação com ações, permissões, receiver de boot, handler sem UI                                                                                                                                                                                                                                  | Lembrete dispara e as 3 ações funcionam com o app fechado, inclusive após reiniciar o aparelho   |
| **7c** | Android: distribuição    | APK/AAB assinado, versão mínima do Android definida                                                                                                                                                                                                                                                                  | Instalável em um aparelho real                                                                   |
| **8**  | Mascote (opcional)       | 3–4 poses estáticas na tela Hoje (slime já aprovado)                                                                                                                                                                                                                                                                 | Imagem muda conforme o progresso                                                                 |

**Ordem de trabalho em cada fase:** Core + testes (TDD), Infrastructure/Platform, ViewModel, View.

**Forma de trabalho:** o assistente implementa fase a fase e explica as decisões, padrões e trechos importantes; o autor revisa e aprova cada fase antes da próxima.

## 13. Estratégia de testes

- **Core (unitários):** `GoalPeriod` (três tipos, recorte por início/fim, virada de semana, horário de verão), `ReminderPlanner`, progresso, sequências, `ReportService`. Use `FakeTimeProvider` e fuso explícito nos testes.
- **Infrastructure (integração):** SQLite em memória (`Data Source=:memory:`) para repositórios e migrações, incluindo as restrições `CHECK`.
- **UI:** testes de ViewModel (sem renderizar); validação visual manual contra os protótipos.

## 14. Riscos e decisões em aberto

| Risco / decisão                                                                                          | Mitigação                                                                                                 |
|----------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------|
| Botões de ação em notificações no Linux dependem do servidor de notificação da distro                    | Spike na Fase 0 no ambiente alvo; fallback em janela própria                                              |
| GNOME não exibe ícone de bandeja por padrão                                                              | Bandeja opcional (extensão AppIndicator); instância única e notificação reabrem o app                     |
| Wayland (padrão do GNOME) pode impedir trazer a janela para a frente; o Avalonia roda via XWayland       | Testar no spike; alternativa: portal de segundo plano ou apenas notificar                                 |
| Modo Não Perturbe do GNOME silencia os banners                                                           | Documentar; lembretes continuam na central de notificações; avaliar aviso na tela Hoje                    |
| Android: o sistema encerra o processo e restringe alarmes (Doze, alarme exato, permissão de notificação) | Alarme do sistema + receiver de boot; handler de resposta sem UI; validar no spike da Fase 0 e na Fase 7b |
| Suporte mobile do Avalonia menos maduro                                                                  | Spike Android já na Fase 0, antes de investir nas telas                                                   |
| Fuso horário e horário de verão                                                                          | Instantes em UTC, datas de calendário locais, conversão na borda via `GoalPeriod`                         |
| Três tipos de recorrência multiplicam casos de borda                                                     | Regra centralizada em `GoalPeriod`; testes por tipo antes de qualquer tela                                |
| Semanas e dias parciais                                                                                  | Alvo não proporcionalizado; arquivamento automático ao fim                                                |
| Dapper vs. EF Core                                                                                       | Dapper no MVP; trocar é barato por causa dos repositórios                                                 |
| Login e sincronização Linux ↔ Android                                                                    | Fora do MVP (dados independentes por dispositivo); modelo já preparado (GUID, `updated_at`, `deleted_at`) |

## 15. Decisões registradas e próximos passos

**Decisões da versão 1.1–1.2 (mantidas):**

- Ambiente de desenvolvimento e alvo Linux: CachyOS (base Arch) com GNOME.
- Runtime: .NET 10 LTS.
- Plataformas iniciais: Linux e Android; Windows e macOS fora do escopo.
- Ao fechar a janela: ocultar em segundo plano por padrão, com opção de encerrar (desktop).
- `amount` soma ao progresso (padrão 1).
- Semanas parciais não proporcionalizam o alvo; meta arquivada automaticamente ao passar o fim.

**Decisões da versão 1.3 (novas):**

- Recorrência `once` (tarefa única, um check-in conclui e arquiva), `daily` (alvo por dia + dias ativos) e `weekly` (alvo por semana); `target_per_week` vira `target`.
- Sequências na unidade de cada tipo: dias (diária), semanas (semanal), sem sequência (única).
- Período de avaliação centralizado em `GoalPeriod` no Core.
- Semana fixa de segunda a domingo; a configuração "início da semana" foi removida.
- Configuração "Tema" removida do MVP (só existe tema escuro).
- Intervalo de lembrete contado a partir do último disparo ou check-in, dentro da janela ativa.
- Referência visual: v4 (desktop) e v3 (mobile), apenas com dados existentes no MVP; ícone e cor por meta entram no modelo.
- Fonte única Figtree (os protótipos usam Figtree no mobile e JetBrains Mono no desktop).
- Navegação padronizada: Hoje, Metas, Relatórios, Configurações.
- `specs/` versionada no repositório git junto com o código.
- Forma de trabalho: o assistente implementa e explica; o autor revisa e aprova por fase.

**Pendências:** versão mínima do Android (Fase 7c); sincronização entre dispositivos (fora do MVP).

**Próximos passos imediatos:**

1.  Revisar e aprovar esta v1.3.
2.  Escrever o plano de implementação detalhado das Fases 0 e 1.
3.  Executar a Fase 0: solução, tema, navegação e os dois spikes.
