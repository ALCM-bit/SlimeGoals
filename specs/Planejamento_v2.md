# Gestor de Metas Semanais — Especificação e Plano de Implementação (Avalonia + C#)

> Versão 1.2 · Alvos iniciais: Linux (desktop) e Android · .NET 10 LTS · Dark mode roxo suave

## 1. Visão geral

Aplicação multiplataforma para cadastrar metas, receber lembretes periódicos até cumpri-las, registrar o progresso e gerar relatórios semanais e mensais sob demanda. Interface convencional com telas, tema escuro roxo/preto suave. A ideia do mascote (slime) fica como melhoria futura.

**Dentro do escopo do MVP:** Linux (desktop, primeiro) e Android (em seguida), dados locais, lembretes, relatórios.

**Fora do escopo do MVP:** Windows e macOS, sincronização entre dispositivos, contas de usuário, mascote animado.

## 2. Requisitos funcionais

| ID   | Requisito                                                                                                                                                                                                    |
|------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| RF01 | CRUD de metas: título, alvo por semana, intervalo de lembrete, horário e dias ativos, início e fim opcionais                                                                                                 |
| RF02 | Tela "Hoje": metas da semana com progresso e botão de check-in rápido                                                                                                                                        |
| RF03 | Registrar check-in (quantidade e nota opcional) e desfazer o último                                                                                                                                          |
| RF04 | Lembretes periódicos por meta, respeitando horário e dias ativos; ações **Feito**, **Adiar**, **Ignorar**                                                                                                    |
| RF05 | Meta cumprida na semana não gera mais lembretes                                                                                                                                                              |
| RF06 | Relatório semanal e mensal por solicitação (progresso por meta, % cumprido, sequências)                                                                                                                      |
| RF07 | Ícone na bandeja: abrir, pausar lembretes, sair                                                                                                                                                              |
| RF08 | Configurações: tema, iniciar com o sistema, duração padrão do "adiar", início da semana, ao fechar a janela (ocultar na bandeja, padrão, ou encerrar; só desktop)                                            |
| RF09 | Arquivar meta sem perder histórico                                                                                                                                                                           |
| RF10 | Período e recorrência: a meta pode ter início e fim; recorrência **nenhuma** (única) ou **semanal**. Fora do período, não aparece em "Hoje" nem gera lembretes; ao passar o fim, é arquivada automaticamente |
| RF11 | No Android, lembretes agendados pelo sistema (`AlarmManager`), com as ações funcionando mesmo com o app fechado                                                                                              |

## 3. Requisitos não funcionais

- **Offline e local:** SQLite, sem servidor.
- **Separação de camadas:** nenhuma regra de negócio na UI, para compartilhar Core e UI entre Linux e Android.
- **Testável:** Core sem dependência de Avalonia; relógio injetável (`TimeProvider`).
- **Resposta independente da UI:** tratar a ação de um lembrete (Feito/Adiar/Ignorar) não pode exigir janela nem ViewModel; no Android o processo pode estar morto.
- **Desempenho:** abertura < 2 s; uso de CPU ocioso próximo de zero (sem polling).

## 4. Stack

| Camada        | Tecnologia                                                                                                                                                  |
|---------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Runtime       | .NET 10 (LTS), suportado até nov/2028                                                                                                                       |
| UI            | Avalonia 11 (XAML) com MVVM                                                                                                                                 |
| MVVM          | CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)                                                                                            |
| DI            | Microsoft.Extensions.DependencyInjection                                                                                                                    |
| Banco         | SQLite com Microsoft.Data.Sqlite + Dapper (leve; EF Core é alternativa)                                                                                     |
| Migrações     | Scripts SQL numerados + `PRAGMA user_version`                                                                                                               |
| Gráficos      | Barras desenhadas em XAML no MVP (o design usa barras simples); LiveCharts2 só se surgirem gráficos que o justifiquem                                       |
| Testes        | xUnit + FluentAssertions                                                                                                                                    |
| Empacotamento | `dotnet publish` por RID (linux-x64); AppImage ou binário *self-contained* com arquivo `.desktop` no Linux (PKGBUILD opcional); APK/AAB assinado no Android |

**Justificativa das escolhas:**

- **C# / .NET 10:** um único runtime para Core, dados e UI; domínio já conhecido pelo autor; o .NET 8 sai de suporte em 10/11/2026.
- **Avalonia:** único framework .NET de UI com Linux e Android no mesmo código; Electron foi descartado pelo consumo de memória de um app residente. O suporte mobile é menos maduro, por isso há spike na Fase 0.
- **SQLite + Dapper:** arquivo único, offline, SQL explícito e fácil de trocar atrás dos repositórios; EF Core traria cerimônia desnecessária para 4 tabelas.
- **CommunityToolkit.Mvvm e DI da Microsoft:** padrões do ecossistema, com source generators e pouca cerimônia.
- **Sem biblioteca de gráficos no início:** menos dependência e menos risco de incompatibilidade com o Avalonia.

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
public interface ICheckinRepository { /* add, undo, query por período */ }
public interface IReminderScheduler { void Reschedule(); void Pause(); void Resume(); }
public interface INotificationService
{
    void Show(Reminder reminder);                                  // não bloqueia
    event EventHandler<ReminderActionEventArgs>? ActionReceived;  // Feito | Adiar | Ignorar (Linux)
}
public interface IAutostartService { bool IsEnabled { get; } void Set(bool enabled); }
public interface IReportService { Task<Report> BuildAsync(ReportPeriod period, DateOnly reference); }
```

**Componente do Core obrigatório:** `ReminderResponseHandler` recebe a ação (Feito → check-in; Adiar → +N min; Ignorar → próximo intervalo) e pede reagendamento. Ele usa só repositórios, sem UI. No Linux é chamado pelo evento `ActionReceived`; no Android, diretamente pelo `BroadcastReceiver`, inclusive com o app fechado.

## 6. Estrutura da solução

    SlimeGoals/
    ├── src/
    │   ├── SlimeGoals.Core/                 # net10.0
    │   │   ├── Domain/            # Goal, Checkin, ReminderLog, Report
    │   │   ├── Services/          # GoalService, CheckinService, ReportService, ReminderPlanner, ReminderResponseHandler
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

| Tela              | Conteúdo                                                                                            |
|-------------------|-----------------------------------------------------------------------------------------------------|
| **Hoje**          | Resumo da semana, cartões de metas com barra de progresso e botão "Feito"                           |
| **Metas**         | Lista, criar, editar, arquivar                                                                      |
| **Editar meta**   | Título, alvo semanal, intervalo, horário ativo, dias da semana, início e fim opcionais, recorrência |
| **Relatórios**    | Alternar semanal/mensal, navegar entre períodos, gráfico + tabela                                   |
| **Configurações** | Tema, autostart, "adiar", início da semana, ao fechar a janela (desktop)                            |
| **Lembrete**      | Notificação nativa do SO com as três ações (janela própria do Avalonia só como fallback)            |

Navegação: barra lateral à esquerda no desktop (padrão do design anexo) e navegação inferior no Android (Hoje, Metas, Relatórios, Configurações). Os ViewModels são compartilhados; só o shell de navegação muda. O breakpoint de 720 px do protótipo é o ponto de partida do layout adaptativo.

## 8. Modelo de dados (SQLite)

```sql
CREATE TABLE goals (
  id TEXT PRIMARY KEY,                -- GUID
  title TEXT NOT NULL,
  target_per_week INTEGER NOT NULL,
  reminder_every_min INTEGER NOT NULL,
  active_from TEXT NOT NULL,          -- 'HH:MM'
  active_until TEXT NOT NULL,
  active_days TEXT NOT NULL,          -- '1,2,3,4,5' (1 = segunda)
  start_date TEXT,                    -- 'YYYY-MM-DD' (data local), opcional
  end_date TEXT,                      -- 'YYYY-MM-DD' (data local), opcional
  recurrence TEXT NOT NULL DEFAULT 'weekly', -- 'none' | 'weekly'
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  archived_at TEXT,
  deleted_at TEXT                     -- exclusão lógica
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
  response TEXT                       -- 'done' | 'snooze' | 'ignore' | NULL
);

CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE INDEX idx_checkins_goal_date ON checkins(goal_id, done_at);
```

Guarde **instantes** (`done_at`, `fired_at`, `created_at`…) em UTC e converta para o fuso local na camada de apresentação e no cálculo de semana. `start_date` e `end_date` são **datas de calendário locais**, não instantes: gravá-las em UTC deslocaria o dia em alguns fusos. Meta `none` vale para o período inteiro; sem `end_date`, fica aberta até ser cumprida.

**Persistência e usuários (decisão do MVP):** sem login e sem sincronização. Os dados ficam em um arquivo SQLite na pasta do usuário do sistema operacional, que já isola um usuário do outro. Para não precisar refatorar se a sincronização vier depois, o modelo já usa GUIDs, `updated_at` e exclusão lógica (`deleted_at`); todas as consultas e relatórios devem ignorar registros com `deleted_at` preenchido. A decisão sobre login e sincronização (pasta sincronizada, backend pronto ou API própria) fica para a fase 7.

No Linux, o arquivo fica em `$XDG_DATA_HOME` (padrão `~/.local/share/SlimeGoals`); no Android, no armazenamento privado do app. As consultas serão refinadas durante o desenvolvimento.

## 9. Lembretes

1.  `ReminderPlanner` (Core, puro e testável) calcula o próximo disparo de cada meta: próximo instante dentro da janela ativa, respeitando dias, período (`start_date`/`end_date`), intervalo e adiamentos. O estado do adiamento é reconstruído a partir do `reminder_log` (último disparo + resposta), para sobreviver a reinício.
2.  O scheduler mantém **um único disparo** apontado para o mais próximo e reagenda ao criar/editar meta, registrar check-in ou retomar do repouso. **Linux:** timer do processo. **Android:** alarme exato do sistema (`AlarmManager`), reagendado por um receiver de boot.
3.  Ao disparar: grava em `reminder_log` e chama `INotificationService.Show`. A resposta chega depois e é tratada pelo `ReminderResponseHandler` (Feito → check-in; Adiar → +N min; Ignorar → próximo intervalo). Notificações não bloqueiam o código, por isso o modelo é baseado em evento (Linux) ou receiver (Android).
4.  Notificação sem resposta conta como ignorada ao expirar, e o planejador segue o intervalo normal. A expiração é controlada pelo próprio app (prazo próprio), porque o GNOME tende a apenas esconder o banner e manter a notificação na central; validar no spike.
5.  **Linux (GNOME):** o app continua em segundo plano com a janela fechada; fechar oculta por padrão (configurável). O GNOME não exibe ícone de bandeja por padrão, então a bandeja é opcional (extensão AppIndicator). Sem ela, o app é reaberto pelo atalho do aplicativo (instância única) ou pela notificação. No Wayland, o compositor pode limitar trazer a janela para a frente; validar no spike e, se necessário, avaliar o portal de segundo plano (`xdg-desktop-portal` Background).
6.  **Android:** o ciclo de vida é do sistema; exige permissão de notificações (Android 13+), permissão de alarme exato (Android 12+) e tratamento do modo Doze, a validar no spike.
7.  Meta com alvo semanal atingido é excluída do planejamento até a próxima semana. Meta fora do período, ou arquivada, não gera lembretes.
8.  Tratar suspensão do computador (Linux: recalcular ao retomar, via sinal do systemd-logind).

## 10. Relatórios

Calculados por query, sem tabela própria.

- **Semanal (seg–dom):** por meta, check-ins vs. alvo, % cumprido, melhor dia, lembretes disparados vs. respondidos.
- **Mensal:** semanas cumpridas por meta, evolução semana a semana, sequência atual e maior sequência (em semanas consecutivas cumpridas). Só contam as semanas em que a meta estava ativa.

A query abaixo é **ilustrativa** e será revisada na Fase 5. Pontos obrigatórios: ignorar registros com `deleted_at`; incluir metas arquivadas quando estavam ativas no período (RF09); calcular `@start`/`@end` como a semana local convertida para UTC.

```sql
SELECT g.id, g.title, g.target_per_week, COALESCE(SUM(c.amount), 0) AS done
FROM goals g
LEFT JOIN checkins c ON c.goal_id = g.id AND c.done_at >= @start AND c.done_at < @end
WHERE g.archived_at IS NULL
GROUP BY g.id;
```

## 11. Design system (tokens)

| Token          | Valor     | Uso                      |
|----------------|-----------|--------------------------|
| `bg`           | `#0F0D15` | Fundo da janela          |
| `surface`      | `#17141F` | Barra lateral, cartões   |
| `surface-2`    | `#1F1B2B` | Hover, campos            |
| `border`       | `#2A2538` | Divisórias               |
| `text`         | `#E8E4F0` | Texto principal          |
| `muted`        | `#8C86A0` | Texto secundário         |
| `primary`      | `#9B7BEA` | Ações, progresso         |
| `primary-soft` | `#B9A3F0` | Destaques, ícones ativos |
| `success`      | `#7FD1AE` | Meta cumprida            |
| `warning`      | `#E8B86D` | Atenção                  |

Cantos arredondados de 12–14 px, sem sombras fortes, espaçamento em múltiplos de 4. Implementar como `ResourceDictionary` em `Theme.axaml` e usar apenas via `DynamicResource`. O protótipo visual acompanha esta especificação.

## 12. Plano de implementação

Linux primeiro, Android em seguida. Windows e macOS estão fora do escopo.

| Fase   | Entrega                  | Tarefas principais                                                                                                                                                                                                                                                                                                   | Critério de pronto                                                                               |
|--------|--------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------|
| **0**  | Fundação + spikes        | Solução em `net10.0`, projetos, DI, `Theme.axaml`, navegação lateral. Spikes descartáveis: (a) notificação com ações no GNOME via DBus, com arquivo `.desktop` e dica `desktop-entry`, testada com a janela oculta e com Não Perturbe ativo; (b) *hello world* Avalonia no Android com alarme + notificação com ação | App abre com o tema; notificação com ação responde no Linux; build Android roda e dispara alarme |
| **1**  | Metas (domínio + dados)  | Entidades, `001_init.sql` (com `start_date`, `end_date`, `recurrence`), repositórios, `GoalService`, cálculo de semana (UTC↔local, início configurável), testes                                                                                                                                                      | CRUD passa nos testes e persiste após reiniciar                                                  |
| **2**  | Telas de metas           | `GoalsView`, `GoalEditView` (período e recorrência), validação, arquivamento automático ao fim                                                                                                                                                                                                                       | Criar, editar e arquivar pela interface                                                          |
| **3**  | Check-ins e tela Hoje    | `CheckinService` (`amount` soma ao progresso), `TodayView`, desfazer                                                                                                                                                                                                                                                 | Check-in atualiza o progresso na hora                                                            |
| **4a** | Planejador               | `ReminderPlanner`: regras escritas antes (janela, dias, período, intervalo, adiar, ignorar, meta cumprida) e testes com `FakeTimeProvider`                                                                                                                                                                           | Testes cobrem virada de dia e de semana                                                          |
| **4b** | Agendador + resposta     | Scheduler Linux (timer único), `ReminderResponseHandler`                                                                                                                                                                                                                                                             | Disparo no horário e 3 ações funcionando                                                         |
| **4c** | Notificação nativa Linux | Adaptador DBus + fallback em janela; expiração = ignorado                                                                                                                                                                                                                                                            | Funciona com a janela principal fechada                                                          |
| **4d** | Segundo plano            | Fechar = ocultar, instância única, bandeja opcional (extensão AppIndicator no GNOME)                                                                                                                                                                                                                                 | Reabrir o app não duplica o processo                                                             |
| **5**  | Relatórios               | `ReportService`, `ReportsView`, barras em XAML, query corrigida, sequência em semanas                                                                                                                                                                                                                                | Números conferem com os check-ins (testes), inclusive meta arquivada                             |
| **6**  | Linux: acabamento        | Configurações (incluindo ao fechar), autostart XDG, suspensão, ícones, arquivo `.desktop`, AppImage ou *self-contained*                                                                                                                                                                                              | Instalável e estável no GNOME (CachyOS)                                                          |
| **7a** | Android: UI              | Head Android, shell com navegação inferior, layouts adaptados                                                                                                                                                                                                                                                        | Todas as telas usáveis no celular                                                                |
| **7b** | Android: lembretes       | `AlarmManager`, notificação com ações, permissões, receiver de boot, handler sem UI                                                                                                                                                                                                                                  | Lembrete dispara e as 3 ações funcionam com o app fechado, inclusive após reiniciar o aparelho   |
| **7c** | Android: distribuição    | APK/AAB assinado, versão mínima do Android definida                                                                                                                                                                                                                                                                  | Instalável em um aparelho real                                                                   |
| **8**  | Mascote (opcional)       | 3–4 poses estáticas na tela Hoje (slime já aprovado)                                                                                                                                                                                                                                                                 | Imagem muda conforme o progresso                                                                 |

**Ordem de trabalho em cada fase:** Core + testes, Infrastructure/Platform, ViewModel, View.

## 13. Estratégia de testes

- **Core (unitários):** `ReminderPlanner`, cálculo de semana, `ReportService`, regras de progresso. Use `FakeTimeProvider`.
- **Infrastructure (integração):** SQLite em memória (`Data Source=:memory:`) para repositórios e migrações.
- **UI:** testes de ViewModel (sem renderizar); validação visual manual.

## 14. Riscos e decisões em aberto

| Risco / decisão                                                                                          | Mitigação                                                                                                 |
|----------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------|
| Botões de ação em notificações no Linux dependem do servidor de notificação da distro                    | Spike na Fase 0 no ambiente alvo; fallback em janela própria                                              |
| GNOME não exibe ícone de bandeja por padrão                                                              | Bandeja opcional (extensão AppIndicator); instância única e notificação reabrem o app                     |
| Wayland (padrão do GNOME) pode impedir trazer a janela para a frente; o Avalonia roda via XWayland       | Testar no spike; alternativa: portal de segundo plano ou apenas notificar                                 |
| Modo Não Perturbe do GNOME silencia os banners                                                           | Documentar; lembretes continuam na central de notificações; avaliar aviso na tela Hoje                    |
| Android: o sistema encerra o processo e restringe alarmes (Doze, alarme exato, permissão de notificação) | Alarme do sistema + receiver de boot; handler de resposta sem UI; validar no spike da Fase 0 e na Fase 7b |
| Suporte mobile do Avalonia menos maduro                                                                  | Spike Android já na Fase 0, antes de investir nas telas                                                   |
| Fuso horário, horário de verão e início da semana                                                        | Instantes em UTC, datas de calendário locais, conversão na borda, `week_start` configurável               |
| Regras de recorrência e semanas parciais                                                                 | MVP: só `none` e `weekly`; alvo não proporcionalizado; arquivamento automático ao fim                     |
| Dapper vs. EF Core                                                                                       | Dapper no MVP; trocar é barato por causa dos repositórios                                                 |
| Login e sincronização Linux ↔ Android                                                                    | Fora do MVP (dados independentes por dispositivo); modelo já preparado (GUID, `updated_at`, `deleted_at`) |

## 15. Decisões registradas e próximos passos

**Decisões da versão 1.1:**

- Ambiente de desenvolvimento e alvo Linux: CachyOS (base Arch) com GNOME.
- Runtime: versão LTS mais recente do .NET (.NET 10).
- Plataformas iniciais: Linux e Android; Windows e macOS fora do escopo.
- Metas com início e fim opcionais e recorrência (`none` ou `weekly`).
- Ao fechar a janela: ocultar na bandeja por padrão, com opção de encerrar (desktop).
- Sequência medida em semanas consecutivas cumpridas; `amount` soma ao progresso (padrão 1).
- Semanas parciais não proporcionalizam o alvo; meta arquivada automaticamente ao passar o fim.
- Queries de relatório serão refinadas durante o desenvolvimento.

**Pendências:** versão mínima do Android (Fase 7c); sincronização entre dispositivos (fora do MVP).

**Próximos passos imediatos:**

1.  Instalar o SDK .NET 10 (pacote da distro) e os templates; gerar a solução (`avalonia.xplat`).
2.  Criar `Core`, `Infrastructure` e `Platform.Linux`; mover a lógica para eles.
3.  Montar `Theme.axaml` com os tokens da seção 11.
4.  Fase 0: janela com navegação lateral, mais os dois spikes (Linux e Android).
