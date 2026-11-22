# Tags nas metas (v1)

> Desenho completo, aprovado pelo autor em 21/11/2026. Os esboços mostrados na conversa ficaram em `.superpowers/brainstorm/`, fora do repositório. Caminho escolhido: B, as tags têm cadastro próprio e sincronizam separadamente.
>
> Um revisor leu o desenho como quem vai construir. Ele mostrou que guardar as tags **dentro** da meta deixava, pela sincronização, uma tag ser apagada por uma edição antiga ou voltar depois de excluída. Isso fere dois pontos de "Seria um fracasso se". Por isso, a ligação entre meta e tag virou um registro próprio (§4.4). Essa e as outras decisões que vieram da revisão estão marcadas como *Decisão minha*.

## 1. Para que serve

O autor quer que cada meta receba tags ("remédios, estudos e por aí") para "facilitar a filtragem" e filtrar as metas na tela principal (o Hoje). As tags são escolhidas ou criadas num quadro da página de cadastro e edição da meta. O app já vem com três tags: **medicamentos**, **estudos** e **tarefas**.

Fora da meta, a pessoa pode **trocar a cor**, **renomear** e **excluir** uma tag. Uma tag pode existir sem nenhuma meta que a use.

## 2. Objetivos, o que fica de fora e o que seria um fracasso

**Objetivos**
- Marcar zero ou mais tags em cada meta, no quadro "Tags" da página da meta.
- Criar uma tag nova no próprio quadro.
- Gerenciar as tags em Configurações › Tags: criar, trocar a cor, renomear e excluir.
- Filtrar a lista "Metas de hoje" por uma ou mais tags, com um botão "Limpar".
- Sincronizar as tags e as tags de cada meta entre os aparelhos.
- A tag é identificada pelo **nome**, sem id combinado entre aparelhos (pedido do autor: "evitar dor de cabeça com ids que podem diferir entre sistemas").

**Fica de fora nesta versão**
- Filtro por tag na página Metas e nos Relatórios.
- Filtros salvos, e o filtro do Hoje guardado entre aberturas do app.
- Cores fora da paleta das metas.
- Tags nas notificações, na janelinha do Linux e na tela de bloqueio.

**Seria um fracasso se**
- a mesma tag aparecesse duplicada entre o Linux e o Android ("Estudos" e "estudos", ou duas "estudos");
- uma tag excluída voltasse sozinha;
- um aparelho com versão antiga, ou uma edição feita antes de atualizar, apagasse as tags de uma meta;
- o filtro mudasse o progresso da semana, os contadores ou o slime (decisão do autor: o filtro muda só a lista);
- a linha de filtros ou o quadro de tags ficassem apertados ou desalinhados a 320 px, ou crescessem sem limite com muitas tags.

## 3. Caminhos considerados

- **A: a tag é um texto dentro da meta.** A lista "tags que já existem" seria a união das tags em uso com as três padrão. Muda pouco a sincronização. Recusado pelo autor: não permite cor, renomear em todas as metas nem tag sem meta.
- **B (escolhido): cadastro próprio de tags, sincronizado à parte.**
  - Uma tabela `tags` e dois tipos novos de registro na sincronização: a tag e a ligação meta ↔ tag.
  - Os ids dos dois tipos são **derivados de nomes**: o da tag vem do nome normalizado, e o da ligação, do id da meta com o nome da tag. Assim, todo aparelho chega ao mesmo id sozinho, e o motor de sincronização, que chaveia tudo por `Guid`, não muda.
  - O conteúdo da meta **não muda**: as tags não viajam dentro dela.
  - Custo: formato 3 da sincronização, migração e as regras de §4.4.

## 4. Como funciona

### 4.1 O nome da tag

1. **Normalização**, sempre a mesma em todo lugar (função única no Core):
   1. normalização Unicode **NFC**. Um "é" digitado no Android e no GNOME pode chegar em formas Unicode diferentes;
   2. `Trim()` e cada sequência de espaços trocada por um espaço só;
   3. `ToLowerInvariant()`. Nunca `ToLower()`, que depende do idioma do aparelho.
2. **O nome gravado e mostrado é o normalizado**, em minúsculas ("medicamentos", "estudos").
3. **Acentos contam:** "remédios" e "remedios" são tags diferentes.
4. **Limite:** de 1 a **24** caracteres depois de normalizar (decisão do autor). Vazio é recusado com mensagem no campo.
5. **Ids derivados:** um UUID versão 5 calculado com SHA-256 (os 16 primeiros bytes, com os bits de versão e de variante ajustados), sobre o texto em UTF-8:
   - tag: `"slimegoals:tag:" + nome`;
   - ligação: `"slimegoals:goal-tag:" + id da meta ("D", minúsculo) + ":" + nome`.

   As duas são funções puras do Core, cobertas por teste com valores fixos, para que Linux e Android nunca divirjam.

### 4.2 Dados

**Tag** (Core, `Domain/Tag.cs`): `Id`, `Name`, `Color` (`GoalColor`, a mesma paleta de 4 cores das metas), `UpdatedAt`, `DeletedAt?`.

**Ligação meta ↔ tag** (Core, `Domain/GoalTag.cs`): `Id`, `GoalId`, `TagName`, `UpdatedAt`, `RemovedAt?`.

- **Nada é apagado de verdade.** Excluir uma tag grava `DeletedAt`. Tirar uma tag de uma meta grava `RemovedAt` na ligação. As duas mudanças sincronizam como registros comuns, não como exclusão no servidor (§4.4).
- `Goal` ganha `Tags`, os nomes visíveis em ordem alfabética, lidos de `goal_tags` (§4.4.5). `GoalDraft` ganha o mesmo campo, com padrão vazio, como foi feito com `Measure` e `Unit`. O conteúdo sincronizado da meta não inclui `Tags`.

**SQLite (migração `010_tags.sql`):**
- `tags (id TEXT PK, name TEXT NOT NULL UNIQUE, color TEXT NOT NULL, updated_at TEXT NOT NULL, deleted_at TEXT)`;
- `goal_tags (id TEXT PK, goal_id TEXT NOT NULL, tag_name TEXT NOT NULL, updated_at TEXT NOT NULL, removed_at TEXT, UNIQUE (goal_id, tag_name))`. Não há chave estrangeira nem para `tags` nem para `goals`, porque a ligação pode chegar antes da tag e antes da meta (§4.4.6). `GoalDeletion` apaga as ligações da meta à mão, como faz com as outras linhas filhas;
- `sync_outbox.kind` e `sync_records.kind` passam a aceitar `'tag'` e `'goal_tag'`. O SQLite não altera um `CHECK`, então essas duas tabelas são recriadas na migração, copiando os dados;
- as três tags padrão (§4.3).

### 4.3 As tags padrão

| Nome | Cor |
|---|---|
| medicamentos | Green |
| estudos | Purple |
| tarefas | Amber |

- São inseridas pela migração com o id derivado do nome e **`updated_at` = `1970-01-01T00:00:00Z`**. Assim, qualquer mudança real (cor, renomeação ou exclusão feita em qualquer aparelho) sempre vence essa cópia inicial. Um aparelho novo que entra na conta não ressuscita uma tag padrão que foi excluída em outro.
- As cópias iniciais **não entram na fila da sincronização**, nem quando a sincronização é ligada (§4.4.7). Elas só sobem quando a pessoa muda alguma delas.
- Depois de criadas, são tags comuns: podem ser renomeadas e excluídas. Excluída, não volta.
- "tarefas" mantém o nome, embora o cronômetro já chame de "tarefa" o nome do trabalho (decisão do autor).

### 4.4 Sincronização

1. **Formato 3.** `SyncCodec.FormatVersion` passa a 3, e `SyncRecordKind` ganha `Tag` e `GoalTag`.
   - Conteúdo da tag: `name`, `color`, `updatedAt` e `deletedAt`.
   - Conteúdo da ligação: `goalId`, `tagName`, `updatedAt` e `removedAt`.
   - O conteúdo da meta e o do registro continuam iguais aos do formato 2.
   - Ao aplicar, um registro cujo id não bate com o calculado a partir do conteúdo é recusado como inválido, como já acontece com "o conteúdo é de outro registro".
2. **Aparelho antigo** (≤ 0.12.1) que recebe um registro do formato 3 pausa a sincronização com o aviso "atualize o app", sem perder nada (spec de sincronização §6). Nada a fazer nele.
3. **Edições de meta não tocam nas tags.** Como as tags não viajam dentro da meta, nenhuma edição de meta, nem as feitas antes de atualizar ou ainda na fila, apaga ou ressuscita uma tag. Uma mudança de título concorrente com a exclusão de uma tag também não traz a tag de volta. *Decisão minha*, motivada pela revisão.
4. **Conflito**, para a tag e para a ligação: vence o `updated_at` mais novo; no empate, vence o maior id de aparelho do envelope, como nas metas.
   - A exclusão é só um campo do conteúdo. Por isso, **excluir e recriar com o mesmo nome**, ou tirar e pôr de novo a mesma tag numa meta, seguem a mesma regra: vale o que aconteceu por último.
   - Tags e ligações não usam `sync_tombstones` nem a exclusão no servidor. A exceção é a ligação de uma meta excluída (item 6).
   - **Data de uma edição:** mesma regra das metas. `updated_at` é o maior entre a hora atual e o `updated_at` local + 1 ms.
5. **O que aparece numa meta** é calculado na leitura por uma função pura do Core. O resultado não depende da ordem em que os registros chegaram nem de como foram divididos em páginas. Uma ligação aparece se:
   - ela não tem `RemovedAt`; **e**
   - a tag não está excluída, ou **a ligação é mais nova que a exclusão** (`ligação.UpdatedAt > tag.DeletedAt`).

   O segundo caso é o de alguém marcar a tag numa meta, em outro aparelho, depois que ela foi excluída. Para não perder essa escolha, a tag aparece de novo, como ativa, em todas as telas, com a cor que tinha. *Decisão minha.* Isso não conta como "voltar sozinha": alguém a marcou depois da exclusão. Excluir uma tag também grava `RemovedAt` em todas as ligações dela, com o mesmo instante, então as ligações antigas nunca a trazem de volta.

   Uma ligação cuja tag ainda não chegou aparece com a cor Purple até a tag chegar. Nada é gravado em `tags` por causa disso.
6. **Ligação de meta que ainda não existe:** é gravada em `goal_tags` assim mesmo, sem chave estrangeira, e só aparece quando a meta chega. *Decisão do plano:* isso dispensa a `sync_pending`, que guarda registros e os decodifica como registros.
   - **Meta excluída** apaga as ligações dela junto com os registros, pelo mesmo caminho de `GoalDeletion` e de `EnqueueGoalDeletionAsync`. É o único caso em que uma ligação vira exclusão no servidor, como os registros da meta.
   - Ligação que chega para uma meta com marca de exclusão é descartada, como os registros.
7. **Fila:**
   - Toda gravação de tag ou de ligação entra na `sync_outbox` (`kind = 'tag'` ou `'goal_tag'`, `op = 'upsert'`) na mesma transação.
   - `SyncApply` não grava nada na fila por causa das tags. A exceção que já existe, a das metas com marca de exclusão, continua sendo a única.
   - **Ao ligar a sincronização,** `SyncStateRepository.EnableAsync` também põe na fila todas as tags (menos as cópias iniciais de 1970) e todas as ligações. *Decisão minha.*

### 4.5 Operações (Core: `TagService`)

- **Criar(nome):** normaliza e valida.
  - Se já existe uma tag ativa com esse nome, devolve a existente sem erro.
  - Se existe uma excluída, reativa.
  - A cor de uma tag nova é a primeira da paleta (Purple → Pink → Amber → Green) que ainda não está em uso. Se todas estão em uso, usa a da posição `quantidade de tags ativas mod 4`.
- **TrocarCor(nome, cor).**
- **Renomear(antigo, novo):** numa transação só:
  1. cria ou reativa a tag nova, com a cor da antiga se for nova ou estava excluída;
  2. para cada ligação ativa da antiga, grava `RemovedAt` nela e cria ou reativa a ligação da mesma meta com o nome novo;
  3. exclui a antiga.

  Nenhuma meta muda, então o `updated_at` das metas fica como está.
  - **Se o nome novo já existe**, as duas tags se juntam. As metas ficam com a tag existente, que mantém a cor dela. Antes de juntar, a tela pede confirmação: "Já existe a tag "estudos". Juntar as duas?".
  - Renomear para o mesmo nome normalizado não faz nada.
- **Excluir(nome):** numa transação só, grava `RemovedAt` em todas as ligações ativas da tag e `DeletedAt` na tag, com o mesmo instante. Metas arquivadas também perdem a tag. Isso não passa por `GoalService.UpdateAsync`, que recusa editar metas arquivadas: a meta em si não muda.
- **Listar:** as tags visíveis (§4.4.5), em ordem alfabética, com a contagem de metas não excluídas que as usam. Arquivadas contam (*Decisão minha*). O texto mostra as arquivadas à parte: "usada em 3 metas (1 arquivada)".
- **Salvar uma meta** (`GoalService`, na mesma transação da meta):
  - cria as ligações novas e grava `RemovedAt` nas que saíram;
  - cria pela regra de "Criar" as tags que entram na meta agora e não estão visíveis (por exemplo, uma excluída por outro aparelho depois de marcada). O quadro da meta já grava as tags digitadas em "Nova tag" (§4.6).

  Uma tag que a meta já tinha e que ainda não chegou ao aparelho fica só como ligação, sem linha em `tags`. Gravar uma linha nova com a data de agora venceria a tag verdadeira que ainda está a caminho. *Decisão minha.*

### 4.6 O quadro de tags na página da meta

Fica logo depois de "Cor" (escolha do autor: o modelo C, um quadro próprio).

```
┌─ Tags ──────────────────────── Gerenciar tags › ─┐
│ (● medicamentos) (○ estudos) (○ tarefas) (● saúde)│
│ [ Nova tag…                              ] [Criar]│
└───────────────────────────────────────────────────┘
```

- **Chips** de todas as tags visíveis, em ordem alfabética, **mais as da meta que ainda não chegaram ao aparelho** (em Purple). Assim, salvar a meta não tira uma tag sem querer.
  - Tocar marca ou desmarca.
  - A tag marcada fica com o fundo e a borda da cor dela. A desmarcada fica neutra, com um ponto da cor dela.
  - Os chips quebram linha.
- **"Nova tag…" + Criar (ou Enter):** valida o nome (§4.1), **grava a tag no catálogo na hora** (a regra de "Criar" do §4.5) e a põe na lista **já marcada**, com a cor dela.
  - *Decisão do autor (22/11):* a tag já aparece em "Gerenciar tags ›" e nos outros aparelhos antes de salvar a meta. Cancelar a meta deixa a tag no catálogo, sem meta, e ela pode ser excluída em Configurações › Tags. A ligação com a meta só é gravada ao salvar.
  - Se o nome já está nos chips, a tag é só marcada, sem gravar: uma tag que ainda está a caminho não ganha linha nova.
  - Se a gravação falha, aparece "Não foi possível salvar a tag." abaixo do campo, e o nome digitado fica.
  - O erro aparece abaixo do campo, como nos outros campos da página.
- **"Gerenciar tags ›"** abre Configurações › Tags. *Decisão minha:* o formulário da meta não é perdido.
  - Voltar dessa tela retorna à página da meta com o que já estava preenchido.
  - As mudanças de cor e de nome feitas lá já aparecem nos chips. Uma tag marcada que foi renomeada ou juntada lá continua marcada, com o nome novo.
  - Se uma tag marcada no formulário for excluída lá, ela sai da seleção.
  - *Decisão da revisão final:* com o formulário aberto, uma exclusão que chega pela sincronização também tira a tag da seleção, e salvar não a traz de volta (§4.4.3).

### 4.7 Configurações › Tags

Uma linha nova "Tags" na lista de Configurações, entre "Lembretes" e "Janela". Ela abre uma seção no mesmo padrão das outras (Sincronização, Lembretes…).

```
Tags                                        [+ Nova tag]
● estudos        usada em 2 metas                  [✎] [🗑]
● medicamentos   usada em 3 metas (1 arquivada)    [✎] [🗑]
● trabalho       nenhuma meta                      [✎] [🗑]
```

- **Tocar no ponto de cor** abre as 4 cores e grava ao escolher.
- **✎** troca o nome por um campo com ✓ e ✕, no mesmo padrão do campo da tarefa do cronômetro. Com ✓, renomeia pelas regras de §4.5, inclusive a confirmação de juntar.
- **🗑** pede confirmação: "Excluir a tag "estudos"? Ela sai de 2 metas." Quando nenhuma meta usa a tag: "Excluir a tag "trabalho"?".
- **+ Nova tag** abre o mesmo campo, com ✓ e ✕, no topo da lista.
- **Lista vazia:** "Nenhuma tag. Crie uma aqui ou na página de uma meta."
- Tags que só existem como ligação (ainda não chegaram) não aparecem nesta lista.

### 4.8 O filtro no Hoje

O filtro muda **só** a lista "Metas de hoje". O progresso da semana, os contadores, a sequência, o slime e os avisos continuam contando todas as metas (decisão do autor).

```
Metas de hoje                                 [Pausar lembretes]
[✕ Limpar] (● medicamentos 3) (● estudos 2) (○ tarefas 2) (○ saú›
5 de 9 metas
```

- **Quais tags aparecem:** só as que alguma meta da lista de hoje usa. Uma tag sem meta hoje não ocupa espaço.
- **Ordem:** as tags com mais metas hoje primeiro; no empate, ordem alfabética. O número ao lado do nome é a quantidade de metas de hoje com aquela tag.
- **Seleção múltipla** (pedido do autor): a lista mostra as metas com **qualquer uma** das tags marcadas.
- **"✕ Limpar"** aparece só com alguma tag marcada. Ele fica fixo no começo da linha e não rola com as tags.
- **A linha nunca quebra:** ela rola de lado. No celular, com o dedo. No desktop, com a roda do mouse, e uma seta "›" com um esmaecido na ponta indica que há mais.
- **"5 de 9 metas"** aparece abaixo da linha só com filtro ativo.
- **Sem nenhuma meta com tag hoje**, a linha não aparece.
- **Duração do filtro:** em memória, até o app fechar. Ao abrir, a lista está sem filtro.
- **Tag marcada que sai da linha:** a última meta dela foi concluída e saiu da lista, ou a tag foi renomeada ou excluída. *Decisão minha:* ela sai da seleção. Sem nenhuma tag marcada, o filtro se limpa.
- **Filtro que esvazia a lista:** a mensagem é "Nenhuma meta de hoje com essas tags.", com o "Limpar" à vista.
- **Tags no cartão:** cada cartão do Hoje mostra as tags da meta em chips pequenos, abaixo do título e da linha de progresso. Meta sem tag não ganha essa linha.

## 5. O que fica para quem constrói

- **Telas:** a disposição exata do quadro, da seção de Configurações, da linha de filtros e dos chips do cartão. Conferir com `tools/SlimeGoals.Render` a 320, 360 e 1100 px, com simetria visual e nos dois temas. As cores dos chips vêm dos recursos `Goal*Bg`/`Goal*Fg` que já existem.
- **Navegação:** como a página da meta sobrevive à ida a Configurações › Tags (§4.6), dentro da navegação que o `ShellViewModel` já tem.
- **Migração 010:** a forma de recriar `sync_outbox` e `sync_records`, preservando a fila e os registros.
- **Rolagem:** a rolagem lateral da linha de filtros no Avalonia, com toque no Android e roda do mouse no desktop.
- **Testes:**
  - normalização (NFC, espaços, maiúsculas, limite de 24) e ids derivados com valores fixos;
  - criar, reativar, renomear, juntar e excluir no `TagService`, a fila de cada operação, e o `updated_at` das metas que não muda;
  - `SyncCodec` no formato 3 (tag e ligação), e a meta igual ao formato 2;
  - conflito por `updated_at` na tag e na ligação, e a tag padrão de 1970 perdendo para qualquer mudança;
  - a função de visibilidade (§4.4.5) com as entradas em qualquer ordem: exclusão contra ligação mais nova e mais antiga, ligação sem tag;
  - uma edição de meta enfileirada antes da atualização que não mexe nas tags;
  - ligação esperando a meta em `sync_pending`, e meta excluída que leva as ligações;
  - `EnableAsync` que enfileira tags e ligações, menos as cópias de 1970;
  - migração 010 sobre um banco com fila e registros;
  - ViewModels do quadro (inclusive a tag que ainda não chegou), da seção de Configurações e do filtro do Hoje (OU entre tags, "Limpar", seleção que some).
- **Documentos:** atualizar `specs/Sincronizacao_v1.md` §3 com as linhas das tags e das ligações, e `docs/arquitetura.md` se surgirem arquivos novos. Feito na verificação (§6).

## 6. Verificação

### Feito automaticamente (21/11/2026)

- **Build e testes:** `dotnet build SlimeGoals.slnx -c Release` sem avisos nem erros; `dotnet test --solution SlimeGoals.slnx`: 2117 testes, 0 falhas, 0 ignorados (com o Docker ligado para os testes do servidor).
- **Android:** `dotnet build SlimeGoals.Android.slnx` (comando de `docs/android.md`) sem avisos nem erros. Só compilado: o APK não foi instalado nem empacotado.
- **Telas:** `tools/SlimeGoals.Render` (README da ferramenta) a 320, 360, 412 e 1100 px:
  - `goal-edit-tags-<w>`: o quadro de tags na página da meta;
  - `settings-tags-<w>` e `settings-tags-edit-<w>`: Configurações › Tags e uma linha em cada estado;
  - `today-filter-idle-<w>`, `today-filter-<w>` (duas tags marcadas), `today-filter-single-<w>` e `today-filter-empty-<w>` (nenhuma meta de hoje com tag, sem linha de filtros).
- **Partida do desktop em pasta temporária** (`XDG_DATA_HOME` e `XDG_CONFIG_HOME` em `/tmp`): o app abriu e ficou de pé por 25 s, sem nada na saída de erro. O banco ficou na versão 10, com as tabelas `tags` e `goal_tags` e as três tags padrão (`medicamentos`, `estudos` e `tarefas`, com `updated_at` de 1970).

### Decisões de implementação

- **A seta "›" do filtro do Hoje também aparece no celular.** As views não têm um sinal confiável de celular ou desktop, e a seta não atrapalha o toque. Fica como está, não só no desktop como dizia a §4.8.
- **"Nenhuma meta de hoje com essas tags." é defensiva e hoje inalcançável.** Uma tag marcada sempre tem meta na lista: quando a última meta dela sai, a tag deixa a seleção (§4.8). Por isso a ferramenta de telas não a captura; o estado vazio que existe é o de nenhuma meta com tag.
- **O app só tem o tema escuro** (`RequestedThemeVariant="Dark"`), então a conferência de "dois temas" valeu para um só.

### Falta conferir à mão (pendente)

Roteiro no desktop, sempre com `XDG_DATA_HOME=/tmp/slimegoals-dev/data XDG_CONFIG_HOME=/tmp/slimegoals-dev/config dotnet run --project src/SlimeGoals.Desktop`:

- [ ] Criar uma meta com a tag "saúde" (digitada no quadro da meta). Antes de salvar, "Gerenciar tags ›" já mostra "saúde"; voltar mantém a tag marcada.
- [ ] No Hoje, filtrar por "saúde": a lista mostra só as metas com ela, "x de y metas" e "✕ Limpar" aparecem; limpar volta tudo.
- [ ] Em Configurações › Tags, renomear "saúde" para "estudos": o app pergunta se junta, e as metas das duas ficam com "estudos".
- [ ] Excluir "tarefas": a confirmação diz em quantas metas ela está, e a tag some das metas e do Hoje.
- [ ] Voltar da tela de tags para a página de uma meta aberta: o que estava digitado continua lá.
- [ ] Gerenciar tags › renomear a tag marcada › voltar › Salvar: a meta mantém a tag com o nome novo.

Conferência no emulador (`slimegoals-api36`, sempre com `-s emulator-5554`), com a conta de teste no servidor de desenvolvimento:

- [ ] Instalar, entrar na conta e ver que as tags criadas no desktop chegam com a cor certa.
- [ ] Ver que a exclusão de "tarefas" chega.
- [ ] Marcar uma tag numa meta no emulador e ver que ela chega ao desktop.
