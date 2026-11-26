# SlimeGoals.Render

Ferramenta de desenvolvimento que renderiza as telas do app sem janela (Avalonia Headless + Skia) e grava PNGs para conferência visual. Fica fora da solução: `dotnet test --solution SlimeGoals.slnx` não a compila.

## Uso

```
dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all|mascot|narrow|time|account|mini|sync-warning|today-filter|wide]
```

Sem o segundo argumento, vale `all`. Grava um PNG por tela e tamanho, sempre o `ShellView` inteiro:

- `today-<tamanho>.png`, `goals-<tamanho>.png`, `goal-edit-<tamanho>.png`, `goal-edit-tags-<tamanho>.png` (o quadro de tags, rolado até ele, com um chip marcado e outro recém-criado), `goal-edit-end-<tamanho>.png` (o formulário rolado até o fim),
  `reports-week-<tamanho>.png`, `reports-month-<tamanho>.png`, `settings-<tamanho>.png` (a lista de grupos) e
  `settings-<grupo>-<tamanho>.png` para cada grupo que a plataforma tem (`reminders`, `tags`, `window`, `general`, `about`) e `settings-tags-edit-<tamanho>.png` (Configurações › Tags com uma linha em cada estado: cores, edição com erro, junção, exclusão e o campo de nova tag);
- `<tamanho>` é `phone` (412×915) ou `desktop` (1100×720).

O modo `today-filter` (também incluído no `all`) grava o Hoje com o filtro de tags a 320, 360, 412 e 1100 px, como `<cena>-<largura>.png`: `today-filter-idle` (a linha de chips sem seleção), `today-filter` (duas tags marcadas: "✕ Limpar" fixo e "x de y metas"), `today-filter-single` (uma tag que esconde cartões) e `today-filter-empty` (nenhuma meta de hoje com tag: a linha de filtros some). A mensagem "Nenhuma meta de hoje com essas tags." não tem captura: é uma defesa que hoje não se alcança, porque uma tag marcada sempre tem meta na lista (ao sair a última, ela deixa a seleção). O `all` grava só 412 e 1100 px nas demais telas; `narrow` acrescenta 320 e 360. Grava também o Hoje sem metas, numa amostra vazia, a 360 e 1100 px (`today-empty-<largura>.png`): o aviso fica logo abaixo do cabeçalho.

O modo `wide` grava o Hoje numa janela larga, a 1440×900 e 1920×1080 (`today-wide-<largura>.png`): as metas em linhas e o resumo, com "Esta semana", na coluna da direita (decisão 16). O `desktop` (1100×720) mostra a outra forma do desktop, com o destaque e os blocos numa faixa acima das linhas.

O modo `account` grava a seção "Sincronização" de Configurações sobre uma conta falsa (`SampleAccount`, em `Sample.cs`: nenhuma rede, códigos de mentira), a 320×1400, 360×1400, 900×800 e 1100×800, como `account-<tela>-<largura>.png`:

- estados da seção: `signed-out`, `signed-in-error` (com `last_error`), `session-expired`, `vault-locked`, `session-unreadable`, `insecure-session`, `untrusted-app`, `rooted`;
- Sincronizar agora: `sync-running` (ciclo preso, com "Sincronizando…"), `sync-done` e `sync-failed` (a linha e, abaixo, a faixa com o motivo);
- subpáginas: `register`, `recovery-code`, `confirm`, `login`, `login-busy` (operação presa, com a barra de ocupado), `login-error`, `recover`, `recovered-code`, `reauthenticate`, `change-password`, `devices`, `sign-out` (aviso com 3 pendências), `delete-account`.

O cadastro pendente da conta falsa usa o construtor interno de `PendingRegistration` (`InternalsVisibleTo` no `SlimeGoals.Sync.csproj`).

O modo `narrow` grava o editor de meta com a confirmação de exclusão a 320 e 360 px (`goal-edit-delete-<largura>.png`) e o Hoje com um cronômetro rodando a 320, 360 e 1100 px (`today-timer-<largura>.png`, com o nome gravado; `today-timer-edit-<largura>.png`, com o nome em edição; `today-timer-switch-<largura>.png`, trocando de tarefa; `today-chunks-<largura>.png`, com os trechos de hoje abertos; `today-chunk-edit-<largura>.png`, ajustando um trecho; `today-panel-<largura>.png`, o painel de lançar tempo; `today-timer-stopped-<largura>.png`, a conferência do cronômetro parado): é o cartão mais apertado, com o tempo, o botão de parar e o de mais opções.

O modo `time` (também incluído no `narrow`) grava a aba Tempo (Pomodoro) a 320×640, 360×760 e 1100×720, como `<cena>-<largura>.png`, numa sessão de exemplo (plano 40/20/60/30 min, 2 rodadas, meta Leitura) que o relógio de cena (`SceneClock`, em `Sample.cs`) avança:

- preparo: `time-prepare` (a sequência montada) e `time-block-edit` (o terceiro bloco aberto para editar);
- sessão: `time-running` (foco com a subatividade), `time-switch` (trocando de subatividade), `time-paused`, `time-waiting` (foco acabou, esperando o toque), `time-waiting-break` (pausa acabou), `time-review` (conferência depois de mais de 30 min parado) e `time-confirm-end`;
- fim: `time-end-linked` (com meta), `time-end-unassigned` (sem meta, com dois trechos), `time-discard` (confirmação de descartar) e `time-create-goal` (o editor de meta aberto pela aba Tempo);
- `today-focus-band`: o Hoje com a faixa da sessão terminada sem meta ("Pomodoro concluído"), a 915 px de altura no celular.

O modo `mini` grava a janelinha do cronômetro (a `MiniTimerWindow` de verdade, sobre um fundo cinza com 24 px de folga em volta do cartão para os cantos aparecerem), rodando há 23 min 51 s: `mini-timer.png` com a meta "Leitura", `mini-timer-long.png` com o título longo de exemplo, para ver o corte com reticências, e `mini-timer-hover.png` com o "×" de esconder à vista, como fica com o mouse sobre a janelinha. Compare com `specs/design/cronometro-janelinha-prototipo.png` (opção A).

O modo `mascot` grava só `mascot.png`: as quatro poses do slime (dormindo, parado, feliz, comemorando), lado a lado em 160×160 sobre o `HeroBrush`, com o nome acessível embaixo. O quadro é capturado cerca de 0,8 s depois de montar a tela, com as animações já em andamento.

## Tema

O app só tem o tema escuro (`RequestedThemeVariant="Dark"` em `App.axaml`, sem paleta clara), então as telas saem só nele.

## Dados

Um banco SQLite temporário (`slimegoals-render-<guid>.db` em `Path.GetTempPath()`), apagado ao final; nunca toca o banco do app.
Quatro metas de exemplo (Beber água, Leitura, Treino, Aula de inglês), com tags (as três padrão e mais algumas, uma delas, "trabalho", sem meta) e registros dos últimos 10 dias. O relógio é o do sistema, menos no Pomodoro (`FocusService` e a aba Tempo), que usa um relógio de cena parado (`SceneClock`) que só anda quando a cena manda.

## Observações

- A variação `phone` monta o shell com as capacidades do Android (`PlatformCapabilities(true, false, false, false)` e uma porta de permissões falsa com tudo negado, para o aviso aparecer): lembretes em Hoje, sem as seções Geral e Janela nas Configurações e sem "Sair"; a `desktop`, com `PlatformCapabilities.Desktop`.
- A `RenderApp` espelha os `DataTemplates` do `App.axaml`: ao criar uma página nova, registre-a nos dois lugares. Os modelos casam só com uma instância do tipo (como o `DataType` do XAML): um `FuncDataTemplate<T>` simples também casaria com nulo, e um ComboBox sem seleção desenharia uma página inteira dentro dele.
- O slime sai no quadro parado da pose (partes da pose visíveis, sem movimento): a janela headless não fica ativa, então a pseudo-classe `:live` não liga. É o esperado; a captura não precisa da animação.
