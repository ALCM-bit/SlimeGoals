# Pomodoro e a página Tempo (v1)

> Desenho completo, em revisão com o autor (16/11/2026). O esboço aprovado ("Gostei. Achei ótimo") ficou em `.superpowers/brainstorm/` (fora do repositório). Caminho escolhido: A, a sessão de foco separada do cronômetro. Um revisor leu o desenho como quem vai construir. Das seis dúvidas dele, uma foi ao autor (o som do aviso: "Como despertador") e cinco foram decididas aqui, marcadas como *Decisão minha*.

## 1. Para que serve

O autor pediu "mais ferramentas e métodos de gestão de tempo como o pomodoro", que pudessem ser "vinculado a uma tarefa de tempo pelo usuário ou simplesmente ser usado". O motivo, nas palavras dele: falta "a possibilidade de gerenciar intervalos". A parte das tarefas menores já foi feita (`Tarefas_no_cronometro_v1.md`).

Este desenho cria a aba **Tempo**, com subabas: o **Pomodoro** é a primeira, e outros métodos podem chegar depois. Nele a pessoa monta uma sequência de blocos de foco e de pausa, com os tempos que quiser, e o app marca o ritmo: avisa quando cada bloco acaba e **espera um toque** para começar o próximo. O foco pode contar numa meta de tempo, escolhida no início ou no fim, ou ser descartado.

## 2. Objetivos, o que fica de fora e o que seria um fracasso

**Objetivos**
- Uma aba "Tempo" na navegação, com subabas. Nesta versão, só "Pomodoro".
- Montar a sequência: blocos de foco e de pausa, cada um com o seu tempo, e "repetir N vezes". O exemplo do autor: "1 atividade de 40 minutos, 1 pausa de 20, 1 atividade de 1 hora e 1 pausa de 30".
- A sequência usada por último reaparece na próxima abertura como sugestão, e a pessoa edita se quiser.
- Durante o foco, apontar a subatividade (✎) e trocá-la (⇄), como no cartão do cronômetro.
- Quando o bloco acaba, o app avisa e espera o toque. Ele nunca começa o próximo bloco sozinho (decisão do autor: "O app sugere, espera meu toque").
- Ter aviso e notificação próprios, separados dos lembretes (pedido do autor: "pode ser bom ele ter a própria").
- Na sessão sem meta, decidir no fim entre somar numa meta, criar uma meta com o tempo ou descartar.
- Ter alinhamento e espaçamento de nível sênior de UX/UI (pedido do autor; ver §6).

**Fica de fora nesta versão**
- Outros métodos na aba Tempo. A barra de subabas já existe, com uma aba só.
- Histórico das sessões. O tempo que entra numa meta aparece como os registros de sempre; o tempo descartado não fica em lugar nenhum.
- Sequências salvas com nome ("Estudo", "Trabalho"). Só existe a última usada.
- Sincronizar a sessão em andamento ou a sequência sugerida entre aparelhos. As duas ficam no aparelho, como o cronômetro. Os registros de tempo sincronizam como sempre.
- Avançar a sessão pelo aviso de fim de bloco (por exemplo, um botão "Começar pausa" na notificação). *Decisão minha:* tocar no aviso abre o app na aba Tempo. Um botão desses exigiria a mesma proteção do "Parar" (o código secreto do PR #42), e a pessoa vai querer olhar a tela antes de seguir. A tela de alarme do Android (§4.5) só cala o som e abre o app, sem mudar a sessão.

**Seria um fracasso se**
- o app interrompesse a pessoa começando um bloco sozinho;
- algum tempo de foco fosse contado duas vezes ou se perdesse, inclusive com o app fechado pelo Android, com o computador desligado ou numa troca de subatividade;
- o aviso de fim de bloco não tocasse com o celular minimizado ou de tela apagada;
- o cronômetro dos cartões do Hoje mudasse de comportamento;
- a página parecesse improvisada: desalinhada, com espaçamentos irregulares ou apertada a 320 px.

## 3. Caminhos considerados

- **A (escolhido): uma sessão de foco separada.** Peças próprias para a sessão: o estado no banco, um serviço, um alarme e um tipo de notificação. O cronômetro não muda. As duas peças se tocam em três pontos: (1) só um relógio roda por vez; (2) a janelinha e a notificação mostram "o que está rodando"; (3) os focos viram registros pelo mesmo caminho do ⇄. Custo: mais código novo.
- **B: estender o cronômetro com um "modo Pomodoro".** Reaproveitaria o banco e as telas de fora do app, mas toda regra do cronômetro supõe que existe uma meta (começar, registrar, a meta arquivada, a troca de tarefa) e ganharia exceções. Recusado: arrisca o que já foi conferido nos dois aparelhos.
- **C: o Pomodoro só liga e desliga o cronômetro a cada bloco.** Recusado: não funciona sem meta, e o relógio pausado ficaria remendado.

O autor perguntou se não bastava avisar ao fechar, em vez de guardar a sessão. No Linux, fechar a janela só a esconde, mas no Android o sistema encerra o app sem perguntar (memória, economia de bateria, deslizar dos recentes). Sem a sessão guardada, o alarme acordaria um app que não sabe que havia um bloco, e o tempo de uma sessão sem meta sumiria. Ele aprovou guardar ("Pode seguir com a A").

## 4. Como funciona

### 4.1 Preparar

- A aba Tempo abre na subaba Pomodoro. Sem sessão em andamento, ela mostra o **preparo**:
  - **Sequência:** a lista de blocos, cada um com tipo (Foco ou Pausa), tempo e um ✕ para tirar. Embaixo, "+ Foco", "+ Pausa" e "Repetir N×".
  - **Contar numa meta (opcional):** um seletor com "Sem meta" e as metas de tempo ativas hoje.
  - **Começar · <duração total>:** o botão principal mostra quanto tempo a sessão inteira dura, por exemplo "Começar · 2 h 30".
- **Sugestão:** a sequência e o "Repetir" da última sessão começada voltam na próxima abertura. A meta escolhida não volta: cada sessão começa "Sem meta". *Decisão minha:* repetir a meta de ontem sem querer contaria tempo no lugar errado.
- **Primeira vez:** Foco 25 min e Pausa 5 min, repetir 4×. *Decisão minha.*
- **Editar o tempo de um bloco:** horas e minutos, como no painel de lançar tempo e no editor do trecho. O autor reclamou quando o editor só aceitava minutos.
- **Limites (decisão minha):**
  - cada bloco tem de 1 min a 4 h;
  - a sequência tem até 12 blocos;
  - "Repetir" vai de 1 a 10;
  - precisa haver pelo menos um bloco de Foco.
  - Não há regra de alternância: dois focos ou duas pausas seguidas são permitidos.
- **Repetir N×** roda a lista inteira N vezes, com o limite de 12 blocos valendo para a lista, antes da repetição. *Decisão minha.*
- **Um relógio por vez:** com um cronômetro rodando ou parado à espera de registro, "Começar" fica desligado, com o motivo e um caminho para o Hoje. Com uma sessão existente, o ▶ dos cartões fica desligado com o motivo e um caminho para a aba Tempo, como já acontece entre duas metas.
  - Uma sessão existe do "Começar" até o destino do tempo. Isso inclui rodando, pausada, esperando o toque e a tela de fim de uma sessão sem meta à espera de destino. *Decisão minha:* é a mesma regra do cronômetro parado à espera de registro.
  - O "Iniciar" de um lembrete com uma sessão existente segue o caminho de hoje para cronômetro aberto: avisa e abre o app.
  - A guarda vale para os dois lados ao mesmo tempo. Dois toques quase simultâneos, um no ▶ e outro no "Começar", não podem deixar os dois rodando.

### 4.2 Rodando

- **O que aparece:**
  - uma bolinha por bloco **da rodada atual**: os feitos cheios, o atual destacado, as pausas com outra forma ou cor;
  - o selo da fase ("Foco · 2 de 2", "Pausa"), em que o número conta só os focos da rodada;
  - com "Repetir" maior que 1, uma linha discreta "Rodada 1 de 4" sob as bolinhas. *Decisão minha:* 12 blocos repetidos 10 vezes não caberiam em bolinhas a 320 px;
  - o tempo que falta em números grandes, minutos em cima e segundos embaixo, como na imagem de referência do autor;
  - a meta, se houver;
  - o campo da subatividade, só no foco;
  - os controles.
- **Blocos de 1 h ou mais:** a linha de cima mostra os minutos totais ("60", "75"). *Decisão minha:* mantém as duas linhas e a leitura simples. Quem constrói pode trocar por "1:15" se ficar mais claro, desde que a altura não mude entre os blocos.
- **Controles:**
  - **✕ Encerrar** pede confirmação.
  - **⏸/▶ Pausar e retomar o relógio.** Pausado, o tempo não corre e não conta.
  - **⏭ Pular para o próximo bloco.**
  - Os três ficam centrados, com o principal maior.
- **Subatividade:** o campo, o ✎ e o ⇄ funcionam como no cartão do cronômetro (`Tarefas_no_cronometro_v1.md` §4 e §7.1):
  - o nome é opcional e nasce na hora;
  - as sugestões vêm das notas recentes da meta escolhida, ou de todas as metas na sessão sem meta;
  - o ⇄ fecha o trecho atual e segue contando com o novo nome.
- **A subatividade continua de um foco para o outro**, até a pessoa trocar. *Decisão minha:* o mais comum é retomar o mesmo trabalho depois da pausa.

### 4.3 Fim de um bloco

- No instante em que o bloco acaba:
  - chega o **aviso próprio**, com som: "Hora da pausa" ao fim de um foco, "Hora de voltar ao foco" ao fim de uma pausa e "Sessão concluída" ao fim do último bloco;
  - a página mostra "Foco concluído" ou "Pausa concluída", conta o tempo extra ("+2 min") e oferece **[Encerrar]** e **[Começar pausa]** ou **[Voltar ao foco]**.
- **O tempo extra:**
  - depois de um foco, conta como foco até o toque, porque a pessoa estava trabalhando;
  - depois de uma pausa, não conta.
- **O próximo bloco começa no toque**, nunca antes. A contagem dele parte do instante do toque.
- **Se ninguém tocar:** o aviso fica na central e a página continua esperando, sem limite de tempo. *Decisão minha.* Uma espera de horas faria um foco esquecido virar horas de trabalho. Por isso, com 30 min ou mais de extra, qualquer toque que feche esse foco pede a **conferência do último trecho**. Isso vale para "Começar pausa", Encerrar, ⏭ e a tela de fim da sessão sem meta.
  - A conferência traz o trecho aberto (do último corte até agora, com o extra) em horas e minutos, como a conferência do cronômetro parado.
  - A pessoa pode diminuir ou aumentar esse trecho, até o limite de 24 h por registro que o app já tem. Os trechos anteriores já foram gravados e se ajustam pela lista "Hoje" do cartão da meta.
  - O que ela confirma é o que entra. O próximo bloco, se houver, começa no instante da confirmação.

### 4.4 Para onde vai o tempo

- **Trecho:** é o tempo de foco entre dois pontos de corte. Os pontos de corte são o início do foco, um ⇄, o fim do foco (no toque, com o extra), ⏭ e ✕. O tempo pausado com ⏸ fica de fora do trecho. Um trecho tem a subatividade como nota.
- **Arredondamento** (decisão minha, a partir da regra já aprovada do ⇄ e do ■ do cronômetro):
  - cada corte no meio da sessão (⇄, fim de um foco, ⏭) registra os minutos inteiros;
  - os segundos que sobram ficam guardados na sessão e passam para o próximo trecho de foco, mesmo atravessando uma pausa;
  - o último corte da sessão (fim do último foco, ou ✕) arredonda o que resta ao minuto mais próximo, como o ■ do cronômetro;
  - assim, a soma dos trechos fica a menos de meio minuto do foco real, e nenhum segundo é contado duas vezes.
- **Com meta escolhida no início:**
  - cada trecho entra na meta na hora do corte, como um registro comum (o mesmo caminho do ⇄: banco e fila da sincronização na mesma transação);
  - o progresso aparece no Hoje enquanto a sessão roda;
  - os lembretes da meta ficam calados durante a sessão, como já ficam com o cronômetro rodando.
- **Sem meta:**
  - os trechos ficam guardados no aparelho, junto com a sessão;
  - no fim (último bloco concluído ou ✕), a tela de fim mostra o foco total e os trechos por subatividade, e oferece três destinos.
- **Os três destinos da sessão sem meta:**
  - **Somar na meta…:** o seletor de metas de tempo. Os trechos viram registros nela, cada um com a sua nota, **com a data e a hora da escolha** e pelo caminho comum de registro.
    - *Decisão minha:* gravar com o instante original exigiria um caminho novo de registro no passado (períodos já fechados, sequência, compensação do total limitado, metas que ainda não existiam). A tela de fim aparece logo que a sessão acaba, então quase sempre o dia é o mesmo.
    - Se o destino for escolhido em outro dia ou outra semana, o tempo conta no período da escolha.
  - **Criar uma meta com ele:** abre o editor de meta já como meta de tempo. Ao salvar, os trechos entram nela. Ao cancelar, volta à tela de fim, sem perder nada.
  - **Descartar:** pede confirmação e apaga os trechos.
- **Escolher a meta no fim de uma sessão que começou sem meta** é sempre possível. Escolher no meio da sessão não é, nesta versão. *Decisão minha:* evita mudar o destino de trechos já feitos.
- **Meta arquivada, apagada ou que deixou de aceitar registro durante uma sessão com meta**, inclusive por sincronização: os trechos já gravados ficam, e a sessão segue como sem meta a partir dali. Ao fim, a tela de fim oferece os três destinos para o resto.

### 4.5 Fora do app

- **Linux:** a janelinha do cronômetro mostra também o Pomodoro, com a fase e o tempo que falta contando para baixo (por exemplo "Foco · Revisar capítulo 3", e "Estudar" quando há meta). O aviso de fim de bloco é uma notificação do GNOME disparada pelo processo em segundo plano. Para seguir a decisão "Como despertador", ela vai com urgência crítica, que o GNOME mostra mesmo no "Não perturbe" e mantém até ser dispensada, e com o som de alarme do tema ("alarm-clock-elapsed"). *Decisão minha.* Fechar a janela só a esconde, como hoje. Ao escolher "Sair" com uma sessão rodando, o app avisa que a sessão continua guardada e que o aviso de fim de bloco só toca com o app aberto.
- **Android:**
  - a notificação fixa mostra a fase e a contagem regressiva (o `Chronometer` já usado, em modo regressivo);
  - o aviso de fim de bloco vem por um **alarme próprio**, separado do alarme único dos lembretes. Ele acorda o app, lê a sessão guardada e publica o aviso num **canal próprio** ("Pomodoro");
  - **o aviso toca como o despertador do relógio** (decisão do autor: "Como despertador"):
    - toca no volume de alarme, mesmo no silencioso e no "Não perturbe", e vibra;
    - o alarme é armado como despertador (`setAlarmClock`), o tipo que o Android mais respeita na economia de bateria, com a mesma permissão de alarme exato que os lembretes já pedem;
    - acende a tela e mostra uma **tela de alarme** pequena sobre a tela de bloqueio, só com a fase ("Hora da pausa"), um botão "Parar som" e um botão "Abrir". "Abrir" leva à aba Tempo e passa pelo desbloqueio. *Decisão minha, por segurança:* nenhuma meta, tarefa ou tempo aparece sobre a tela de bloqueio;
    - o som para no "Parar som", ao tocar ou dispensar a notificação, ao abrir a aba Tempo ou depois de 1 minuto. *Decisão minha.* A notificação fica na central até o toque;
    - a tela cheia usa a permissão de aviso em tela cheia (`USE_FULL_SCREEN_INTENT`). Se o Android não a conceder, o aviso aparece no topo da tela, com o mesmo som de alarme;
  - depois de reiniciar o celular, de uma atualização do app ou de uma mudança de fuso, o alarme é rearmado como o dos lembretes (`SystemEventReceiver`);
  - sem permissão de alarme exato, cai para o inexato, como os lembretes.
- **Pausado (⏸):** a janelinha e a notificação mostram "Pausado" e o tempo parado. Não há alarme armado.

## 5. Restrições

- Arquitetura hexagonal do projeto. A sessão, as regras e o serviço ficam no Core; o banco, na Infrastructure; o alarme e a notificação, atrás de portas, implementadas no Linux e no Android.
- O cronômetro atual (`TimerService`, `ActiveTimer`, `active_timer`) não muda de comportamento. As únicas mudanças permitidas nele são a regra de um relógio por vez e o que a janelinha e a notificação mostram.
- Registros de tempo continuam sendo `Checkin` comuns. Nenhuma mudança no formato da sincronização.
- O tempo é guardado em instantes (UTC), não em contadores que dependem do app estar aberto. Fechar e reabrir mostra o tempo certo.
- `TreatWarningsAsErrors`, TDD, `Trace` sem a mensagem das exceções (`TraceText`).
- Textos em português, no tom do app.
- Android: as permissões novas são só `USE_FULL_SCREEN_INTENT` (tela de alarme) e a de mostrar sobre a tela de bloqueio na atividade da tela de alarme. Nenhum serviço em primeiro plano: o desenho do cronômetro fora do app já recusou um serviço sempre ligado.

## 6. Qualidade visual (pedido do autor)

"Só garanta o alinhamento e espaçamento. Como se fosse algo feito por um senior especialista em UX e UI." Na prática, para esta página:

- **Grade de espaçamento:** só os valores que o app já usa (múltiplos de 4: 4, 8, 12, 16, 24). Uma única margem lateral na página inteira, igual à do Hoje.
- **Alinhamento:**
  - os rótulos dos cartões ("Sequência", "Contar numa meta") alinham à esquerda com o conteúdo, numa mesma coluna;
  - os tempos dos blocos alinham à direita, numa coluna só;
  - o ✕ de cada bloco ocupa uma coluna fixa, mesmo quando falta.
- **Números grandes:** algarismos de largura fixa (tabulares), para o tempo não "tremer" a cada segundo. Os minutos e os segundos ficam centrados no mesmo eixo que o selo da fase, as bolinhas e os controles.
- **Simetria:** os três controles ficam centrados, com o mesmo espaço entre si; o ✕ e o ⏭ têm o mesmo tamanho. Na tela de fim, os dois botões dividem a largura por igual.
- **Alvos de toque:** pelo menos 44 px.
- **Larguras:** conferir no Render a 320, 360 e 1100 px. No desktop, o conteúdo fica numa coluna centrada de largura limitada, em vez de esticar até a borda.
- **Barra de baixo no celular:** cinco abas. "Configurações" vira "Config." só ali; a barra lateral do desktop mantém o nome inteiro. Todos os rótulos cabem a 320 px sem cortar.
- **Estados:** preparo, rodando, pausado, fim de foco, fim de pausa, fim da sessão com meta e fim da sessão sem meta. Cada um vira uma cena no Render (`tools/SlimeGoals.Render`) nas três larguras.
- **Cores:** as do tema. O foco usa a cor principal (roxa) e a pausa, a verde das metas (`GoalGreen`), como no esboço.

## 7. O que fica para quem constrói

- Os nomes e o formato da tabela da sessão e dos trechos guardados. Restrições: uma sessão por aparelho; ela sobrevive ao fechamento; os trechos sem meta somem quando a sessão termina.
- Onde guardar a sequência sugerida (`settings`, chave/valor, é o natural).
- Como a janelinha e a notificação passam a representar "o que está rodando". Restrição: o cronômetro aparece exatamente como hoje.
- O ícone da aba Tempo, no mesmo traço dos outros ícones do tema.
- Os textos exatos de confirmação, dentro do tom do app.
- A ordem do trabalho. Sugestão:
  1. Core e banco (sessão, regras, cortes, destinos);
  2. a página Tempo e a navegação;
  3. Linux (aviso e janelinha);
  4. Android (alarme, canal e notificação);
  5. conferência no desktop, no emulador e no Render.
