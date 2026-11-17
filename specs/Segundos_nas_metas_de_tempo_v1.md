# Segundos nas metas de tempo (v1)

> Desenho completo, em revisão pelo autor desde 17/11/2026. Caminho escolhido: 1, guardar segundos. Na conversa o autor escolheu ver os segundos "em todo lugar" (opção B), e não só na lista de trechos. Um revisor leu o desenho como quem vai construir e não deixou dúvida para o autor; as duas pequenas que levantou foram decididas aqui (versão de lançamento e o "+N" do Pomodoro).

Sai depois da 0.9.0 (Pomodoro), numa versão própria.

## 1. Para que serve

O autor testou o Pomodoro no celular e disse: "achei que uma parte que está me incomodando é as metas de tempo terem que ter pelo menos 1 minuto e não considerarem os segundos completos. Isso é ruim principalmente quando coloco uma subtarefa em um período quebrado."

Hoje as metas de tempo guardam minutos inteiros:
- **Troca de subtarefa (⇄), no cronômetro e no Pomodoro:** o trecho anterior leva só os minutos inteiros. Os segundos que sobram vão para a subtarefa seguinte. Abaixo de 1 minuto, nenhum trecho é gravado e só o nome muda. Os 40 s de uma subtarefa somem dentro da próxima.
- **Parar o cronômetro e último corte do Pomodoro:** o tempo é arredondado ao minuto mais próximo.

Este desenho faz as metas de tempo guardarem e mostrarem o tempo até o segundo.

## 2. Objetivos, o que fica de fora e o que seria um fracasso

**Objetivos**
- Todo trecho medido por relógio (cronômetro, troca de subtarefa, Pomodoro) é gravado com os segundos inteiros, sem arredondar ao minuto.
- Um trecho de qualquer duração a partir de 1 s é gravado e aparece com o nome da sua subtarefa.
- Os segundos aparecem em todo lugar onde o tempo de uma meta aparece: Hoje, lista de trechos, relatórios, fim do Pomodoro.
- Os dados que já existem são convertidos uma vez, sem perder nada.

**Fica de fora**
- **Lançar tempo à mão com segundos.** O painel de lançar tempo do Hoje, o alvo da meta e os blocos do Pomodoro continuam em horas e minutos.
- Segundos no plano do Pomodoro: blocos continuam em minutos.
- Frações de segundo: o tempo gravado é sempre um número inteiro de segundos.

**Seria um fracasso se**
- algum registro antigo mudasse de valor na conversão (60 min virando 60 s, ou 3600 min);
- um aparelho com a versão antiga lesse segundos como se fossem minutos;
- o tempo somado de uma sessão fosse diferente do tempo real (segundos contados duas vezes, ou perdidos);
- as telas a 320 px cortassem os textos mais longos ("1 h 12 min 40 s de 2 h 30 min hoje").

## 3. Como construir

- **1 (escolhida): guardar segundos.** Nas metas de tempo, `Checkin.Amount`, `Goal.Target` e o trecho guardado do Pomodoro passam a ser segundos. Uma migração multiplica por 60 o que existe. A conta continua sendo uma soma de inteiros, com uma unidade só.
- **2: minutos mais uma coluna de segundos (0 a 59).** Um aparelho antigo continuaria lendo os minutos certos. Recusada: toda soma vira "minutos + segundos ÷ 60", e esquecer o campo numa conta perde segundos sem ninguém perceber.
- **3: valor fracionário (2,67 min).** Recusada: erro de arredondamento, e mexe também nas metas de quantidade.

## 4. Como funciona

### 4.1 A unidade

1. **Metas de tempo contam em segundos.** `Measure.Duration` passa a significar "tempo em segundos" em `Checkin.Amount`, `Goal.Target`, `GoalProgress`, `GoalTotals`, relatórios, sequência de dias e compensação. As metas de conclusão e de quantidade não mudam.
2. **Limites em segundos.**
   - `CheckinService.MaxDurationAmount` vira 24 h em segundos (86 400).
   - O alvo continua entre 1 min e 24 h (diária) ou 168 h (semanal), agora em segundos (60 a 86 400 e 60 a 604 800). As mensagens não mudam.
   - O limite do trecho do Pomodoro (`FocusService`) usa o mesmo `MaxDurationAmount`.
3. **Onde se digita em horas e minutos, a conversão é na borda.** O painel de lançar tempo, o alvo da meta e os blocos do Pomodoro continuam em h e min. O App multiplica por 60 ao gravar; o Core só vê segundos.
   - O "Desfazer" ou o lançamento negativo do painel seguem a mesma conta.

### 4.2 O que gera trechos

1. **Cronômetro, Parar.** O registro leva os segundos inteiros entre o início e a parada, truncando a fração. `TimerService.Minutes` vira `Seconds`. Abaixo de 1 s, nada é registrado, como hoje com 0 min.
2. **Cronômetro, troca de subtarefa (⇄).** O trecho leva os segundos inteiros, e o novo início é o antigo mais esses segundos: a fração de segundo passa para a próxima subtarefa. Só abaixo de 1 s o nome muda sem trecho.
3. **Pomodoro.** `FocusClock.CutMinutes` vira um corte em segundos inteiros (truncando), e `Carry` guarda só a fração de segundo. No último corte, arredonda ao segundo mais próximo. A conferência do excesso (≥ 30 min) continua com o mesmo limiar.
   - `FocusChunk.Minutes` (o tempo guardado de uma sessão sem meta) vira segundos.
4. **Conferência do cronômetro parado e do Pomodoro, e ajuste de um trecho de hoje.** Estes três editores mostram um valor medido e já exato. *Decisão minha:* eles ganham um campo de segundos ao lado de h e min ("h · min · s"), para que abrir e salvar sem mexer não mude o valor e para que a pessoa possa corrigir até o segundo. Valem as mesmas quebras de linha a 320/360 px que já existem nesses editores.

### 4.3 Como o tempo aparece

1. **Formato único.** `GoalFormatting.Duration(int seconds)` mostra horas, minutos e segundos, omitindo as partes zeradas:
   - 0 → "0 min";
   - 40 → "40 s";
   - 160 → "2 min 40 s";
   - 3 600 → "1 h";
   - 4 360 → "1 h 12 min 40 s";
   - 3 605 → "1 h 5 s";
   - 9 000 → "2 h 30 min" (hoje é "2 h 30": *decisão minha*, o "min" volta porque agora há uma terceira parte depois dele);
   - negativo leva o sinal na frente, como hoje.
2. **Em todo lugar.** O formato vale para: alvo e progresso da meta ("1 h 12 min 40 s de 2 h hoje"), cabeçalho e lista de trechos ("Hoje · 1 h 12 min 40 s", "Revisar capítulo 3 · 2 min 40 s"), relatórios, faixa do Hoje do cronômetro parado, fim do Pomodoro (total e linhas por subtarefa), botão "Começar · 2 h 30 min" e o "+N" do excesso do Pomodoro.
3. **Progresso com o cronômetro rodando.** Não muda: o progresso conta só o que já foi registrado, e o tempo correndo aparece no relógio do cartão, como hoje. (Corrigido ao planejar: a primeira versão deste item supunha um progresso ao vivo que o app não tem.)
4. **O "+N" do excesso do Pomodoro.** Hoje ele fica escondido no primeiro minuto, para não mostrar "+0 min". *Decisão minha:* com segundos ele aparece a partir de 1 s ("+1 s"); abaixo de 1 s continua escondido.
5. **320 px.** Os textos mais longos quebram linha em vez de cortar. A conferência visual inclui "1 h 12 min 40 s de 2 h 30 min hoje" no card do Hoje e no relatório.

### 4.4 Conversão dos dados que existem

1. **Migração 009.** Numa transação:
   - `goals.target` × 60 nas metas de tempo;
   - `checkins.amount` × 60 nos registros de metas de tempo, incluindo os de compensação e os apagados (para que um apagado restaurado pela sincronização volte certo);
   - `focus_chunk.minutes` × 60, com a coluna renomeada para `seconds`;
   - nada muda em `updated_at`: a conversão não é uma edição e não entra na fila de envio.
2. **Sessão ou cronômetro em andamento na hora da atualização.** Guardam instantes, não totais, e continuam certos. O `carry_ms` de uma sessão em andamento fica como está: ele é o resto do último corte, e o próximo corte o soma aos segundos.

### 4.5 Sincronização

1. **Formato 2.** `SyncCodec.FormatVersion` passa de 1 para 2. No formato 2, metas e registros de tempo levam segundos.
2. **Aparelho antigo.** Um aparelho na 0.9.0 que recebe um registro do formato 2 já pausa a sincronização com o aviso "atualize o app", sem perder nada (spec de sincronização §6). Nada a fazer nele.
3. **Aparelho novo lendo o formato 1.** O servidor guarda o histórico cifrado no formato 1, e um aparelho novo continua recebendo registros do formato 1 (de outro aparelho ainda não atualizado, ou numa ressincronização completa). Ele lê o formato 1 como minutos e converte para segundos ao aplicar:
   - **meta do formato 1:** de tempo, alvo × 60;
   - **registro do formato 1:** × 60 se a meta dele for de tempo. Quando a meta ainda não chegou, o registro espera em `sync_pending`, que passa a guardar a versão do formato. Ele é convertido quando a meta chega.
4. **Envio.** O aparelho novo só envia no formato 2. Os registros do formato 1 que já estão no servidor não são reenviados: continuam válidos para quem os lê.
5. **O servidor não muda.** Ele só guarda blobs cifrados e não lê o valor.

## 5. O que fica para quem constrói

- Os nomes novos (`Seconds`, `CutSeconds`, coluna `seconds`) e onde a conversão de h/min fica no App.
- Como o leitor do formato 1 descobre a medida da meta de um registro (consulta à tabela de metas na aplicação da página).
- A ordem das tarefas, desde que cada uma deixe a `main` compilando e com os testes passando.
- Os testes de cada regra de §4, incluindo a migração (valores antes e depois, por medida) e a leitura do formato 1 com o registro chegando antes da meta.

## 6. Verificação

*(preenchida ao fim da construção)*
