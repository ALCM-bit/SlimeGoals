# Tarefas no cronômetro (v1)

> Desenho curto, aprovado pelo autor em 15/11/2026. O plano está em `specs/plans/2026-11-15-tarefas-no-cronometro.md`. O Pomodoro (intervalos de foco e pausa) vem num desenho seguinte e reaproveita a tarefa como o foco de cada ciclo.

## 1. Para que serve

Nas palavras do autor, falta "a possibilidade de gerenciar intervalos ou quebrar em várias tarefas menores para o tempo ser alocado na principal". As tarefas **nascem na hora em que o trabalho começa**: não são uma lista cadastrada na meta.

Este desenho cobre as tarefas. Numa meta de tempo, a pessoa diz no que está trabalhando ("Revisar capítulo 3"), e o tempo de cada tarefa soma na meta, como hoje.

## 2. Objetivos, o que fica de fora e o que seria um fracasso

**Objetivos**
- Dar um nome opcional ao trabalho do cronômetro.
- Ver esse nome no Hoje, na janelinha do Linux e na notificação do Android.
- Trocar de tarefa sem parar o cronômetro: o trecho feito é registrado, e a contagem continua com o novo nome.
- Somar o tempo de toda tarefa na meta, sem regra nova de progresso.

**Fica de fora nesta versão**
- **Tela de histórico por tarefa** ("Inglês 2 h, Capítulo 3 50 min"). *Decisão minha:* o autor disse "ajustamos conforme o necessário". O nome fica gravado em cada registro, então a tela pode vir depois sem perder nada.
- Tarefas cadastradas na meta.
- Pomodoro e intervalos (próximo desenho).
- Trocar de tarefa pela janelinha ou pela notificação: só pelo Hoje.
- Sincronizar o cronômetro em andamento entre aparelhos (ele continua local).

**Seria um fracasso se**
- começar o cronômetro ficasse mais lento que hoje;
- o mesmo tempo fosse contado duas vezes, ou algum tempo se perdesse, ao trocar de tarefa.

## 3. Como construir

- **A (escolhida): o nome da tarefa é a nota do registro.** `Checkin.Note` já existe, já é sincronizada (`SyncCodec`), já tem limite de 200 caracteres (`CheckinService.MaxNoteLength`) e já é preenchível no painel de registro do Hoje. O cronômetro ganha um campo local com o nome. Custo: as notas dos registros manuais também viram sugestões de tarefa, o que deve ser útil.
- **B: um campo "tarefa" separado da nota.** Exige uma migração em `checkins` e uma mudança no formato da sincronização, e todos os aparelhos teriam de atualizar juntos. Recusada: custo alto para separar duas coisas que o autor usa do mesmo jeito.
- **C: tarefas cadastradas na meta.** Recusada: contraria "nasce na hora".

## 4. Como funciona

1. **Onde o nome fica.** `ActiveTimer` ganha `Task` (`string?`). Uma migração nova faz `ALTER TABLE active_timer ADD COLUMN task TEXT`. O cronômetro continua local, sem sincronização.
2. **Começar continua com um toque.** ▶ começa na hora, como hoje. O "Iniciar" do lembrete também começa sem nome.
   - *Decisão minha, diferente do que mostrei na conversa:* o campo não aparece antes de começar. Ele aparece no cartão do cronômetro rodando, porque um cronômetro iniciado pela notificação também precisa receber um nome depois, e assim o ▶ não ganha um passo a mais.
3. **O campo no cartão: "No que está trabalhando?"**
   - Fica vazio por padrão e mostra até 5 sugestões: as notas distintas mais recentes dos registros ativos daquela meta.
   - O nome é gravado ao confirmar (Enter), ao escolher uma sugestão ou ao sair do campo.
   - O nome passa por trim; vazio vira `null`. O limite é de 200 caracteres, como o da nota.
   - Core: `TimerService.SetTaskAsync(string? task)`, que vale para o cronômetro rodando ou parado.
4. **Parar.** O painel de conferência já vem com a nota preenchida com o nome da tarefa, e a pessoa pode editar. Registrar funciona como hoje.
5. **Trocar de tarefa (⇄), só com o cronômetro rodando.**
   - ⇄ abre o mesmo campo, vazio e com as sugestões, e um botão "Trocar".
   - Ao confirmar, o trecho até agora é registrado sem conferência, com a nota igual à tarefa atual. Na mesma operação, o cronômetro segue com o novo nome.
   - O trecho leva só os **minutos inteiros**, arredondados para baixo. O novo início é o início antigo mais esses minutos, e não o instante da troca: os segundos que sobram passam para a próxima tarefa.
   - Um trecho de menos de 1 minuto não gera registro; só o nome muda, e o início fica o mesmo.
   - Se o cronômetro foi parado ou descartado por outro processo no meio da troca, nada é gravado, e o Hoje mostra "O cronômetro mudou. Confira em Hoje.".
   - Core: `TimerService.SwitchTaskAsync(string? newTask)`, que devolve o novo total do período.
   - Requisito: nenhum instante é contado duas vezes nem perdido. O "Parar" continua arredondando ao minuto mais próximo, como hoje.
6. **Fora do app.** A janelinha e a notificação mostram o nome da tarefa junto ao da meta, cortado com reticências se for longo. Sem tarefa, mostram o que mostram hoje.
7. **Texto do Hoje.** Com o cronômetro rodando e uma tarefa definida, o cartão mostra o nome da tarefa abaixo do título da meta.

```
┌─────────────────────────────────────┐
│ 📖 Estudar           3 h 20 / 10 h  │
│    Revisar capítulo 3               │
│    0:23:51                  [⇄] [■] │
└─────────────────────────────────────┘
```

## 5. O que fica para quem constrói

- A disposição exata do campo e das sugestões no cartão, no desktop e no celular, conferida com `tools/SlimeGoals.Render` em várias larguras, com simetria visual.
- Como a janelinha e a notificação encaixam a segunda linha.
- A consulta das sugestões: no repositório de registros, filtrando por meta, `note` não nula e não excluídos, por `done_at` decrescente.
- Testes: unitários no `TimerService` (definir, limpar, trocar, trecho de 0 min, nada contado duas vezes), integração no SQLite (migração e sugestões) e ViewModels do Hoje.

## 6. Verificação (15/11/2026)

- **Testes automatizados:** `dotnet test --solution SlimeGoals.slnx`, `dotnet build SlimeGoals.slnx -c Release` e o build de `SlimeGoals.Android.slnx` passam sem warnings.
- **Desktop:** o Hoje real foi conferido sem janela, com o SQLite de verdade numa pasta temporária. Foram 12 de 12 verificações:
  - ▶ começa com o campo vazio, e as sugestões aparecem;
  - Enter, sair do campo e escolher uma sugestão gravam o nome;
  - ⇄ esconde ⇄ e ■;
  - trocar depois de 3 min registra 3 min com a tarefa antiga como nota, e o novo início é o antigo mais 3 min;
  - parar preenche a nota com a tarefa.
- **A conferir pelo autor no GNOME:** a janelinha real com "Meta · Tarefa". O Wayland não deixa automatizar a janela.
- **Android (emulador, API 36, conta de teste):**
  - ▶ começa na hora, e o campo vem vazio;
  - Enter grava, e a notificação mostra "Estudo teste · Ingles";
  - ⇄ com "Capitulo 3" depois de mais de 1 min registra 1 min e muda a notificação; os segundos passam para a tarefa nova;
  - tocar numa sugestão grava;
  - parar preenche a nota com a tarefa.
- **Achado no celular, corrigido:** com o teclado aberto, a lista do Hoje ficava sem altura, porque o título, o destaque e os blocos ocupavam a tela, e o campo em edição sumia atrás do teclado. Agora, com o teclado aberto, o destaque e os blocos somem, e a página rola até o campo. O mesmo valia para a nota do painel de registro.
- **Revisão final, corrigido:**
  - um "Parar" de outro processo no meio de uma troca podia contar o mesmo trecho duas vezes;
  - a troca de menos de 1 min renomeava um cronômetro parado nesse meio-tempo;
  - a troca com o cronômetro parado ou descartado por outro processo não mostrava "O cronômetro mudou. Confira em Hoje.";
  - tocar ■ com o campo em edição não parava o cronômetro;
  - digitar um texto que passa por uma sugestão gravava no meio da digitação.

  Conferido no emulador.
- **Não exercitado no aparelho:** o "Iniciar" do lembrete. O teste `Timer_started_from_a_reminder_has_an_empty_task_field` cobre esse caminho.

## 7. Revisão depois do teste do autor (15/11/2026)

O autor conferiu a janelinha ("ficou boa") e apontou dois problemas no Hoje:
- "não tem um confirmar ou algo ali que mostre que o que o usuário escreveu está sendo seguido";
- a pessoa "não consegue ver essas subatividades nem ajustá-las se precisar".

O autor escolheu ver e ajustar os trechos de hoje dentro do cartão (caminho A). O histórico da meta pelo ⋯ fica para depois.

### 7.1 O campo da tarefa ganha estados

- **Com nome gravado:** o nome aparece como texto ("▸ Capítulo 3"), com ✎ para editar e ⇄ para trocar. O texto mostrar o nome é a confirmação de que ele está valendo.
- **Sem nome, ou editando:** o campo "No que está trabalhando?" aparece com um botão ✓. O ✓, o Enter e escolher uma sugestão gravam; Esc ou ✕ cancelam a edição.
- *Decisão minha:* sair do campo **não grava mais**. O texto fica no campo como rascunho, com o ✓ à vista, até a pessoa confirmar ou cancelar. ■ e ⇄ continuam usando o texto digitado. Gravar ao sair do campo era o que tirava o botão de baixo do dedo no toque seguinte.

```
│ 🎯 Estudar        0:23:51   [■] [⋯] │
│ ▸ Capítulo 3                 [✎] [⇄]│   nome gravado

│ [ No que está trabalhando?   ] [✓]  │   sem nome ou editando
```

### 7.2 Trechos de hoje no cartão

- **Onde aparece:** só em metas de tempo, com pelo menos um registro no dia de hoje, rodando ou não. Abaixo do cartão vem uma linha recolhível, "Hoje · 37 min ▾", fechada por padrão.
- **Ao abrir:** os registros do dia, do mais recente para o mais antigo, com o nome (a nota, ou "Sem nome") e os minutos. O trecho em andamento entra no topo, só para ver, com o tempo correndo.
- **Ajustar:** o ✎ de um registro abre, no lugar dele, os minutos, o nome e os botões Apagar, Cancelar e Salvar.
  - **Salvar substitui o registro.** Numa transação só, o antigo é apagado e um novo é gravado com o mesmo horário (`done_at`), os novos minutos e o novo nome. A sincronização não altera registros, mas leva exclusões e registros novos, então isso chega aos outros aparelhos sem mudar o formato.
  - **Apagar** exclui o registro, na mesma transação da fila da sincronização.
  - **Validação:** os minutos vão de 1 a 24 h, o nome segue o limite de 200 caracteres, e o total do período não pode ficar negativo. As mensagens ficam no próprio editor.
- *Decisão minha:* os registros de compensação aparecem na lista, mas sem ✎. Eles só surgem depois de juntar aparelhos que estavam sem rede ou de mudar a recorrência da meta, e alterá-los mudaria a conta da junção.
- *Decisão minha:* os registros negativos (o "tirar" do painel) aparecem como "−5 min" e também podem ser apagados ou ajustados.

### 7.3 O que fica para quem constrói

- **Core:** `CheckinService.ReplaceAsync(id, minutes, note)` e `CheckinService.DeleteAsync(id)`, além de uma consulta dos registros do dia de uma meta.
- **Infra:** a exclusão de um registro com a fila da sincronização, com `op = 'delete'` se o servidor já conhece o registro; se não conhece, ele só sai da fila. A substituição roda numa transação só.
- **App:** os estados do campo, a lista recolhível e o editor do registro, conferidos com `tools/SlimeGoals.Render` em várias larguras e no emulador.
- **Testes:**
  - substituir e apagar, sem contar duas vezes;
  - total negativo recusado;
  - compensação sem edição;
  - a fila da sincronização: exclusão de um registro conhecido e de um que nunca foi enviado;
  - os ViewModels do campo e da lista.

