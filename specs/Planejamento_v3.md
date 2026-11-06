# Gestor de Metas — Especificação e Plano de Implementação (Avalonia + C#)

> Versão 1.4 · Alvos iniciais: Linux (desktop) e Android · .NET 10 LTS · Dark mode roxo suave
>
> Substitui a v1.2 (`Planejamento_v2.md`, mantida como histórico). Mudanças principais: tipos de recorrência (`daily`, `weekly`; a tarefa única `once` foi removida em 01/11/2026) unificados pelo conceito de **período de avaliação**; semana fixa de segunda a domingo; referência visual v4 (desktop) e v3 (mobile); ícone e cor por meta; **medida** por meta (conclusão, quantidade ou tempo, v1.4). Ver seção 15.

## 1. Visão geral

Aplicação multiplataforma para cadastrar metas (hábitos diários e metas semanais), receber lembretes periódicos até cumpri-las, registrar o progresso e gerar relatórios semanais e mensais sob demanda. Interface convencional com telas, tema escuro roxo/preto suave. O mascote (slime) aparece na tela Hoje, com poses que mudam conforme o dia (Fase 8).

**Dentro do escopo do MVP:** Linux (desktop, primeiro) e Android (em seguida), dados locais, lembretes, relatórios.

**Fora do escopo do MVP:** Windows e macOS, sincronização entre dispositivos, contas de usuário, mascote em outras telas, nas notificações ou com mais poses que as quatro do Hoje, tema claro, recursos dos protótipos sem dado correspondente (ver seção 11).

## 2. Requisitos funcionais

| ID   | Requisito                                                                                                                                                                                                                       |
|------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| RF01 | CRUD de metas: título, tipo de recorrência (`daily`, `weekly`), medida (`completion`, `quantity` com unidade, `duration`), alvo do período, ícone e cor, intervalo de lembrete, horário e dias ativos, início e fim opcionais                                                       |
| RF02 | Tela "Hoje": metas com progresso **no período atual de cada uma** (dia ou semana) e registro rápido conforme a medida (Feito, −/+, cronômetro)                                                                                             |
| RF03 | Registrar progresso com nota opcional: adicionar ou remover valor (o total do período nunca fica negativo); metas de tempo com lançamento manual ou cronômetro que sobrevive ao fechamento do app |
| RF04 | Lembretes periódicos por meta, respeitando horário e dias ativos; ações **Feito**, **Adiar**, **Ignorar**                                                                                                                       |
| RF05 | Meta cumprida no período atual não gera mais lembretes até o próximo período (diária: dia seguinte ativo; semanal: próxima semana)                                                                            |
| RF06 | Relatório semanal e mensal por solicitação (progresso por meta, % cumprido, sequências na unidade de cada tipo)                                                                                      |
| RF07 | Ícone na bandeja: abrir, pausar lembretes, sair                                                                                                                                                                                 |
| RF08 | Configurações: iniciar com o sistema, duração padrão do "adiar", ao fechar a janela (ocultar em segundo plano, padrão, ou encerrar; só desktop)                                                                                 |
| RF09 | Arquivar meta sem perder histórico                                                                                                                                                                                              |
| RF10 | Período e recorrência: início e fim opcionais. Fora do período, a meta não aparece em "Hoje" nem gera lembretes; ao passar o fim, é arquivada automaticamente |
| RF11 | No Android, lembretes agendados pelo sistema (`AlarmManager`), com as ações funcionando mesmo com o app fechado                                                                                                                 |

## 3. Requisitos não funcionais

- **Offline e local:** SQLite, sem servidor. A sincronização opcional está em `Sincronizacao_v1.md`; sem conta, nada muda.
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
| Empacotamento | `dotnet publish` por RID (linux-x64); `.tar.gz` *self-contained* com `install.sh` e arquivo `.desktop` no Linux (AppImage e PKGBUILD ficam como opcionais futuros); APK assinado no Android |

**Justificativa das escolhas:**

- **C# / .NET 10:** um único runtime para Core, dados e UI; domínio já conhecido pelo autor; o .NET 8 sai de suporte em 10/11/2026.
- **Avalonia:** único framework .NET de UI com Linux e Android no mesmo código; Electron foi descartado pelo consumo de memória de um app residente. O suporte mobile é menos maduro, por isso há spike na Fase 0.
- **SQLite + Dapper:** arquivo único, offline, SQL explícito e fácil de trocar atrás dos repositórios; EF Core traria cerimônia desnecessária para 4 tabelas.
- **CommunityToolkit.Mvvm e DI da Microsoft:** padrões do ecossistema, com source generators e pouca cerimônia.
- **Sem biblioteca de gráficos no início:** menos dependência e menos risco de incompatibilidade com o Avalonia.

**Ambiente:** workload Android instalado no SDK da distro (`sudo dotnet workload install android`), JDK 21 do `pacman` e Android SDK em `~/Android/Sdk`; comandos em `specs/spikes/android-alarme.md`.

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
       SlimeGoals.Android (head: AlarmManager, notificação, receivers) [net10.0-android]

**Regra de dependência:** `Core` não referencia ninguém. `Infrastructure`, `Platform.Linux` e `App` referenciam `Core`. A composição (DI) acontece só nos heads (`Desktop` e `Android`).

**Por que separar as plataformas:** código que depende do workload Android não compila para Linux; manter os adaptadores do Android no head e o agendador (`SystemAlarmReminderScheduler`, com a porta `ISystemAlarm`) no `Core` mantém `Core` e `Infrastructure` portáteis e a lógica testável. Não há `Platform.Android` (Fase 7b, decisão 10): os receivers precisam do container do `AndroidApplication`, e um projeto a mais não ganharia teste.

**Mapeamento:** `Core` é o hexágono; as interfaces abaixo são as portas de saída; `Infrastructure`, `Platform.Linux` e o head Android (`SlimeGoals.Android/Reminders`) contêm os adaptadores de saída; `App` é o adaptador de entrada (UI).

**Princípio dos lembretes:** o `ReminderPlanner` calcula o próximo disparo e o scheduler registra **um único** disparo. No Linux é um timer do processo; no Android é um alarme do sistema (o processo pode ser encerrado a qualquer momento).

**Simplificações assumidas no MVP:** domínio e serviços ficam no mesmo projeto (`Core`), e os ViewModels chamam serviços concretos, sem interfaces de entrada. Se o projeto crescer, o `Core` pode ser dividido em `Domain` e `Application`.

**Portas previstas para depois do MVP:** `ISyncService` e `IAuthService`. Não serão implementadas agora.

**Interfaces-chave no Core (pontos de variação por plataforma):**

```csharp
public interface IGoalRepository { /* CRUD de metas */ }
public interface ICheckinRepository { /* add, undo, soma por período, query por período */ }
public interface IReminderScheduler { void Reschedule(); }  // Fase 4: assíncrono (Task); Pause/Resume ficaram no Core (`ReminderPause`), não no agendador (Fase 4d)
public interface INotificationService
{
    void Show(Reminder reminder);                                  // não bloqueia (Fase 4: Task)
    event EventHandler<ReminderActionEventArgs>? ActionReceived;  // Feito | Adiar | Ignorar (Linux)
}
public interface IAutostart { bool IsEnabled(); void Enable(); void Disable(); }  // Core/Abstractions
public interface IReportService { Task<Report> BuildAsync(ReportPeriod period, DateOnly reference); }
```

**Componentes do Core obrigatórios:**

- `GoalPeriod` (puro, sem I/O): dado uma meta e uma data local, devolve o **período de avaliação** `[início, fim)` em datas locais (ver seção 5.1). É a única fonte da regra "qual intervalo conta para o progresso" e é usado pela tela Hoje, pelo `ReminderPlanner` e pelo `ReportService`.
- `ReminderResponseHandler`: recebe a ação (ação principal → registro conforme a medida, ver seção 9 item 11; Adiar → +N min; Ignorar → próximo intervalo) e pede reagendamento. Usa só repositórios, sem UI. No Linux é chamado pelo evento `ActionReceived`; no Android, diretamente pelo `BroadcastReceiver`, inclusive com o app fechado.

### 5.1 Tipos de recorrência e período de avaliação

| Tipo               | Período de avaliação                                  | Alvo (`target`)           | Cumprida quando                 | Lembretes param quando                                    | Sequência                                                  |
|--------------------|-------------------------------------------------------|---------------------------|---------------------------------|-----------------------------------------------------------|------------------------------------------------------------|
| `daily` (diária)   | o dia local                                           | por dia (≥ 1)             | soma do dia ≥ `target`          | alvo do dia atingido; voltam no próximo dia ativo         | dias ativos consecutivos cumpridos; dias inativos não quebram |
| `weekly` (semanal) | a semana local, **segunda a domingo** (fixo)          | por semana (≥ 1)          | soma da semana ≥ `target`       | alvo da semana atingido; voltam na próxima semana         | semanas consecutivas cumpridas                             |

### 5.2 Medida da meta (v1.4)

A medida é independente da recorrência: diz **o que** se conta no período; a recorrência diz **qual** é o período.

| Medida                  | Alvo (`target`)                                        | Valor de cada registro (`amount`)  | Progresso no Hoje            |
|-------------------------|--------------------------------------------------------|------------------------------------|------------------------------|
| `completion` (conclusão) | sempre 1 (campo oculto): feito ou não feito no período | 1 = feito; −1 = desmarcar          | "Feito hoje" / "Pendente"    |
| `quantity` (quantidade)  | inteiro ≥ 1 na **unidade livre** da meta (`unit`, até 20 caracteres: "copos", "páginas", "vezes") | inteiro com sinal, em unidades | "5 de 8 copos hoje"          |
| `duration` (tempo)       | minutos ≥ 1 (o formulário mostra horas + minutos)       | inteiro com sinal, em minutos      | "2 h 30 de 5 h na semana"    |

- **Registros com sinal:** o usuário ajusta o total do período com **+** e **−** (ou digitando um valor e escolhendo Adicionar/Remover). Cada ajuste é um registro em `checkins`; o progresso é a soma do período. Assim os relatórios sabem quando cada coisa aconteceu. O total do período **nunca fica negativo**: remover mais do que existe é recusado. Não há edição nem "desfazer" de registros individuais no MVP.
- **Limites:** alvo da quantidade 1–9999; alvo do tempo 1 min–24 h na diária e 1 min–168 h na semanal; unidade até 20 caracteres; nota até 200; valor de um registro: ±1 na conclusão, até 9999 na quantidade e até 24 h no tempo (em módulo).
- **Só inteiros:** sem frações (quem precisa usa uma unidade menor, como ml). Evita arredondamento no SQLite.
- **Cronômetro (só `duration`):** um por vez em todo o app, gravado em `active_timer` (instante de início em UTC), para sobreviver ao fechamento do app e ao processo morto no Android. Ao parar, o tempo é arredondado para minutos (`stopped_at` gravado, para não se perder se o app fechar) e mostrado para conferência; o usuário registra (pode ajustar) ou descarta. Iniciar outro cronômetro pede para parar o atual.
- **Trocar a medida** só é permitido enquanto a meta não tem registros (30 "copos" não viram 30 minutos). A recorrência continua podendo ser trocada sempre.
- Cumprida quando a soma do período ≥ `target` (conclusão: ≥ 1). A soma pode passar do alvo.

Regras comuns:

- `active_days` na meta diária define **quais dias contam** (dias inativos não aparecem em "Hoje", não geram lembretes e não entram no cálculo de sequência). Na semanal, define **em quais dias o app lembra**; registros em qualquer dia contam para a semana.
- O período de avaliação é sempre recortado por `start_date`/`end_date`. Semanas e dias parciais **não** proporcionalizam o alvo.
- O período atual (ou o dia de hoje) ainda em andamento não quebra a sequência; ela é avaliada até o último período fechado, somando o atual se já estiver cumprido.

## 6. Estrutura da solução

    SlimeGoals/
    ├── specs/                           # especificações e protótipos (versionados)
    ├── src/
    │   ├── SlimeGoals.Core/                 # net10.0
    │   │   ├── Domain/            # Goal, Recurrence, GoalPeriod, Checkin, ReminderLog, Report, DailySums, GoalHistory, Streaks
    │   │   ├── Services/          # GoalService, CheckinService, ProgressService, ReportService, ReminderPlanner, ReminderResponseHandler, SystemAlarmReminderScheduler (Android)
    │   │   └── Abstractions/      # interfaces acima
    │   ├── SlimeGoals.Infrastructure/       # net10.0 (portátil)
    │   │   └── Data/              # Connection, Migrations/*.sql, Repositories
    │   ├── SlimeGoals.Platform.Linux/       # net10.0
    │   │   ├── Scheduling/        # timer único
    │   │   ├── Notifications/     # DBus (freedesktop) + fallback em janela
    │   │   ├── SingleInstance/    # guarda de instância única (trava + socket Unix)
    │   │   ├── Autostart/         # XdgAutostart (arquivo em ~/.config/autostart)
    │   │   └── Power/             # suspensão: logind PrepareForSleep, SleepResumeWatcher
    │   ├── SlimeGoals.App/                  # biblioteca Avalonia compartilhada
    │   │   ├── Views/             # TodayView, GoalsView, GoalEditView, ReportsView, SettingsView
    │   │   ├── ViewModels/
    │   │   ├── Assets/Fonts/      # Figtree
    │   │   ├── Styles/            # Theme.axaml (tokens), Controls.axaml
    │   │   └── App.axaml          # inclui o ícone da bandeja
    │   ├── SlimeGoals.Desktop/              # head Linux: Program.cs, DI, liga a instância única e a suspensão ao app (SleepResumeHost)
    │   └── SlimeGoals.Android/              # head Android: AndroidApplication, MainActivity, AndroidCompositionRoot
    │       └── Reminders/     # AndroidSystemAlarm (AlarmManager), AndroidNotificationService (canal, ações), ReminderReceivers.cs (receivers de alarme, ação, boot e eventos do sistema), AndroidReminderPermissions, NoopWindowActivator
    ├── tools/SlimeGoals.Render/         # renderização headless de cada página (412×915 e 1100×720), fora das soluções
    ├── packaging/linux/                 # build.sh, install.sh, uninstall.sh, slimegoals.desktop.in, make-icons.sh, icons/, licenses/
    ├── tests/
    │   ├── SlimeGoals.Core.Tests/
    │   └── SlimeGoals.Infrastructure.Tests/
    ├── SlimeGoals.slnx                  # sem o head Android (o `dotnet test` não exige o workload `android`)
    └── SlimeGoals.Android.slnx          # Core, Infrastructure, App e Android

Ponto de partida: `dotnet new install Avalonia.Templates` e `dotnet new avalonia.xplat -n SlimeGoals` (gera Core de UI + heads desktop/mobile). Depois reorganize para a estrutura acima e aponte todos os projetos para `net10.0` (ou `net10.0-android`).

## 7. Telas

| Tela              | Conteúdo                                                                                                                                                       |
|-------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Hoje**          | Cartão de destaque "Progresso da semana" (%, barra, balão e slime com pose conforme o dia, Fase 8) e três tiles ("Em andamento", "Concluídas hoje", "Sequência": maior sequência atual entre as metas ativas, com o título da meta); lista "Metas de hoje" com progresso no período atual e controles por medida (Feito/Desmarcar; −/+ e painel com valor e nota; cronômetro ▶/⏹ e lançamento manual); próximos lembretes do dia (Fase 4) |
| **Metas**         | Lista (filtro ativas/arquivadas), criar, editar, arquivar, desarquivar. Meta arquivada é somente leitura (só desarquivar ou excluir)                            |
| **Editar meta**   | Título, tipo (diária/semanal), medida (conclusão/quantidade/tempo; travada após o primeiro registro), alvo (+ unidade na quantidade; h + min no tempo; oculto na conclusão), ícone e cor, intervalo, horário ativo, dias da semana, início e fim opcionais. Abre como página no lugar da lista (desktop e celular); excluir com confirmação na própria página |
| **Relatórios**    | Segmentos Semana/Mês e botões Anterior/Próximo (abre no período atual; Próximo desabilitado nele; Anterior sem limite). Resumo (% cumprido, lembretes disparados vs. respondidos na semana), barras ("Metas com progresso por dia" na semana, "Evolução por semana" no mês) e tabela "Metas" com sequências; meta arquivada leva a tag "Arquivada". Período sem metas: "Nenhuma meta neste período." |
| **Configurações** | Seções "Geral" (Iniciar com o sistema), "Lembretes" (Adiar lembretes por: 5, 10, 15, 30 min ou 1 h) e "Janela" (Ao fechar a janela: Manter em segundo plano ou Encerrar o app). Mudanças salvam na hora; se falhar, volta ao valor anterior e mostra mensagem; falha ao carregar mostra "Não foi possível carregar a configuração." |
| **Lembrete**      | Notificação nativa do SO com as três ações (janela própria do Avalonia só como fallback)                                                                       |

Navegação: barra lateral à esquerda no desktop (v4) e navegação inferior no Android (v3) com **Hoje, Metas, Relatórios, Configurações** (o protótipo v3 usa "Semana" e "Ajustes"; o MVP padroniza os nomes acima). Os ViewModels são compartilhados; só o shell de navegação muda. O breakpoint de 720 px é o ponto de partida do layout adaptativo.

**Interface no Android (Fase 7a):** o `ShellView` usa a classe `compact` quando sua largura é menor que 720 px (também numa janela estreita no desktop). Nela, a barra lateral dá lugar à navegação inferior (ListBox ligada a `CurrentPage`, com ícones do Lucide: sun, list-checks, chart-column, sliders-horizontal), os tiles do Hoje ficam 2×2 (na Fase 8, o cartão de destaque ocupa a largura e os 3 tiles ficam numa linha), o formulário de meta ocupa a largura com as ações dentro da rolagem, os tiles de Relatórios empilham (barras de 22 px) e Configurações ocupa a largura toda. O botão Voltar do sistema chama `ShellViewModel.GoBack()`: fecha o editor sem salvar; em outra página volta para Hoje; em Hoje devolve falso e o sistema manda o app para segundo plano. O que aparece depende de `PlatformCapabilities(Reminders, Autostart, WindowClose, Quit)`: no desktop tudo verdadeiro; no Android da 7a tudo falso, o que esconde "Próximos lembretes" e "Pausar lembretes" no Hoje, as seções "Geral" e "Janela" em Configurações e o "Sair". O tile "Lembretes" de Relatórios continua visível no Android. **Fase 7b:** `Reminders` passa a verdadeiro no Android, e Hoje volta a mostrar "Próximos lembretes" e "Pausar lembretes".

Na tela Hoje, as metas aparecem assim: diária → "3 de 8 copos hoje"; semanal → "2 h 30 de 5 h na semana"; conclusão → "Feito hoje" / "Pendente". Metas cumpridas no período vão para o fim da lista, marcadas como concluídas.

**% da semana (resumo do Hoje):** média do progresso de cada meta visível na semana, limitado a 100% por meta. Semanal: soma da semana ÷ alvo. Diária: média da fração cumprida em cada dia ativo da semana até hoje, inclusive (dentro do período da meta e a partir do dia da criação, até 100% por dia); hoje entra com o progresso parcial. Não se somam valores de unidades diferentes.

## 8. Modelo de dados (SQLite)

```sql
CREATE TABLE goals (
  id TEXT PRIMARY KEY,                -- GUID
  title TEXT NOT NULL,
  recurrence TEXT NOT NULL,           -- 'daily' | 'weekly'
  target INTEGER NOT NULL DEFAULT 1,  -- alvo por período
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
  archived_at TEXT,
  deleted_at TEXT,                    -- sem uso desde a 004: excluir apaga a linha; a coluna fica para não recriar a tabela
  measure TEXT NOT NULL DEFAULT 'quantity', -- 'completion' | 'quantity' | 'duration' (v1.4)
  unit TEXT,                          -- só 'quantity': unidade livre, até 20 caracteres
  CHECK (recurrence IN ('daily','weekly')),
  CHECK (target >= 1),
  CHECK (measure IN ('completion','quantity','duration')),
  CHECK (measure <> 'completion' OR target = 1),
  CHECK ((measure = 'quantity') = (unit IS NOT NULL))
);
-- Esquema alvo após 001_init + 002_remove_once + 003_measures + 004_purge_deleted. A 002 recriou goals
-- sem 'once' e sem completed_at; a 003 adiciona measure/unit (metas existentes viram 'quantity' com
-- unidade 'vezes') e a tabela active_timer; a 004 só apaga dados (seção 15, exclusão definitiva).

CREATE TABLE checkins (
  id TEXT PRIMARY KEY,                -- GUID
  goal_id TEXT NOT NULL REFERENCES goals(id),
  done_at TEXT NOT NULL,              -- ISO 8601 (UTC)
  amount INTEGER NOT NULL DEFAULT 1,  -- com sinal, na unidade base da medida (seção 5.2); <> 0
  note TEXT,
  updated_at TEXT NOT NULL,
  deleted_at TEXT                     -- sem uso desde a 004, como em goals
);

CREATE TABLE reminder_log (
  id TEXT PRIMARY KEY,                -- GUID
  goal_id TEXT NOT NULL REFERENCES goals(id),
  fired_at TEXT NOT NULL,
  response TEXT,                      -- 'done' | 'snooze' | 'ignore' | NULL
  responded_at TEXT
);

CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);

CREATE TABLE active_timer (            -- no máximo uma linha (v1.4)
  id INTEGER PRIMARY KEY CHECK (id = 1),
  goal_id TEXT NOT NULL REFERENCES goals(id),
  started_at TEXT NOT NULL,           -- ISO 8601 (UTC)
  stopped_at TEXT                     -- preenchido = parado, aguardando registrar ou descartar
);
CREATE INDEX idx_checkins_goal_date ON checkins(goal_id, done_at);
CREATE INDEX idx_reminder_log_goal ON reminder_log(goal_id, fired_at);
```

Guarde **instantes** (`done_at`, `fired_at`, `created_at`…) em UTC e converta para o fuso local na camada de apresentação e no cálculo de período. `start_date` e `end_date` são **datas de calendário locais**, não instantes: gravá-las em UTC deslocaria o dia em alguns fusos. Para consultar check-ins de um período, o Core converte `[início, fim)` local para UTC (via `GoalPeriod`) e o repositório filtra `done_at` nesse intervalo.

**Persistência e usuários (decisão do MVP):** sem login e sem sincronização. Os dados ficam em um arquivo SQLite na pasta do usuário do sistema operacional, que já isola um usuário do outro. Para não precisar refatorar se a sincronização vier depois, o modelo já usa GUIDs e `updated_at`.

**Exclusão (decisão de 06/11/2026):** excluir uma meta apaga de vez, numa única transação, a linha em `goals` e tudo ligado a ela (`checkins`, `reminder_log`, `active_timer`), e o histórico dela some dos relatórios. Não há como desfazer. Toda conexão liga `PRAGMA secure_delete = ON`, para o conteúdo apagado não ficar legível no arquivo. As colunas `deleted_at` continuam no esquema, sem uso, e as consultas seguem ignorando as linhas com elas preenchidas. Se a sincronização vier, ela precisará de outro jeito de propagar exclusões (por exemplo, uma tabela só com os ids apagados).

No Linux, o arquivo fica em `$XDG_DATA_HOME` (padrão `~/.local/share/SlimeGoals`); no Android, no armazenamento privado do app.

## 9. Lembretes

1.  `ReminderPlanner` (Core, puro e testável) calcula o próximo disparo de cada meta elegível. Uma meta é elegível se não está arquivada nem excluída, está dentro de `start_date`/`end_date`, o dia é ativo e o período atual **não** está cumprido (seção 5.1).
2.  **Regra do intervalo:** o próximo disparo é `max(último disparo, último check-in) + reminder_every_min`, limitado à janela `active_from`–`active_until` do dia. Se não houver disparo nem check-in na janela de hoje, o primeiro disparo é em `active_from`; uma meta **criada hoje** dentro da janela conta o intervalo a partir da criação (salvar às 14:00 com intervalo de 1 h → primeiro lembrete às 15:00). Se o horário calculado passar de `active_until`, vai para `active_from` do próximo dia em que a meta for elegível. Adiar substitui o próximo disparo por `agora + adiar` (10 min por padrão; configurável em 5, 10, 15, 30 ou 60 min, chave `reminders.snooze_minutes`, valor ausente ou inválido vale 10; mudar vale na hora, inclusive para um adiar pendente, que segue a configuração atual); ignorar segue a regra normal. **Disparo atrasado:** se o horário calculado já passou (ex.: app aberto às 14:00 numa meta com janela desde 08:00), o lembrete dispara uma vez, na hora, e o intervalo recomeça a contar desse disparo.
3.  O estado do adiamento é reconstruído a partir do `reminder_log` (último disparo + resposta), para sobreviver a reinício.
4.  O scheduler mantém **um único disparo** apontado para o mais próximo entre todas as metas e reagenda ao criar/editar meta, registrar check-in, responder lembrete ou retomar do repouso. **Linux:** timer do processo, que também acorda à meia-noite local para avisar o `DataChangeNotifier`; a tela Hoje recarrega na virada do dia e depois de cada disparo, mantendo "Próximos lembretes" em dia. **Android:** `SystemAlarmReminderScheduler` arma um único alarme do sistema (`AlarmManager`) no próximo lembrete e o cancela se não houver nenhum; **sem alarme à meia-noite** (a tela Hoje recarrega ao voltar ao app). Receivers reagendam depois de boot, atualização do app, mudança de hora ou fuso e mudança da permissão de alarme exato. Se a sincronização falhar, arma nova tentativa em 15 min e registra em `Trace`.
5.  Ao disparar: grava em `reminder_log` e chama `INotificationService.Show`. A resposta chega depois e é tratada pelo `ReminderResponseHandler`. Notificações não bloqueiam o código, por isso o modelo é baseado em evento (Linux) ou receiver (Android).
6.  Notificação sem resposta conta como ignorada ao expirar e é retirada da central quando: dispara o próximo lembrete da mesma meta; a meta deixa de precisar de lembrete (cumprida, arquivada, cronômetro iniciado, fora da janela); a meta é excluída (nesse caso a exclusão retira a notificação e apaga o log: não sobra resposta gravada); o app encerra; ou o app abre e encontra registros pendentes de uma execução anterior. **Android:** pendentes **não expiram ao abrir o app** (a notificação segue na gaveta com ações que funcionam, pois o Android recria o processo para o receiver); valem as demais regras, e arrastar a notificação conta como ignorada. O prazo é controlado pelo app: o GNOME ignora `expire_timeout` e mantém a notificação na central (confirmado no spike, `specs/spikes/gnome-notificacoes.md`). Fechar a notificação no X (`NotificationClosed`, motivo 2, sem `ActionInvoked` antes) também conta como ignorada. Notificações deixadas por uma execução que caiu ficam na central do GNOME com ações que não funcionam (o mapa de ids do D-Bus estava na memória); a próxima abertura as marca como ignoradas.
7.  **Entrega das ações no Linux:** o GNOME envia `ActionInvoked` apenas para a conexão D-Bus que criou a notificação; com o processo encerrado, o clique é perdido sem aviso. Por isso o adaptador mantém uma única conexão de vida longa e, ao encerrar o app, retira as notificações pendentes (`CloseNotification`). A ativação via D-Bus (`org.gtk.Notifications` ou portal) é avaliada na Fase 4c.
8.  **Linux (GNOME):** o app continua em segundo plano com a janela fechada; fechar oculta por padrão, e a configuração "Ao fechar a janela" (`window.close_action`: `hide` ou `quit`) permite encerrar o app, o que equivale a "Sair"; sair por "Sair" na barra lateral, `Ctrl+Q` ou bandeja. "Iniciar com o sistema" grava `~/.config/autostart/slimegoals.desktop` com `--background`: o app abre sem janela e segue com lembretes e bandeja; uma segunda instância iniciada com `--background` sai sem mostrar a janela. Instância única por banco de dados (trava + socket Unix em `XDG_RUNTIME_DIR`): a segunda execução mostra a janela existente (no Wayland, o GNOME pode exibir "está pronto" em vez de trazê-la). Lembretes podem ser pausados e retomados (pausa persistida; ao retomar o intervalo recomeça, sem rajada). O GNOME não exibe ícone de bandeja por padrão, então a bandeja é opcional (extensão AppIndicator). Sem ela, o app é reaberto pelo atalho do aplicativo (instância única) ou pela notificação. No Wayland, clicar na notificação e chamar `Show()` + `Activate()` trouxe a janela à frente (spike); o GNOME também envia `ActivationToken` (xdg-activation), disponível se for preciso.
9.  **Android:** exige permissão de notificações (Android 13+) e de alarme exato (Android 12+); no Android 16 as duas vêm **negadas** num app recém-instalado e são pedidas num fluxo de primeira execução (`ACTION_REQUEST_SCHEDULE_EXACT_ALARM` abre direto a página do app). `SetExactAndAllowWhileIdle` dispara no horário mesmo com o processo morto e em Doze profundo; o Android cria o processo só para o receiver, e a ação da notificação chega a um `BroadcastReceiver` sem UI (confirmado no spike, `specs/spikes/android-alarme.md`). Desligar a permissão de alarme exato cancela os alarmes: reagendar quando ela voltar e avisar na tela Hoje se faltar alguma permissão. **Fase 7b:** o aviso na tela Hoje, com "Permitir", é o fluxo de primeira execução (aparece já na primeira abertura, sem tela de boas-vindas); "Permitir" pede notificações e depois alarmes exatos, e Configurações mostra o estado das duas. Sem a permissão de alarme exato, os lembretes seguem **inexatos** (`SetAndAllowWhileIdle`, que o sistema pode atrasar alguns minutos); quando ela volta, o sistema envia `ACTION_SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED` e o app reagenda exato. Notificação: canal `lembretes`, uma por disparo (tag = id do registro), botões da ação principal, "Adiar" e "Ignorar"; tocar no corpo abre o app sem registrar resposta. "Iniciar" com outro cronômetro rodando troca o texto por "Outro cronômetro está rodando. Toque para abrir o app." (o Android 12+ não deixa o receiver abrir uma tela), e o registro segue pendente. **Fase 7c:** Configurações ganha a linha "Bateria" ("Sem restrição" ou "Otimizada", com o botão "Liberar" que abre o diálogo do sistema, `ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`, pedindo a permissão `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`, aceitável fora da Play Store); ela só aparece no Android e não entra no aviso do Hoje. Medido no emulador: o Android considera permitido o alarme exato de um app isento de otimização de bateria, então "Liberar" também faz "Alarmes exatos" aparecer como permitido, e remover a isenção traz o bloqueio de volta.
10. Suspensão do computador (implementada na Fase 6): no Linux, o app escuta `PrepareForSleep` do systemd-logind; ao acordar, avisa o `DataChangeNotifier`, o agendador recalcula e a tela Hoje recarrega, mesmo após a meia-noite. Lembretes vencidos durante o sono disparam uma vez cada, na hora (regra do disparo atrasado). Sem barramento do sistema ou sem logind, o app segue sem isso e registra em `Trace`.
11. **Ação principal por medida (v1.4):** `completion` → **Feito** (registra 1); `quantity` → **+1** (soma 1 unidade); `duration` → **Iniciar** (liga o cronômetro sem abrir o app; se outro estiver rodando, abre o app para decidir). **Adiar** e **Ignorar** valem para as três. Meta com cronômetro rodando, ou parado aguardando registro, não gera lembretes; ao registrar, a regra do intervalo conta a partir do registro. Se o cronômetro rodando for de outra meta, **Iniciar** apenas abre o app (cronômetro de meta arquivada ou inexistente é órfão e não conta como conflito).

## 10. Relatórios

Sem tabela própria e sem query nova nos repositórios. Cada relatório faz uma leitura de registros do período (linhas com `deleted_at` ignoradas pelos repositórios), agrupa por dia local no Core (`DailySums`) e calcula o resto em `GoalHistory`, `Streaks` e `ReportService` (`GetWeekAsync`, `GetMonthAsync`, `MonthRange`, `GetTopStreakAsync`). Limites locais convertidos para UTC com `DateRange.ToUtc`; semana sempre de segunda a domingo; metas arquivadas entram quando estavam ativas no período (RF09).

**Semanal:**

| Tipo     | O que mostra                                                                       |
|----------|------------------------------------------------------------------------------------|
| `weekly` | soma vs. alvo na unidade da meta, % cumprido, melhor dia                           |
| `daily`  | dias cumpridos / dias contados da semana, %                                        |
| Geral    | metas com progresso por dia (barras), lembretes disparados vs. respondidos         |

**Mensal:** por meta, períodos cumpridos no mês (semanas para `weekly`, dias para `daily`), evolução semana a semana, sequência atual e maior sequência na unidade do tipo. O mês são as semanas cuja segunda-feira cai nele, inteiras; o cabeçalho mostra o intervalo real.

**Regras da Fase 5:**

- **Período contado.** Uma meta conta num dia quando ele está entre a data local de criação e a de arquivamento (inclusive), dentro do início e fim da meta e não é futuro. Diária: só dias ativos. Semanal: a semana conta se ao menos um dia dela conta; a soma usa o período de avaliação recortado. O período em andamento (hoje, semana atual) conta como pendente até ser cumprido: entra no % ("1 de 2 dias") mas não quebra a sequência.
- **% cumprido.** Diária: dias cumpridos ÷ dias contados. Semanal na semana: soma ÷ alvo, até 100% (a semana em andamento mostra o parcial). Semanal no mês: semanas cumpridas ÷ semanas contadas. Sem período contado, "—". O % geral é a média dos % que têm valor, com `MidpointRounding.AwayFromZero`. Difere do % do Hoje, em que a diária conta a fração parcial de cada dia.
- **Barras.** Semana: metas com progresso no dia local (soma líquida positiva; oito +1 de água contam como uma meta, e +1 seguido de −1 não conta). Mês: média dos % das metas em cada semana. Clicar numa barra mostra as metas que a compõem: na semana, o valor de cada meta no dia (✓ na diária cumprida); no mês, o % de cada meta na semana.
- **Lembretes.** Disparados = entradas do log no intervalo; respondidos = Feito ou Adiar. Ignorar e expiração ficam gravados do mesmo jeito (§9) e não contam como resposta.
- **Sequências.** Calculadas sobre todo o histórico da meta até o fim do período mostrado ou até hoje. Num mês passado aparecem como estavam no fim dele. Dias inativos da diária não quebram a sequência; meta arquivada para no dia do arquivamento.
- **Melhor dia** (só semanal): dia de maior soma positiva; no empate, o mais cedo.

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
| **1**  | Metas (domínio + dados)  | Entidades, `Recurrence`, `GoalPeriod` (daily/weekly, segunda a domingo, UTC↔local), `001_init.sql`, repositórios, `GoalService`, testes                                                                                                                                                                         | CRUD e `GoalPeriod` passam nos testes; dados persistem após reiniciar                            |
| **2**  | Telas de metas           | `GoalsView`, `GoalEditView` (tipo, alvo, ícone/cor, período), validação, arquivamento automático ao fim                                                                                                                                                                                                              | Criar, editar e arquivar pela interface                                                          |
| **3**  | Medidas, registros e Hoje | Medida da meta (`003_measures.sql`, formulário), `CheckinService` (ajustes com sinal, total ≥ 0), `TimerService` (`active_timer`), `ProgressService`, `TodayView` com controles por medida e resumo da semana | Registrar, remover e cronometrar atualizam o progresso na hora, nas três medidas e nos dois tipos |
| **4a** | Planejador               | `ReminderPlanner`: regras da seção 9 escritas antes, testes com `FakeTimeProvider` para os dois tipos                                                                                                                                                                                                                | Testes cobrem virada de dia e de semana, dias inativos, meta cumprida, adiar e ignorar           |
| **4b** | Agendador + resposta     | Scheduler Linux (timer único), `ReminderResponseHandler`                                                                                                                                                                                                                                                             | Disparo no horário e 3 ações funcionando                                                         |
| **4c** | Notificação nativa Linux | Adaptador DBus; expiração = ignorado. A janela principal fica aberta ou minimizada até a 4d (fechar ainda encerra o app); sem fallback em janela (fica para a Fase 6, se preciso; não foi feito na Fase 6)                                                                                                                                                                                                                                                            | Notificação com ações funciona no GNOME (janela aberta ou minimizada)                            |
| **4d** | Segundo plano            | Fechar = ocultar, instância única, bandeja opcional (extensão AppIndicator no GNOME). Implementado (verificação manual no GNOME pendente): bandeja com Abrir/Pausar/Sair; pausa também na tela Hoje                                                                                                                                                                                                                                 | Reabrir o app não duplica o processo; notificações funcionam com a janela principal fechada      |
| **5**  | Relatórios               | `ReportService` por tipo, `ReportsView`, barras em XAML, sequências em dias e semanas. Implementado (verificação manual pelo autor pendente): `DailySums`, `GoalHistory`, `Streaks` e `ReportService` no Core; `ReportsPageViewModel`, `ReportFormatting` e `ReportsView` no App; tile "Sequência" no Hoje; sem tabela nem query nova | Números conferem com os check-ins (testes), inclusive meta arquivada                             |
| **6**  | Linux: acabamento        | Configurações, autostart XDG, suspensão, ícones, arquivo `slimegoals.desktop` (`StartupWMClass=slimegoals`, para nome/ícone nas notificações), *self-contained* (AppImage ficou fora; `.tar.gz` self-contained) com os textos de licença das dependências. Implementado (verificação manual pelo autor pendente): `AppSettings` e `IAutostart` no Core; `XdgAutostart` e `SleepResumeWatcher` (logind) no Platform.Linux; `AppStartOptions` (`--background`) e `WindowCloseBehavior` no App; `SleepResumeHost` e `WM_CLASS` `slimegoals` no Desktop; tela de Configurações; ícone do slime no app e na bandeja; pacote `slimegoals-<versão>-linux-x64.tar.gz` (~46 MB, sem *trimming*) com `install.sh`/`uninstall.sh` em `~/.local`, textos de licença e versão 0.6.0 | Instalável e estável no GNOME (CachyOS)                                                          |
| **7a** | Android: UI              | Head Android, shell com navegação inferior (v3), layouts adaptados. Implementado: `PlatformCapabilities` e `ShellViewModel.GoBack()` no App; classe `compact` abaixo de 720 px, navegação inferior com ícones, ciclo de vida `IActivityApplicationLifetime` e fundo/fonte do tema sem janela; head `SlimeGoals.Android` (`dev.alcm.slimegoals`, API mínima 26, APK Debug, ícone do slime, banco em `FilesDir`, sem permissões, `NoopNotificationService`, sem `ReminderHost`); `SlimeGoals.Android.slnx`; `tools/SlimeGoals.Render` | Todas as telas usáveis no celular                                                                |
| **7b** | Android: lembretes       | Implementado: `ISystemAlarm` e `SystemAlarmReminderScheduler` no Core (um alarme no próximo lembrete, nova tentativa em 15 min se falhar); `IReminderPermissions`, aviso no Hoje e estado das permissões em Configurações no App; no head, `AndroidSystemAlarm` (exato ou `SetAndAllowWhileIdle`), `AndroidNotificationService` (canal, "Feito"/"+1"/"Iniciar", "Adiar", "Ignorar"), receivers de alarme, ação e eventos do sistema (boot, atualização, hora, fuso, permissão de alarme exato), `ReminderHost` com `NoopWindowActivator`, `Reminders` verdadeiro; permissões `POST_NOTIFICATIONS`, `SCHEDULE_EXACT_ALARM` e `RECEIVE_BOOT_COMPLETED` | Lembrete dispara e as 3 ações funcionam com o app fechado, inclusive após reiniciar o aparelho   |
| **7c** | Android: distribuição    | Implementado (testado pelo autor num celular real: instalação, notificação, lembrete e reinício; lembretes com a tela bloqueada por 1 h ou mais ainda pendentes): APK assinado por chave do autor (`packaging/android/`), versão mínima API 26, `versionCode` calculado da versão, textos de licença embutidos e seção "Sobre" em Configurações, linha "Bateria" com atalho para a isenção de otimização, ajustes manuais da Samsung no README. APK Release de 35 MB, só arm64 e x64                                                                                                                                                                                                                                                                  | Instalável em um aparelho real                                                                   |
| **8**  | Mascote (opcional)       | 4 poses na tela Hoje (slime já aprovado), com animação leve. Implementado (verificação manual pelo autor pendente): `MascotState`/`MascotPose` e `TodayPageViewModel.Mascot` no App; controle `SlimeMascot` (desenho em formas, animação própria); cartão de destaque no Hoje com balão; tokens `Hero*` no tema; modo `mascot` no `tools/SlimeGoals.Render` | Imagem muda conforme o progresso                                                                 |

**Ordem de trabalho em cada fase:** Core + testes (TDD), Infrastructure/Platform, ViewModel, View.

**Forma de trabalho:** o assistente implementa fase a fase e explica as decisões, padrões e trechos importantes; o autor revisa e aprova cada fase antes da próxima.

## 13. Estratégia de testes

- **Core (unitários):** `GoalPeriod` (dois tipos, recorte por início/fim, virada de semana, horário de verão), `ReminderPlanner`, progresso, `GoalHistory` (períodos contados), `Streaks`, `ReportService`. Use `FakeTimeProvider` e fuso explícito nos testes.
- **Infrastructure (integração):** SQLite em memória (`Data Source=:memory:`) para repositórios e migrações, incluindo as restrições `CHECK`, um teste dos relatórios com meta arquivada e registro excluído e, em arquivo, testes de que o conteúdo apagado não fica legível nos bytes do banco.
- **UI:** testes de ViewModel (sem renderizar; inclui `ReportsPageViewModel`); validação visual manual contra os protótipos.

## 14. Riscos e decisões em aberto

| Risco / decisão                                                                                          | Mitigação                                                                                                 |
|----------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------|
| Botões de ação em notificações no Linux dependem do servidor de notificação da distro                    | **Validado no GNOME 51** (spike da Fase 0); fallback em janela própria para servidores sem `actions`      |
| GNOME não exibe ícone de bandeja por padrão                                                              | Bandeja opcional (extensão AppIndicator), com o ícone do slime; instância única e notificação reabrem o app |
| Wayland (padrão do GNOME) pode impedir trazer a janela para a frente; o Avalonia roda via XWayland       | **Validado no spike:** a janela volta à frente ao clicar na notificação                                   |
| Ação de notificação perdida quando o processo do app não está rodando                                    | Retirar notificações pendentes ao sair; avaliar ativação D-Bus (`org.gtk.Notifications`/portal) na Fase 4c |
| Modo Não Perturbe do GNOME silencia os banners                                                           | Documentar; lembretes continuam na central de notificações; avaliar aviso na tela Hoje                    |
| Android: o sistema encerra o processo e restringe alarmes (Doze, alarme exato, permissão de notificação) | **Validado no emulador (Android 16):** alarme exato e ação funcionam com o processo morto e em Doze. **Fase 7b (emulador):** com o processo morto o alarme cria o processo e a notificação aparece; "+1", "Adiar", "Ignorar" e arrastar são tratados pelo receiver sem tela; revogar o alarme exato faz o Android encerrar o app e cancelar o alarme, que volta inexato na próxima abertura e exato quando a permissão retorna; após *cold boot*, `BOOT_COMPLETED` rearmou o alarme e o lembrete disparou sem abrir o app. **Fase 7c:** o caso da Samsung é coberto pela linha "Bateria" em Configurações (atalho para "Sem restrição") e pelos ajustes manuais do README (Bateria → "Sem restrições" e fora de "Apps em suspensão" e "Apps em suspensão profunda"); falta o autor conferir num aparelho real |
| Suporte mobile do Avalonia menos maduro                                                                  | **Validado no spike:** abertura a frio de 1,0–1,2 s (Debug, emulador); no Android o ciclo de vida é `IActivityApplicationLifetime`. **Fase 7a:** app completo com `TotalTime` de 1591 ms na primeira abertura e 1327 ms nas seguintes (Debug, emulador) |
| Fuso horário e horário de verão                                                                          | Instantes em UTC, datas de calendário locais, conversão na borda via `GoalPeriod`                         |
| Três tipos de recorrência multiplicam casos de borda                                                     | Regra centralizada em `GoalPeriod`; testes por tipo antes de qualquer tela                                |
| Semanas e dias parciais                                                                                  | Alvo não proporcionalizado; arquivamento automático ao fim                                                |
| Dapper vs. EF Core                                                                                       | Dapper no MVP; trocar é barato por causa dos repositórios                                                 |
| Login e sincronização Linux ↔ Android                                                                    | Fora do MVP (dados independentes por dispositivo); próxima etapa em `Sincronizacao_v1.md` |

## 15. Decisões registradas e próximos passos

**Decisões da versão 1.1–1.2 (mantidas):**

- Ambiente de desenvolvimento e alvo Linux: CachyOS (base Arch) com GNOME.
- Runtime: .NET 10 LTS.
- Plataformas iniciais: Linux e Android; Windows e macOS fora do escopo.
- Ao fechar a janela: ocultar em segundo plano por padrão, com opção de encerrar (desktop).
- `amount` soma ao progresso (padrão 1); desde a v1.4 tem sinal (ver 5.2).
- Semanas parciais não proporcionalizam o alvo; meta arquivada automaticamente ao passar o fim.

**Decisões da versão 1.3 (novas):**

- Recorrência `once` (tarefa única; removida na Fase 2, ver abaixo), `daily` (alvo por dia + dias ativos) e `weekly` (alvo por semana); `target_per_week` vira `target`.
- Sequências na unidade de cada tipo: dias (diária), semanas (semanal).
- Período de avaliação centralizado em `GoalPeriod` no Core.
- Semana fixa de segunda a domingo; a configuração "início da semana" foi removida.
- Configuração "Tema" removida do MVP (só existe tema escuro).
- Intervalo de lembrete contado a partir do último disparo ou check-in, dentro da janela ativa.
- Referência visual: v4 (desktop) e v3 (mobile), apenas com dados existentes no MVP; ícone e cor por meta entram no modelo.
- Fonte única Figtree (os protótipos usam Figtree no mobile e JetBrains Mono no desktop).
- Navegação padronizada: Hoje, Metas, Relatórios, Configurações.
- `specs/` versionada no repositório git junto com o código.
- Forma de trabalho: o assistente implementa e explica; o autor revisa e aprova por fase.

**Decisões da Fase 2 (01/11/2026):**

- O tipo de recorrência pode ser alterado a qualquer momento; relatórios passados passam a ser calculados com o tipo atual.
- Meta arquivada é somente leitura: só pode ser desarquivada ou excluída (a exclusão fica no próprio cartão da lista de arquivadas).
- Desarquivar é permitido, exceto para meta cujo fim já passou (seria arquivada de novo; cria-se outra meta). Nesse caso o botão "Desarquivar" não aparece e o cartão mostra "período encerrado".
- Data de fim no passado é recusada ao criar ou editar.
- O arquivamento automático por fim do período roda ao abrir a tela Metas; a virada de dia em tempo real fica para a Fase 4.
- Ícones: subconjunto fixo de 16 ícones de traço do Lucide (licenças ISC e MIT), embutidos como geometria; chave desconhecida no banco cai no ícone padrão `target`.
- Formulário de meta como página dentro da tela Metas (mesma solução no desktop e no celular).
- **Tarefa única (`once`) removida** a pedido do autor: simplifica domínio, telas, lembretes e relatórios. A migração `002` recria `goals` sem `'once'` e sem `completed_at` e **exclui** as tarefas únicas existentes (com check-ins e lembretes). As migrações rodam com chaves estrangeiras desligadas e conferidas por `PRAGMA foreign_key_check` antes do `COMMIT`.
- Linux: o app define `AVALONIA_IM_MODULE=none` (se não definido) para usar XIM; o IME via DBus do Avalonia perdia a composição de teclas mortas (`ç` no layout US internacional).

**Decisões da versão 1.4 (01/11/2026):**

- Medida por meta, independente da recorrência: conclusão (feito/não feito no período), quantidade (inteiro + unidade livre) e tempo (minutos). Ver 5.2.
- Registro por ajuste com sinal (+/−); o total do período nunca fica negativo. Sem edição nem "desfazer" de registros individuais no MVP (substitui o "desfazer o último" da v1.3).
- Cronômetro para metas de tempo, gravado no banco, um por vez; lançamento manual também disponível.
- Ação principal da notificação conforme a medida (Feito, +1, Iniciar); cronômetro rodando pausa os lembretes da meta.
- Medida travada após o primeiro registro.
- Sequência no resumo do Hoje fica para a Fase 5, junto com os relatórios (entregue na Fase 5).

**Decisões da Fase 4 (02/11/2026):**

- Adiar = 10 min fixos (`ReminderService.SnoozeMinutes`); a configuração veio na Fase 6.
- Lembrete atrasado dispara uma vez na hora, e o intervalo recomeça desse disparo.
- Notificação sem resposta vira "ignorada" e é retirada da central: no próximo disparo da meta, quando a meta não precisa mais de lembrete, quando a meta é excluída (desde 06/11/2026 a exclusão retira a notificação e apaga o log junto), ao encerrar o app ou ao abrir com pendentes de execução anterior.
- Cronômetro parado aguardando registro também suspende os lembretes da meta.
- Sem fallback em janela: se o D-Bus falhar, o app segue sem notificações, registra em `Trace` e tenta conectar de novo só no próximo lembrete (uma vez por disparo), sem laço (fallback na Fase 6, se preciso; não foi feito na Fase 6).
- Fechar a janela continua encerrando o app até a 4d; o critério "janela fechada" da 4c passou para a 4d.
- Virada do dia: o agendador acorda à meia-noite local e avisa o `DataChangeNotifier`; a tela Hoje recarrega (resolve a pendência da Fase 3).
- `IReminderScheduler`/`INotificationService` ficaram assíncronos (`Task`); `Pause`/`Resume`: ver Decisões da Fase 4d (ficaram no Core, em `ReminderPause`).
- Pendente de meta excluída ou inexistente é tratado como ignorado e retirado da central. Desde 06/11/2026 a exclusão apaga o log: quem retira é a própria exclusão, e uma ação numa notificação que tenha sobrado encontra o log vazio e não faz nada (ver Exclusão definitiva, no fim desta seção).
- **Iniciar** numa meta de tempo com cronômetro de outra meta rodando abre o app e não altera o cronômetro; cronômetro órfão (meta arquivada/inexistente) não conta como conflito.
- O tratador de respostas é serializado, para que ações simultâneas no mesmo lembrete não registrem duas vezes.
- O agendador avisa `Changed` depois de todo disparo, para "Próximos lembretes" não ficar desatualizado.

**Decisões da Fase 4d (02/11/2026):**

- Fechar a janela sempre oculta nesta fase; para encerrar: "Sair" no rodapé da barra lateral, `Ctrl+Q` ou bandeja. "Encerrar ao fechar" veio com as Configurações (Fase 6).
- Instância única por banco: `slimegoals-<hash>.lock` e `.sock` (8 primeiros hex do SHA-256 do caminho do banco) em `$XDG_RUNTIME_DIR` (ou `Path.GetTempPath()/slimegoals-<usuário>/`); a segunda execução envia `show`, recebe `ok` e sai com código 0; socket órfão é apagado e recriado.
- Pausa dos lembretes gravada em `settings` (`reminders.paused`); pausado, nada dispara e as pendentes saem da central como ignoradas; a tela Hoje mostra "Lembretes pausados" com **Retomar** e oculta "Próximos lembretes".
- Ao retomar, `reminders.resumed_at` (UTC) reinicia o intervalo de cada meta naquele dia, sem rajada de atrasados.
- Bandeja opcional (Abrir, Pausar/Retomar lembretes, Sair; clique no ícone abre); no GNOME exige a extensão AppIndicator e, sem ela, nada quebra; ícone provisório (Lucide "target", `#9B7BEA`) até a Fase 6, que trouxe o ícone do slime.
- A segunda instância traz a janela pelo mesmo `MainWindowActivator` da notificação; no Wayland sem token de ativação o GNOME pode mostrar "SlimeGoals está pronto" (aceito, conferido à mão).
- Desvio da §5: `IReminderScheduler` não ganhou `Pause`/`Resume`; a pausa fica no Core (`ReminderPause`) e chega ao agendador pelo `DataChangeNotifier` (mesma regra no Android, Fase 7b).
- `ResumeAsync` grava `resumed_at` antes de remover `paused`, para um travamento entre as duas gravações não causar rajada.
- O botão Pausar fica no cabeçalho "Metas de hoje", sempre acessível (inclusive sem próximos lembretes e sem bandeja).
- Se a aquisição da instância única lançar exceção, o app abre sem ela (registro em `Trace`).
- Pedido de mostrar a janela antes de ela existir é ignorado (vale também para o "Abrir" da notificação).
- O `.lock` permanece no disco ao encerrar (apagar disputaria com outro processo); só o `.sock` é apagado.

**Decisões da Fase 5 (03/11/2026):**

- Período contado: entre criação e arquivamento (datas locais), dentro do início/fim da meta, nunca futuro; diária só em dias ativos, semanal se ao menos um dia conta.
- Período em andamento conta como pendente até ser cumprido: entra no %, mas não quebra a sequência (revisto em 03/11/2026: só contá-lo depois de cumprido deixava a semana em 100% com o dia de hoje zerado).
- % cumprido: diária = dias cumpridos ÷ contados; semanal na semana = soma ÷ alvo (até 100%); no mês = semanas cumpridas ÷ contadas; sem período, "—"; geral = média dos que têm valor, com a semanal da semana em andamento entrando pelo parcial (confirmado pelo autor em 03/11/2026).
- O % dos relatórios (dia cumprido ou não) difere do % do Hoje (fração parcial por dia).
- Mês = semanas cuja segunda-feira cai nele, inteiras, também para diárias; domingo 01/11/2026 pertence a outubro.
- Barras da semana contam as metas com progresso no dia (revisto em 03/11/2026: antes contavam lançamentos, e oito +1 de água pesavam mais que 40 min de leitura); barras do mês são a média dos % das metas por semana; clicar numa barra mostra as metas que a compõem.
- Lembretes: respondidos = Feito ou Adiar; Ignorar e expiração não contam.
- Sequências sobre todo o histórico até o fim do período mostrado ou hoje; mês passado "como estava"; dias inativos não quebram; arquivada para no arquivamento.
- Melhor dia (semanal): maior soma positiva; empate, o mais cedo.
- Tile "Sequência" do Hoje: maior sequência atual entre as ativas (semana = 7 dias; empate, meta mais antiga); sem sequência, "0 dias" sem título.
- Navegação: abre no período atual; Próximo desabilitado nele; Anterior sem limite; período sem metas mostra "Nenhuma meta neste período.".
- Desarquivar uma meta faz os dias em que ela ficou arquivada contarem como não cumpridos (o modelo guarda só o último arquivamento), o que reduz o % e pode quebrar a sequência; aceito no MVP.

**Decisões da Fase 6 (03/11/2026):**

1. Configurações em `settings`: `reminders.snooze_minutes` (5, 10, 15, 30 ou 60 min; padrão 10; ausente ou inválido vale 10) e `window.close_action` (`hide`, padrão, ou `quit`). Mudar vale na hora, sem reiniciar; o adiar novo vale para as próximas respostas e um adiar pendente segue a configuração atual (revisto durante a execução).
2. Iniciar com o sistema: a fonte da verdade é `~/.config/autostart/slimegoals.desktop` (ou `$XDG_CONFIG_HOME/autostart`), não a tabela `settings`; ligar grava o caminho do executável atual (`Environment.ProcessPath`) com `--background`, desligar apaga o arquivo, e ligar de novo reescreve um caminho antigo. Se a entrada aponta para outro executável (instalação movida), o interruptor aparece desligado até ligar de novo. O `Exec` é gravado com o escape completo do Desktop Entry (`"`, `` ` ``, `$`, `\` e `%`), aceito pelo GNOME/GIO.
3. `--background`: abre sem janela e segue com lembretes e bandeja; atalho, notificação ou bandeja mostram a janela pela instância única; uma segunda instância com `--background` não pede a janela: espera até 3 s pela trava (um logout/login rápido) e, se não a obtiver, sai. Sem o argumento, nada muda.
4. Ao fechar = "Encerrar o app": faz o mesmo que "Sair"; as notificações pendentes saem da central e os lembretes param até abrir de novo. A tela avisa.
5. Suspensão: ao acordar, o app avisa o `DataChangeNotifier`; o agendador recalcula e a tela Hoje recarrega; vencidos disparam uma vez cada; sem barramento ou logind, segue sem isso e registra em `Trace`.
6. Pacote: `.tar.gz` *self-contained* linux-x64 (~46 MB, sem *trimming*) com `install.sh` e `uninstall.sh` no usuário (`~/.local`, sem root); o `install.sh` recusa rodar com o app aberto (pede para sair e rodar de novo); desinstalar mantém os dados e a entrada de autostart; versão em `Directory.Build.props`, a partir de `0.6.0`. AppImage fora (sem `appimagetool`, sem ganho no uso pessoal); PKGBUILD fica como melhoria futura.
7. Ícone do app: o slime do design `specs/design/Slime — mascote do SlimeGoals.html`, pose parada, sem sombra nem confete; substitui também o ícone provisório da bandeja; o "target" do Lucide continua como ícone de meta.
8. Nome e ícone no GNOME: `.desktop` com `StartupWMClass=slimegoals` e `WM_CLASS` `slimegoals` no app (`X11PlatformOptions.WmClass`, via XWayland); cabeçalho das notificações e painel conferidos à mão depois da instalação.

**Pendências:** sincronização entre dispositivos (fora do MVP; especificada em `Sincronizacao_v1.md`). A versão mínima do Android ficou definida como Android 8.0 (API 26) na Fase 7c.

**Próximos passos imediatos:**

1.  Revisar e aprovar esta v1.3.
2.  Escrever o plano de implementação detalhado das Fases 0 e 1.
3.  Executar a Fase 0: solução, tema, navegação e os dois spikes.

**Decisões da Fase 7a (03/11/2026):**

1. `PlatformCapabilities(Reminders, Autostart, WindowClose, Quit)`, um registro por head: desktop tudo verdadeiro; Android da 7a tudo falso (a 7b liga `Reminders`); esconde "Próximos lembretes"/"Pausar lembretes", "Geral", "Janela" e "Sair" conforme o caso; a seção "Lembretes" (adiar) continua.
2. Layout adaptativo por largura, não por plataforma: abaixo de 720 px o `ShellView` usa `compact`; o mínimo de 900 px da janela do desktop continua.
3. Navegação inferior com os quatro itens e ícones do Lucide (sun, list-checks, chart-column, sliders-horizontal); o item ativo usa `Selected` e `PrimarySoft`.
4. Voltar: `ShellViewModel.GoBack()` fecha o editor, senão volta para Hoje, senão devolve falso (o sistema decide); no desktop ninguém o chama.
5. Dados no Android em `<FilesDir>/slimegoals.db` (armazenamento privado), com migrações ao abrir; desinstalar apaga os dados. Fora do backup do Android e da transferência entre aparelhos (`allowBackup=false` e `data_extraction_rules.xml`): trocar de celular não leva as metas.
6. Lembretes na 7a: `NoopNotificationService` só para satisfazer o `ReminderService`; sem `ReminderHost` nem agendador; a 7b usa o `AlarmManager`.
7. Identidade: `dev.alcm.slimegoals`, rótulo "SlimeGoals", API mínima 26, APK Debug, ícone do slime em `mipmap-*`, tema escuro com barras em `#0F0D15`, orientação livre sem recriar a Activity, `AdjustResize` para o teclado.
8. O head Android fica fora de `SlimeGoals.slnx` e entra em `SlimeGoals.Android.slnx` (Core, Infrastructure, App e Android).
9. `tools/SlimeGoals.Render`, fora das duas soluções: semeia banco temporário e grava PNGs de cada página em 412×915 (celular, com as capacidades do Android) e 1100×720 (desktop) para revisão dos layouts.
10. Teclado virtual: o espaço reservado sob o teclado vai na margem do conteúdo do `ShellView`, não no seu `Padding`, que o Avalonia usa para as áreas das barras do sistema (o Android 15+ força *edge-to-edge*). Pôr o valor no `Padding` apagava essas margens ao fechar o teclado: o topo ia para baixo do relógio e a navegação inferior entrava na barra de gestos (corrigido depois da Fase 8).

**Decisões da Fase 7b (03/11/2026):**

1. Agendador do Android no Core (`SystemAlarmReminderScheduler`, porta `ISystemAlarm`), testável sem o workload `android`; `AndroidSystemAlarm` só arma e cancela.
2. Um alarme só, no próximo lembrete; sem lembrete futuro (sem metas, todas cumpridas, pausado) o alarme é cancelado; sem alarme à meia-noite.
3. Falha ao sincronizar arma nova tentativa em 15 min e registra em `Trace`.
4. Pendentes não expiram ao abrir o app no Android; valem as outras regras da §9.6. Notificação deixada na gaveta depois da janela ou do dia segue lá até a próxima sincronização; uma ação sobre lembrete disparado antes de hoje é tratada como obsoleta (registrada como ignorada, nada é gravado).
5. Sem permissão de alarme exato, lembretes inexatos (`SetAndAllowWhileIdle`) e aviso no Hoje; reagenda exato quando a permissão volta; `USE_EXACT_ALARM` descartada.
6. Notificação: canal `lembretes` (importância alta), uma por disparo (tag = id do registro), ação principal, "Adiar" e "Ignorar"; corpo abre o app; arrastar conta como ignorar; ícone do slime em silhueta, destaque `#9B7BEA`.
7. "Iniciar" com outro cronômetro rodando: a notificação vira "Outro cronômetro está rodando. Toque para abrir o app." sem botões (restrição de *trampoline* do Android 12+); o registro segue pendente.
8. Permissões num aviso na tela Hoje (fluxo de primeira execução) e em Configurações, não numa tela de boas-vindas; "Permitir" pede notificações e depois alarmes exatos.
9. Receivers de `BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`, `TIME_SET`, `TIMEZONE_CHANGED` e `SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED` reagendam; vencidos com o aparelho desligado disparam uma vez, na hora.
10. Adaptadores no head `SlimeGoals.Android/Reminders`, sem projeto `Platform.Android`.
11. `PlatformCapabilities.Reminders` verdadeiro no Android; o aviso de Configurações vira "Nada para configurar por enquanto.".
12. `ReminderHost` também no Android, com `NoopWindowActivator`: inicia o agendador ao abrir o app (recupera alarmes após "Forçar parada"); o `ActionReceived` do `AndroidNotificationService` nunca dispara, as ações chegam pelo receiver.
13. Verificado no emulador (API 36): `adb shell appops set --uid dev.alcm.slimegoals SCHEDULE_EXACT_ALARM allow|deny` (a forma por pacote não tem efeito); `am kill` encerra como o sistema, `force-stop` cancela alarmes. Restrições de fabricantes ficam para a 7c.
9. "Próximos lembretes" fica no fim da rolagem do Hoje, depois das metas, e não fixo embaixo: fixo, cada meta com lembrete tirava uma linha da lista no celular (corrigido depois da Fase 8).

**Decisões da Fase 8 (03/11/2026):**

1. Quatro poses pelas metas de hoje, nesta ordem: sem metas → Dormindo; todas cumpridas → Comemorando (mesmo com lembretes pausados); lembretes pausados → Dormindo; nenhuma cumprida → Parado; senão → Feliz. As poses "Lembrete" e "Anotando" do design ficam de fora.
2. Balão: Dormindo "Zzz…"; Parado "Vamos começar?"; Feliz "Falta pouco!" (falta uma meta) ou "Continue assim!"; Comemorando "Tudo feito hoje!".
3. Cartão de destaque (`HeroBrush`) no lugar do tile "Progresso da semana": título, % e barra à esquerda; balão e slime à direita. Os 3 tiles ficam numa linha, no desktop e no celular.
4. A spec pedia poses estáticas; o autor escolheu a animação leve do design: Parado achata/estica (2,6 s) e pisca; Feliz dá três pulinhos e pisca; Dormindo respira e solta dois "z"; Comemorando dá três pulinhos e solta confete. Roda por até 60 s depois de entrar no Hoje, mudar de pose ou reativar a janela; para antes com a janela oculta ou inativa e com o app em segundo plano, e depois fica o quadro parado da pose. Sem opção para desligá-la (o Avalonia não expõe "reduzir movimento" de forma portátil).
5. As cores do desenho ficam nos recursos do `SlimeMascot` (são arte, não tema); os textos do cartão usam os tokens `HeroText`, `HeroMuted`, `HeroTrack`, `HeroBubble` e `HeroBubbleText`.
6. Acessibilidade: `AutomationProperties.Name` "Slime dormindo", "Slime parado", "Slime feliz" ou "Slime comemorando"; o balão é texto comum.
7. Avalonia 12.1.4 não tem animador para `RenderTransform` em `Style.Animations`: o controle anima as próprias propriedades `double` (escala, deslocamento do pulo), copiadas para transformações fixas. Todas as animações exigem a pseudo-classe `:live`, ligada pelo controle e desligada por um `DispatcherTimer` de disparo único (sem polling). Os pulinhos se repetem ao voltar para Hoje (a view é recriada) ou ao reativar a janela, mas não em recarga de dados com a mesma pose. Slime e balão só aparecem depois da primeira carga (`HasMascot`).
8. CPU medida: no emulador (renderização por software) ~16–20% enquanto anima e 0% depois dos 60 s ou com o app em segundo plano; no Linux ~5% enquanto anima e 0% depois dos 60 s (antes, ~5% sem fim). Conferir num aparelho real na 7c.

**Decisões da Fase 7c (04/11/2026):**

1. Versão mínima: Android 8.0 (API 26), a sugestão do spike e o que já estava no projeto; fecha a pendência.
2. Versão `0.7.0` em `Directory.Build.props`; o `versionCode` é calculado por `maior × 10000 + menor × 100 + patch` (0.7.0 dá 700), no csproj do Android, e `ApplicationDisplayVersion` segue `$(Version)`.
3. APK só para `android-arm64` e `android-x64` (o Samsung e o emulador). **Mudou em relação ao plano:** os `RuntimeIdentifiers` ficam no csproj do Android, sob a condição `Configuration=Release`, e não no `build.sh`, porque um `-p:RuntimeIdentifiers` global chegou às bibliotecas `net10.0` e quebrou o build. O Debug não muda.
4. Chave de assinatura fora do repositório, em `${SLIMEGOALS_KEYSTORE:-$HOME/.android-keystores/slimegoals.keystore}`, alias `slimegoals`, RSA 4096, validade de 10000 dias; a senha vem de `SLIMEGOALS_KEYSTORE_PASS`, a mesma para o arquivo e para a chave. `make-keystore.sh` não sobrescreve uma chave existente. Perder a chave impede atualizar o app instalado. O assistente nunca criou nem leu a chave real: os testes usaram chaves descartáveis.
5. Trimming do Release: ficou o padrão do .NET para Android. O *smoke test* (abrir, criar meta com lembrete, +1, alarme armado) não quebrou nada, então não houve `TrimmerRootAssembly` nem `PublishTrimmed=false`. O teste usou lembrete a cada 5 minutos, o mínimo que o app aceita (1 minuto é rejeitado).
6. Licenças embutidas no App como `EmbeddedResource` (`LogicalName` `SlimeGoals.Licenses.<arquivo>`), com fonte única em `src/SlimeGoals.App/Licenses/` (o `LICENSE.md` do projeto entra linkado). O catálogo lista todos os componentes em todas as plataformas, com a coluna "Onde". Não existe `MIT-dotnet-android.txt`: o texto do pacote do .NET para Android é idêntico ao do runtime, então essa entrada reaproveita `MIT-dotnet-runtime-and-Microsoft.Extensions.txt`.
7. "Sobre" em Configurações, em todas as plataformas: "SlimeGoals <versão>", a licença do projeto e o botão "Licenças", que abre a lista na própria página; tocar num item mostra o texto completo.
8. Voltar genérico: `PageViewModel.TryGoBack()`; `ShellViewModel.GoBack()` o chama antes de voltar para Hoje, então o Voltar do sistema vai de texto para lista, para Configurações e para Hoje.
9. Bateria: linha em Configurações, descrita na §9.9; fora do aviso do Hoje.
10. Samsung: os ajustes manuais (Bateria → "Sem restrições"; fora de "Apps em suspensão" e "Apps em suspensão profunda") ficam no README e na verificação manual.
11. Conferido pelo autor num celular real em 06/11/2026, com um APK assinado por chave descartável: instalação, permissão de notificação, lembrete e recuperação depois de religar o aparelho. Pendente: lembretes com a tela bloqueada por 1 h ou mais, teclado e consumo de bateria do slime.

**Exclusão definitiva (06/11/2026):**

1. "Excluir" apaga a meta e tudo ligado a ela numa transação (`IGoalRepository.DeleteAsync`), a pedido do autor, sabendo que o histórico some dos relatórios. Regra na seção 8; a confirmação avisa: "Excluir esta meta? Os registros dela também serão apagados."
2. `PRAGMA secure_delete = ON` em toda conexão (`SqliteConnectionFactory`).
3. A migração `004` apaga as metas com `deleted_at` e tudo ligado a elas, além dos registros com `deleted_at` (o app nunca os gravou). As colunas ficam no esquema, para não recriar tabelas; remover as colunas e `Goal.DeletedAt`/`Checkin.DeletedAt` fica para quando uma migração já precisar recriá-las.
4. Depois da `004`, o `MigrationRunner` roda `VACUUM`, fora da transação e uma vez só: as cópias velhas de linhas e as páginas livres deixadas pelas versões anteriores (inclusive a recriação de `goals` na 002 e na 003) não são alcançadas pelo `secure_delete`. Se o `VACUUM` falhar, o app abre assim mesmo, registra em `Trace` e não tenta de novo.
5. Lembretes: `ReminderService.DeleteGoalAsync` lê os pendentes da meta, apaga e só então retira as notificações. Só o log sabe quais são, e, se o banco recusar, a meta fica com o lembrete. Ele usa a mesma trava do `SyncAsync`, para a exclusão não cruzar um disparo que já gravou o log e ainda não mostrou a notificação.
6. Sobrou uma notificação (processo morto depois do `COMMIT`, falha ao retirar): qualquer ação dá `Stale`; no Android o receiver a retira, e no Linux o próprio servidor a fecha ao acionar. A meta sumir no meio de uma ação vira `GoalNotFoundException` nos repositórios (violação de chave estrangeira em `checkins` e `active_timer`), que o tratador já entende.
7. O alcance é o arquivo do banco: cópias feitas pelo usuário, *snapshots* do sistema de arquivos e o que o disco retém do diário de transações apagado ficam fora.

**Decisões do banco corrompido (06/11/2026):**

1. Banco que não abre ou não migra (inclusive mais novo que o app) mostra uma tela de erro com o caminho do arquivo e "Fechar", nos dois heads; no Linux, mesmo com `--background`. O app só avisa: nunca apaga, sobrescreve nem conserta o arquivo. Não há `integrity_check` na abertura: dano que não atinge o cabeçalho aparece nas leituras, como erro das telas.
2. No Android, o `OnCreate` não relança: `Services` fica nulo, a `MainActivity` mostra a tela de erro, e os receivers terminam sem fazer nada. Sem receiver, os alarmes não são rearmados até o banco abrir de novo.
3. Listas pulam a linha inválida e a registram no Trace (`RowReader`, que converte linha a linha, porque o Dapper falharia a lista inteira por tipo errado numa coluna); leitura por id continua lançando `FormatException`. Quem chega à meta por referência usa `GetIfReadableAsync` e a trata como inexistente: lembrete pendente dela vira "ignorado", e o cronômetro dela vira órfão no Hoje. Iniciar outro cronômetro com o dela rodando continua falhando, para não descartar o registro em andamento.
4. Pular linha não quebra os relatórios: as médias já tratam lista vazia, e os registros de uma meta pulada ficam de fora porque tudo é somado por meta. Os números ficam sem a contribuição da linha: uma meta pulada sai da média, e um registro pulado reduz a soma (ou a aumenta, se for um ajuste negativo). `SumAsync`, em SQL, ainda conta a linha cujo `amount` é válido, então pode divergir do Hoje e dos relatórios nesse caso.
