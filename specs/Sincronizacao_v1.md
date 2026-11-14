# Sincronização entre dispositivos — Especificação

> Versão 1.0 (rascunho para revisão) · 06/11/2026 · Complementa `Planejamento_v3.md`

## 1. Objetivo

As mesmas metas e registros aparecem em todos os aparelhos de uma pessoa, sem que o servidor consiga lê-los. Quem não criar conta continua com o app de hoje: local, offline e sem nenhuma chamada de rede.

**Dentro do escopo:** conta, sincronização de metas e registros entre Linux e Android, servidor em Docker.

**Fora do escopo desta versão:** sincronização em segundo plano no Android, site, compartilhar metas entre pessoas, deduplicar metas iguais, histórico de versões, pagamento.

**Critério de sucesso:** dois aparelhos que editam offline, em qualquer ordem, terminam com os mesmos dados depois de sincronizar, e o banco do servidor não contém título, unidade nem nota legíveis.

## 2. Requisitos

| Id | Requisito |
|---|---|
| RS01 | Sincronizar é opcional e fica desligado até a pessoa criar conta ou entrar |
| RS02 | Sem conta, o app não faz nenhuma chamada de rede |
| RS03 | Sem internet, o app funciona igual; as mudanças esperam na fila local |
| RS04 | O servidor nunca recebe a senha, a chave de dados nem conteúdo legível |
| RS05 | Excluir uma meta apaga o conteúdo dela também no servidor |
| RS06 | Apagar a conta remove tudo do servidor e mantém os dados nos aparelhos |
| RS07 | Sair da conta mantém os dados no aparelho e remove chaves e fila |
| RS08 | Uma falha de sincronização nunca bloqueia nem atrasa o uso do app |
| RS09 | Configurações mostra o estado: última sincronização, pendências e erro |
| RS10 | O endereço do servidor é configurável, e tráfego sem TLS só é aceito em build de desenvolvimento |

## 3. O que sincroniza

| Dado | Sincroniza | Conflito |
|---|---|---|
| `goals` | Sim | Vence o `updated_at` mais novo; empate, o maior id de aparelho. A meta inteira, não campo a campo |
| `checkins` | Sim | Só são acrescentados, e ajuste é valor negativo. A exceção são as compensações concorrentes: vale a mais recente |
| Exclusão de meta | Sim | Vence qualquer edição e apaga os registros da meta |
| `reminder_log`, `settings`, `active_timer` | Não | Pertencem ao aparelho |

**Consequências aceitas:**

1. A edição perdedora some sem aviso.
2. A regra depende do relógio do aparelho: entre edições feitas sem que um aparelho visse a do outro, um relógio adiantado vence a mais recente.
3. Lembretes podem tocar nos dois aparelhos até a sincronização seguinte.
4. Entrar com dados em dois aparelhos une tudo, sem deduplicar.
5. Entre compensações concorrentes, o progresso da mais antiga deixa de contar no total.
6. Uma meta excluída pode voltar por um aparelho parado há mais de 180 dias, se quem excluiu saiu da conta: a marca de exclusão já foi limpa, e o aparelho antigo reenvia a meta como nova.

**Data de uma edição de meta:** ao gravar, `updated_at` é o maior valor entre a hora atual e o `updated_at` que o aparelho já tem para a meta mais 1 ms. Assim, uma edição feita depois de ver outra sempre vence essa outra, mesmo com o relógio atrasado. Gravar em UTC resolve o fuso, não a hora errada do aparelho. A regra vale para toda gravação que muda `updated_at` da meta, com ou sem sincronização ligada.

**Totais depois de juntar:** dois aparelhos offline podem marcar a mesma meta de conclusão (total 2) ou tirar mais do que havia (total negativo). Os registros não são alterados. Telas e relatórios limitam o total a `[0, alvo]` nas metas de conclusão e a no mínimo 0 nas demais. A validação de `CheckinService` parte do total limitado, e o valor gravado é a diferença até o total real, para que um toque tenha o efeito visível. Os dois totais são normalmente iguais; só diferem depois de trocar a recorrência da meta, e aí a tela já parte do total limitado. O limite vale para o total do período, nunca para o dia dentro de uma semana. O valor gravado pode passar de `MaxAmount` por causa da compensação, então a 9.2 não pode recusar isso ao aplicar o que chega.

**Compensações concorrentes:** compensação é o registro gravado com valor diferente do pedido. Quando dois aparelhos compensam a mesma meta no mesmo período sem que um visse o registro do outro, só a mais recente conta: vence o `done_at` mais novo; empate, o maior id do registro (`checkins` não guarda o aparelho). A mais antiga continua guardada, mas fica fora dos totais, para todos os aparelhos chegarem ao mesmo resultado em qualquer ordem. Exemplo: com total real −3, somar 2 em cada aparelho grava 5 nos dois, e o total final é 2, não 7. O registro indica que é compensação com `Compensation`: o início do período (`Period`, gravado porque fusos diferentes dariam períodos diferentes) e os ids das compensações que o aparelho já tinha naquele período (`Seen`). Duas compensações são concorrentes quando nenhuma está no `Seen` da outra. Da mais nova para a mais antiga, uma compensação só conta se não for concorrente com nenhuma das que já contam. O resultado depende só do conjunto de registros, não da ordem de chegada. A 9.1 não precisa disso, porque sem sincronização não há concorrência.

## 4. Lado do aparelho

Migração `005_sync.sql`:

- `sync_state`: uma linha, com `enabled`, id do aparelho, `last_seq`, última sincronização e último erro.
- `sync_outbox`: `record_id`, tipo (`goal` ou `checkin`), operação (`upsert` ou `delete`), `base_seq`, `revision`, ordem de criação. Uma linha por registro: nova mudança substitui a anterior e mantém a posição na fila. `revision` muda a cada substituição e impede que um envio em andamento tire da fila uma edição mais nova. Registro que o servidor nunca viu (sem `seq` conhecido) sai da fila ao ser excluído.
- `sync_records`: `record_id`, tipo, `seq` conhecido. Sabe o `base_seq` de cada registro.
- `sync_tombstones`: ids de metas excluídas, para descartar dados atrasados.
- `sync_pending`: registros recebidos cuja meta ainda não chegou.

Migração `006_sync_engine.sql`:

- `sync_records` ganha `record_key` e `confirmed`. Exclusão chega sem blob, só com a chave, e sem esse mapa o aparelho não saberia o que apagar. Chave que não está em `sync_records` nem na fila é ignorada: o aparelho nunca teve o registro. `confirmed` marca o que o servidor já confirmou.
- `sync_outbox` ganha `record_key`, gravada antes de cada envio, com índice. Assim a exclusão por chave encontra o item cujo aceite se perdeu, sem varrer a fila.
- `sync_state` ganha `full_resync`. Na sincronização completa, o estado fica salvo ali, e uma interrupção continua de onde parou.
- `checkins` ganha as colunas da compensação, e a coluna local `superseded`, que não sincroniza. Cada aparelho a recalcula depois de aplicar uma página. Listas e somas ignoram essas linhas, mas a medida continua travada, porque o registro existe.

**Depois da exclusão:** as linhas de `sync_records` (só id, tipo e `seq`) ficam até o aceite do `delete`, e a 9.2 as limpa nesse aceite.

**Fila só com a sincronização ligada.** Sem conta, nada é gravado na fila, senão ela cresceria para sempre. Ao ligar, todas as metas e registros entram na fila de uma vez.

**Gravação:** repositórios gravam dado e fila na mesma transação. `GoalRepository.AddAsync`, `UpdateAsync` e `CheckinRepository.AddAsync` passam a usar transação explícita.

**Ciclo de sincronização** (um por vez, nunca sobreposto):

1. Busca páginas de `GET /sync/pull?after=last_seq` e aplica cada uma numa transação, avançando `last_seq` no mesmo `COMMIT`.
2. Envia a fila em lotes. Os itens aceitos saem da fila na mesma transação que atualiza `sync_records`.
3. Em conflito, volta ao passo 1. Qualquer conflito em um lote interrompe o envio: os itens aceitos saem da fila e o aparelho busca de novo. O registro local e o remoto passam pela regra da seção 3, e o vencedor é reenviado se for o local. Sem item pendente, o registro recebido é aplicado direto, porque o servidor já resolveu a ordem.
4. Depois de 3 tentativas seguidas, para e tenta no próximo ciclo.

**Aplicação do que chega:**

- Metas antes de registros, dentro da página.
- Registro de meta desconhecida vai para `sync_pending` e é aplicado quando a meta chegar. Isso é necessário porque editar uma meta aumenta o `seq` dela, e um aparelho novo pode receber registros antes da meta.
- Registro de meta em `sync_tombstones` é descartado, e o aparelho envia a exclusão dele.
- Exclusão usa `GoalDeletion.DeleteRowsAsync`, a mesma limpeza de `GoalRepository.DeleteAsync`, sem passar pela fila. As notificações saem por `IRemoteDeletionGate`, que o `ReminderService` implementa sob o mesmo `_gate` de `DeleteGoalAsync`.
- Linha que não converte vai para o Trace e não trava a sincronização, como no `RowReader`.
- Registro que não pode ser lido mantém a cópia local: o `seq` dele e o `base_seq` da fila avançam, e ele continua confirmado.
- Versão de formato desconhecida pausa a sincronização, com o aviso "atualize o app". `last_seq` não avança e nada se perde.
- Um `DataChangeNotifier.Defer` agrupa a página num único aviso.
- Dados aplicados não voltam para a fila (inclusive a exclusão recebida, que usa o caminho sem fila).

**Quando roda:** ao abrir o app, 5 s depois da última mudança, a cada 15 min com o app aberto e, no Android, ao voltar ao primeiro plano. Mudança é todo aviso do `DataChangeNotifier`, inclusive o de uma página aplicada: depois de baixar algo, roda mais um ciclo, que normalmente não encontra nada e não avisa. Falhas repetem com espera crescente, de 30 s até 15 min. Uma mudança durante essa espera a encurta para 5 s, e a contagem de falhas continua. Página que não foi aplicada não avisa, então não encurta a espera.

## 5. Servidor

ASP.NET Core em `src/SlimeGoals.Server`, com PostgreSQL. Docker Compose para rodar localmente.

```
accounts(id, email, email_confirmed_at, kdf_salt, kdf_params, auth_hash, recovery_hash,
         wrapped_key_password, wrapped_key_recovery, next_seq, purge_point,
         confirm_hash, confirm_expires_at, created_at)
devices(id, account_id, name, token_hash, last_seen_at, revoked_at)
records(account_id, record_key, seq, blob, deleted_at)   -- PK (account_id, record_key); índice (account_id, seq)
```

Acréscimos da 9.3 ao esquema: `purge_point` (ponto da limpeza, começa em 0), `confirm_hash` e `confirm_expires_at` (confirmação do e-mail). `next_seq` começa em 1, então o maior `seq` da conta é `next_seq - 1`. Índices: `accounts(email)` único, `devices(token_hash)` único, `devices(account_id)`, `records(account_id, seq)` e `records(deleted_at)` parcial, só das marcas de exclusão.

| Rota | Função |
|---|---|
| `POST /auth/prelogin` | Devolve sal e parâmetros do e-mail |
| `POST /auth/register`, `/auth/confirm`, `/auth/login`, `/auth/logout` | Conta e sessão |
| `POST /auth/recover/begin`, `/auth/recover` | Recuperar com o código, em duas etapas: o `begin` confere o código e devolve `wrapped_key_recovery`, sem a qual o aparelho não abre a chave de dados; o `recover` confere o código de novo, define a nova senha, troca o código de recuperação (o usado deixa de valer) e revoga todos os aparelhos |
| `POST /auth/password` | Trocar senha: exige a `auth_key` atual no corpo |
| `GET /devices`, `DELETE /devices/{id}` | Aparelhos da conta |
| `DELETE /account` | Apaga conta e dados; exige a `auth_key` no corpo, além do token |
| `GET /sync/pull?after=N&limit=M&full=1\|0` | Registros com `seq > N`, em ordem |
| `POST /sync/push` | Lote de `{record_key, base_seq, blob}` ou `{record_key, base_seq, delete: true}` |
| `GET /health` | Responde `200` se o banco responde, `503` se não |

**Respostas de que o motor depende** (`key` é a `record_key`):

- `pull`: `{records: [{key, seq, blob}], has_more}`. `records` vem em ordem de `seq`, com no máximo `limit` itens. `blob` nulo é registro excluído. `has_more` diz se ainda há registros depois da página. O aparelho trata como falha uma página vazia com `has_more` verdadeiro e uma página cujo maior `seq` não passa de `after`.
- `push`: o item leva `blob` ou `delete: true`, nunca os dois; um item sem nenhum dos dois recebe `400`, para um cliente com defeito não apagar um registro. A resposta é `{accepted: [{key, seq}], conflicts: [key]}`. `seq` é o novo `seq` do registro. Todo item do lote aparece em uma das listas. Um lote sem nenhum item aceito faz o aparelho parar de enviar e baixar de novo.

**Regras:**

1. **Ordem sem buracos.** Cada envio roda numa transação que trava a linha da conta e usa `accounts.next_seq`. Uma sequência global do PostgreSQL permitiria a um `pull` pular um `seq` ainda não confirmado, e esse dado se perderia.
2. **Conflito por registro.** O item cujo `base_seq` difere do atual volta na lista de conflitos. Os demais do lote são aceitos, e o item recusado não gasta `seq`. O atual de uma chave que o servidor não tem é 0: `upsert` ou `delete` com base acima de 0 dá conflito. Um `delete` com `base_seq` 0 para essa chave é aceito e cria uma marca de exclusão, com `seq` novo.
3. **Reenvio seguro.** Repetir um envio já aceito responde com conflito, resolvido no ciclo seguinte sem duplicar.
4. **Exclusão.** Zera `blob`, grava `deleted_at` e aumenta o `seq`.
5. **Marcas antigas.** Limpas depois de 180 dias. A limpeza remove só marcas de exclusão, então registros vivos mantêm `seq` antigos. O ponto da limpeza nunca passa do maior `seq` da conta. Marcas com `seq` igual ou acima do ponto ficam, mesmo depois dos 180 dias, então o registro mais novo da conta sempre sobrevive. Sem isso, quem lê tudo terminaria abaixo do ponto, e cada ciclo daria `410` e outra leitura completa. Com `full=0`, o servidor responde `410` quando `N` está abaixo do ponto da limpeza, inclusive `N = 0`, depois que uma limpeza aconteceu. Com `N` igual ao ponto ou acima dele, não responde `410`. Com `full=1`, nunca responde `410`. Sem o `full`, um aparelho que lê tudo receberia `410` na segunda página.
   - **Fórmula da limpeza** (por conta, com a linha da conta travada, uma vez por dia): `candidato` = maior `seq` das marcas com `deleted_at` anterior ao corte, mais 1; `ponto` = `MAX(purge_point atual, MIN(candidato, next_seq - 1))`. Remove as marcas anteriores ao corte com `seq` abaixo do ponto. O ponto nunca recua.
   - O `pull` lê o ponto e os registros numa transação `REPEATABLE READ` somente leitura, do mesmo instante. Com leituras separadas, uma limpeza entre as duas faria um aparelho perder marcas sem receber `410`.
   - O aparelho ignora marca de exclusão de chave desconhecida, mas `last_seq` avança com ela. É assim que um aparelho novo alcança o ponto da limpeza.
   - O aparelho usa `full=1` quando está em `N = 0` sem nenhuma linha em `sync_records`, durante a sincronização completa e depois de um `410` no mesmo ciclo. Nos demais casos usa `full=0`, inclusive em `N = 0`: uma resposta sem `410` nunca remove dados locais, então uma resposta vazia de servidor restaurado de backup não faz perder nada.
   - Depois do `410`, o aparelho zera `last_seq`, marca `sync_records.confirmed = 0` e baixa tudo. O que não voltou e não está na fila foi excluído há mais de 180 dias, e sai do aparelho sem voltar para a fila.
   - No máximo um `410` por ciclo. O segundo conta como falha.
6. **Isolamento.** Toda consulta filtra pela conta da sessão, nunca por parâmetro do cliente.
7. **Limites.** Taxa por IP e por e-mail nas rotas `/auth`, e falhas de `auth_key` por conta em `DELETE /account` e `POST /auth/password`; tamanho máximo de blob e de lote. Cotas por conta configuráveis, desligadas por padrão. Os valores estão em "Decisões da fase 9.3".
8. **Logs.** Nunca registram e-mail completo, token ou blob.

### Decisões da fase 9.3

Acordos feitos ao implementar o servidor. Onde diferem do texto acima, valem estes.

**Token do aparelho.** 256 bits aleatórios, enviado em `Authorization: Bearer` e guardado como SHA-256 em `devices.token_hash`. Não expira: dura até sair, revogar, trocar a senha ou recuperar. `last_seen_at` é atualizado no máximo uma vez por hora.

**Hash de `auth_key` e do código.** SHA-256, sem Argon2id no servidor, comparado com `CryptographicOperations.FixedTimeEquals`. O custo de testar senhas já está no Argon2id do aparelho, e a `auth_key` tem 256 bits. Um hash lento no servidor só abriria espaço para negação de serviço.

**Cadastro não revela quem tem conta.** Todo cadastro com corpo válido recebe a mesma resposta `202`. E-mail de conta confirmada não muda nada nem recebe e-mail. E-mail de conta ainda não confirmada substitui a conta pendente, numa só instrução: sal, parâmetros, `auth_key`, código de recuperação, chaves cifradas, código de confirmação e vencimento novos, e outro e-mail de confirmação sai. O código anterior deixa de valer. A conta não confirmada some depois de 24 h.

**Conta não confirmada é tratada como inexistente.** O `prelogin` devolve o sal falso e o `login` responde `401` mesmo com a chave certa. Sem isso, cadastrar com chave própria e tentar entrar revelaria se o e-mail estava livre. Por isso `email_not_confirmed` não é um código de erro.

**Confirmação ligada a quem cadastrou.** `POST /auth/confirm` recebe `{"token", "auth_key"}`. Só confirma se o código for o mais recente, estiver no prazo e a `auth_key` for a do cadastro, comparada em tempo constante. Código desconhecido, vencido, substituído ou com `auth_key` de outro cadastro dão o mesmo `400 invalid_token`. Sem isso, quem cadastrasse o e-mail de outra pessoa com as próprias chaves faria a vítima confirmar, com o código que chegou primeiro, a conta do atacante. A vítima ficaria presa a um sal e a um código de recuperação que não são dela.

**Limitação aceita do cadastro.** Quem conhece o e-mail de alguém pode cadastrá-lo de novo e invalidar o código pendente, mas não confirma nada sem a caixa de entrada. O limite por e-mail segura a repetição. A conta pendente vence em 24 h, e a limpeza roda uma vez por dia.

**Obrigação para a 9.4: confirmar dentro do app.** O e-mail leva um código que a pessoa digita ou cola no app, não um link solto: a confirmação precisa da `auth_key`, que só o aparelho que cadastrou tem. O app guarda a `auth_key` derivada até a confirmação, ou deriva de novo pedindo a senha.

**E-mail de confirmação.** `IEmailSender`, com `LogEmailSender`, que escreve o token no log, com o e-mail mascarado (`a***@exemplo.com`), e só existe em `Development`. É a exceção à regra 8 da §5, prevista na §10. Fora de `Development`, o servidor recusa iniciar sem um envio real e sem `Server:PreloginSecret`.

**Obrigação para a 9.6: envio em segundo plano.** O envio real precisa rodar em segundo plano, para um e-mail novo e um já usado levarem o mesmo tempo para responder. Enviar na própria requisição revelaria quem tem conta pelo tempo da resposta.

**Apagar a conta e trocar a senha pedem a `auth_key` no corpo**, além do token. Um token roubado lê os blobs cifrados, mas não apaga a cópia da nuvem nem troca a senha. As duas rotas têm limite de 10 falhas de `auth_key` por minuto por conta, para o token roubado não testar senhas sem o limite que o login tem.

**Sal falso.** `HMAC-SHA-256(Server:PreloginSecret, e-mail)`, cortado em 16 bytes, com `Server:DefaultKdfParams` igual a `{"v":1,"m":65536,"t":3,"p":1}`.

**E-mail normalizado.** `Trim()` e `ToLowerInvariant()` antes de qualquer uso, com índice único na coluna.

**Cota.** Só `Sync:MaxRecordsPerAccount`, nula por padrão. Passar dela recusa o lote inteiro com `403 quota_exceeded`.

**`limit` acima do máximo é reduzido, não recusado.** `has_more` continua correto, então o aparelho não percebe diferença.

**Valores que a spec não fixa:**

| Item | Valor |
|---|---|
| `limit` do `pull` | padrão 200, máximo 500 |
| Lote do `push` | até 100 itens |
| Blob | até 64 KiB |
| Corpo | até 64 KiB nas rotas que não são `/sync/push`; até 10 MiB no `/sync/push`; acima disso, `413 too_large` |
| `/auth` | 20 pedidos por minuto por IP e 10 por minuto por e-mail |
| Falhas de `auth_key` | 10 por minuto por conta, em `DELETE /account` e `POST /auth/password` |
| `record_key` | de 1 a 128 caracteres |
| E-mail | até 254 caracteres |
| `auth_key` e chave de recuperação | 32 bytes |
| `kdf_salt` | de 16 a 64 bytes |
| `kdf_params` | até 256 caracteres |
| Chaves cifradas | até 256 bytes |
| Token de confirmação | vale 24 h |
| Marcas de exclusão | 180 dias |

**Limites de taxa e proxy reverso (obrigação para a 9.6).** O limite por IP usa `RemoteIpAddress`. Atrás de um proxy reverso, todos os pedidos pareceriam vir do proxy; é preciso configurar `ForwardedHeaders` com os proxies conhecidos. Os contadores ficam na memória do processo: reiniciar o servidor os zera, e só há uma instância.

**Respostas de erro.** Corpo `{"error":"<código>"}`, em minúsculas e sem detalhes internos.

| Status | Código | Quando |
|---|---|---|
| 400 | `invalid_request` | Campo ausente, formato ou tamanho errado, `limit` ou `after` inválidos, lote acima do máximo |
| 400 | `invalid_token` | Confirmação desconhecida, vencida, substituída por um novo cadastro ou com `auth_key` de outro cadastro |
| 401 | `unauthorized` | Token ausente, desconhecido ou revogado; credencial errada; e-mail desconhecido ou não confirmado no login |
| 403 | `quota_exceeded` | Cota da conta |
| 404 | `not_found` | Aparelho que não existe nesta conta |
| 410 | `reset_required` | Regra 5 |
| 413 | `too_large` | Blob ou corpo acima do máximo |
| 429 | `rate_limited` | Limite de taxa, com `Retry-After` |

**Docker Compose.** `docker compose up -d --build --wait` sobe o PostgreSQL (`postgres:18-alpine`, sem porta publicada) e a API em `http://127.0.0.1:8080`, em modo `Development`, com o processo rodando como usuário `app`. O servidor aplica as migrações ao iniciar.

## 6. Criptografia e conta

| Item | Decisão |
|---|---|
| Derivação da senha | Argon2id, 64 MiB, 3 passadas, 1 faixa; parâmetros salvos por conta. A validar no Android (seção 8) |
| Chaves derivadas da senha | HKDF gera a `auth_key` (enviada ao servidor) e a `wrap_key` (nunca sai do aparelho) |
| Chave de dados | 256 bits aleatórios, criada no cadastro |
| Conteúdo | AES-256-GCM, nonce aleatório de 96 bits, com subchave da chave de dados |
| Dado autenticado | Versão do formato, tipo e id do registro ficam dentro do conteúdo cifrado, porque quem baixa um blob só tem a `record_key`. `Open` recebe a chave, abre o blob e recusa se `RecordKey(id)` não bater. O servidor não consegue trocar um blob por outro |
| `record_key` | HMAC-SHA-256 do id, com subchave da chave de dados |
| Código de recuperação | 128 bits aleatórios, mostrado uma vez. Gera outro par de chaves, como a senha. Vale para uma recuperação só: cada recuperação gera um código novo |
| Guarda no servidor | Hash da `auth_key` e do código, e a chave de dados cifrada duas vezes |
| Guarda no aparelho | Android Keystore; Secret Service no Linux. Sem cofre disponível, a sincronização não liga: nunca em arquivo sem proteção |
| Senha | Mínimo de 10 caracteres |

- **`prelogin`:** e-mail desconhecido recebe um sal falso determinístico, para não revelar quem tem conta.
- **Novo aparelho:** e-mail e senha trazem a chave de dados cifrada, que é aberta localmente.
- **Trocar senha:** recifra só a chave de dados e revoga as outras sessões. Os aparelhos entram de novo, sem baixar tudo.
- **Recuperar:** o código abre a chave de dados e define nova senha.
- **Senha e código perdidos:** a cópia da nuvem é irrecuperável. A pessoa apaga a conta e recomeça a partir de um aparelho, sem perder os dados dele.

**O que o servidor ainda sabe:** e-mail, IP, quantos aparelhos existem, quantos registros e de que tamanho, e quando cada mudança aconteceu. O ritmo de mudanças pode sugerir hábitos. **Senha fraca:** quem roubar o banco pode testar senhas contra a chave cifrada; o Argon2id encarece isso, mas não impede.

### Decisões da fase 9.4

Acordos feitos ao implementar a criptografia e a conta (`src/SlimeGoals.Sync/Crypto` e `src/SlimeGoals.Sync/Account`). Onde diferem do texto acima, valem estes. A 9.4 não tem telas: o app ainda não referencia `SlimeGoals.Sync`.

**Cadastro pendente só na memória.** `RegisterAsync` devolve um `PendingRegistration` com a `auth_key` e a `wrap_key`, e `ConfirmAsync` entra com elas, sem um segundo Argon2id. É assim que a 9.4 cumpre a obrigação de confirmar dentro do app. Fechar o app antes de confirmar significa cadastrar de novo: o servidor substitui a conta pendente e manda outro código. Derivar de novo pela senha não funciona, porque o `prelogin` esconde a conta não confirmada e devolve o sal falso. O código é de uso único: se a entrada falhar depois que o servidor o aceitou (cofre recusado, rede caída no `login`), o mesmo `PendingRegistration` repete `ConfirmAsync` sem mandar o código de novo e só refaz a entrada.

**`kdf_params` do servidor têm piso e teto.** O cliente aceita só `v` igual a 1, `m` de 65536 a 262144 KiB, `t` de 3 a 10 e `p` de 1 a 4. Sal com menos de 16 bytes também é recusado. Fora disso, `KdfParamsException` antes de derivar, e nada é enviado depois do `prelogin`.
- O piso impede que um servidor malicioso peça parâmetros fracos, que deixariam a `auth_key` barata de testar contra senhas.
- O teto impede que ele peça 4 GiB de memória ao celular.

**Senha.** Normalizada em NFKC antes de virar UTF-8, então um "ç" composto ou decomposto dá a mesma chave. Tem de 10 a 1024 caracteres, contados depois da normalização. Senha nova (cadastro, troca, recuperação) passa por `PasswordKeys.Validate` antes de qualquer pedido; a do `login` não, porque já existe.

**Derivação.**
- `mestre = Argon2id(senha, kdf_salt, m, t, p)`, com 32 bytes. Padrão `{"v":1,"m":65536,"t":3,"p":1}`.
- `auth_key = HKDF-SHA256(mestre, info "slimegoals/v1/auth")` e `wrap_key = HKDF-SHA256(mestre, info "slimegoals/v1/wrap")`, ambas com 32 bytes e sem sal no HKDF.
- O sal do cadastro tem 16 bytes aleatórios.

**Código de recuperação.** 128 bits aleatórios em Base32 de Crockford: 26 caracteres em grupos de 4 separados por hífen, o último com 2.
- A leitura ignora caixa, espaços e hífens, e troca `O` por `0` e `I`/`L` por `1`. Tamanho, alfabeto ou bits finais errados lançam `FormatException`.
- `recovery_key = HKDF(código, "slimegoals/v1/recovery-auth")` e `recovery_wrap = HKDF(código, "slimegoals/v1/recovery-wrap")`, sem Argon2id, porque o código já tem 128 bits.

**Embrulho da chave de dados.** `versão 1 (1 byte) ‖ nonce (12) ‖ cifra (32) ‖ tag (16)`, 61 bytes, em AES-256-GCM com a `wrap_key` ou a `recovery_wrap`. O dado autenticado é `"slimegoals/v1/wrap-password"` ou `"slimegoals/v1/wrap-recovery"`, o que impede usar um embrulho no lugar do outro.

**Blob do registro.** `versão 1 (1 byte) ‖ nonce (12) ‖ cifra ‖ tag (16)`, com o `SyncEnvelope` em JSON dentro da cifra.
- Subchaves: `enc = HKDF(dados, "slimegoals/v1/enc")` e `rk = HKDF(dados, "slimegoals/v1/record-key")`.
- `record_key = base64url(HMAC-SHA256(rk, id))`, sem preenchimento, com 43 caracteres. O id vai em 16 bytes na ordem de `Guid.TryWriteBytes` com `bigEndian: true` (a ordem do texto do GUID).
- O dado autenticado é `"slimegoals/v1/record" ‖ record_key` em UTF-8: blob trocado de chave falha já no GCM. `Open` também confere `RecordKey(envelope.RecordId)`.
- Byte de versão ou versão do envelope desconhecidos lançam `SyncFormatException`, que pausa o motor. A versão do envelope é lida antes dos outros campos, então uma versão nova com outra forma também pausa, em vez de virar registro ilegível. Tag inválida lança `CryptographicException`.

**Vetores fixos.** Conferidos fora do .NET (OpenSSL e Python). A 9.5 tem de obter os mesmos valores no Android.

| Entrada | Saída |
|---|---|
| Senha `correct horse battery`, sal `000102…0f` (16 bytes), parâmetros padrão | `auth_key` `ba53acc24ffa9ba24189d1f5b7946535d1ee123feaef82cc6d9b5cfbb2f172bc` |
| A mesma | `wrap_key` `67c2cecefb41882ac98e816bab7619c23f0dc2d30a089743bb33c67ed79bc552` |
| Código com os bytes `000102…0f`, mostrado como `000G-40R4-0M30-E209-185G-R38E-1W` | `recovery_key` `944be12c002a09efb7f8b498f2063f4b674d48a0d24e0bb8d4d814e625dd2159` |
| O mesmo | `recovery_wrap` `c7da2cd9178e6498001e21d80eead4bdd5ea7273c354e309ad2bd7fe4e1e3eef` |
| Chave de dados `000102…1f` (32 bytes), id `00112233-4455-6677-8899-aabbccddeeff` | `record_key` `QDTsiTp-QhBeJKBWIU_Iu_FbuyRZgQnQWvn4-NzxRxY` |

Os testes estão em `tests/SlimeGoals.Sync.Tests/Crypto` (`PasswordKeysTests`, `RecoveryCodeTests`, `AesGcmSyncCryptoTests`).

**O que vai ao cofre.** Um único segredo, `sync-session`, com um JSON de `server`, `email`, `device_id` (o do servidor), `token` e `data_key` (base64). Um item só é gravado e apagado de uma vez.
- No Secret Service, o item fica na coleção `default`, com os atributos `application=slimegoals` e `name=sync-session` (e, desde a 9.5a, `profile`). Sem a coleção, ela é criada ao gravar.
- Coleção travada chama `Unlock` e espera o `Prompt`. Recusa ou ausência do serviço lançam `SecretVaultUnavailableException`.
- Segredo ilegível faz `OpenSessionAsync` lançar `InvalidDataException`; `SignOutAsync` trata como sessão ausente e limpa, para a conta não ficar presa.

**Cofre antes da rede.** `RegisterAsync`, `ConfirmAsync`, `LoginAsync` e `RecoverAsync` conferem `IsAvailableAsync` antes do primeiro pedido. Sem cofre, a sincronização não liga, e nada é gravado em arquivo.

**Ordem de entrada.**
1. `login`;
2. abrir a chave de dados;
3. gravar no cofre;
4. `EnableAsync`.

Se o passo 2, 3 ou 4 falhar, o segredo é apagado se chegou a ser gravado, o aparelho faz `logout` do token emitido, ignorando a falha dele, e relança o erro original. Entrar com a sincronização já ligada é recusado ("Saia da conta antes de entrar em outra."): é preciso sair antes.

**Trocar a senha** mantém este aparelho e revoga os outros. A chave de dados não muda, e o cofre deste aparelho também não. Os revogados recebem `401` no ciclo. Desde a 9.5a, eles não saem: entram de novo só com a senha e mantêm a fila (ver "Aparelho revogado não perde a fila" em "Decisões da fase 9.5a"). Sair e entrar, que zera o estado e baixa tudo de novo sem duplicatas (provado em `EncryptedSyncEndToEndTests`), perderia as edições offline ainda na fila.

**Sair (`SignOutAsync`).**
1. tenta `POST /auth/logout`;
2. apaga o segredo do cofre;
3. chama `DisableAsync`.

Falha de rede ou da API no passo 1 não impede os outros dois. Cofre indisponível ao ler a sessão lança `SecretVaultUnavailableException` sem mudar nada: a sincronização continua ligada até a pessoa destravar o chaveiro e tentar de novo.

**Apagar a conta** pede a senha: `prelogin`, derivação da `auth_key` e `DELETE /account`. Depois, a mesma limpeza local de sair, sem `logout`, e metas e registros ficam no aparelho (RS06). Senha errada não muda nada local.

**Erros da API.** `AccountApiException` com `Kind`: `Unauthorized`, `InvalidToken`, `InvalidRequest`, `RateLimited` (com `RetryAfter`), `TooLarge`, `NotFound` ou `Unavailable` (rede, 5xx e resposta ilegível). Os textos para a pessoa ficam na 9.5.

**`device_id` local e do servidor seguem separados.** `sync_state.device_id` continua sendo o do envelope (9.2). O do servidor fica no cofre, para `IsCurrent` e para revogar.

**Privacidade provada.** `EncryptedSyncEndToEndTests` varre todas as colunas de texto e binárias de `accounts`, `devices` e `records`, achadas pelo `information_schema`. Procura título, unidade, nota, senha e código de recuperação em UTF-8, UTF-16, base64 e na forma escapada do JSON (`á`), além de um pedaço só ASCII de cada valor, porque o JSON do registro escapa o acento. Com a cripto trocada por texto puro, o teste falha.

**Obrigações para a 9.5.**
- **Cofre do Android**, sobre o Keystore, com o mesmo segredo `sync-session`.
- **Vetores no Android:** os da tabela acima, no emulador e no aparelho, com o Konscious no arm64.
- **Sair com envio da fila e aviso** (§7): tentar enviar antes de `SignOutAsync`, avisar do que se perde e deixar a pessoa escolher.
- **`DisableAsync` com o ciclo cancelado:** cancelar e esperar o ciclo do motor antes de desligar, e descartar a `SyncSession` (o provedor de token passa a devolver nulo).
- **Serializar as operações de conta:** o serviço não impede duas ao mesmo tempo. Dois `login` juntos deixam um aparelho órfão no servidor.
- **Revogar o próprio aparelho:** `RevokeDeviceAsync` aceita o aparelho atual, e a sincronização fica ligada com um token morto. A tela esconde essa opção ou a troca por sair.
- **Mapear para textos** `AccountApiException`, `KdfParamsException`, `SecretVaultUnavailableException`, a `FormatException` do código de recuperação e a `ArgumentException` de e-mail, senha e nome do aparelho.
- **Cancelamento depois do servidor:** cancelar depois que o servidor já aceitou (conta apagada, recuperação feita) ainda pode interromper a limpeza ou a entrada local. A tela não oferece cancelar nesses passos, ou refaz a limpeza.
- **Endereço do servidor configurável e TLS:** o `AccountService` aceita qualquer `Uri` e não exige HTTPS. Fora do desenvolvimento, só HTTPS.
- **Teste manual do chaveiro do GNOME:** destravar pelo diálogo de verdade e recusar o pedido. Os testes automáticos só cobrem o `Prompt` recusado (daemon isolado) e o aceito (Secret Service falso).

### Decisões da fase 9.5a

Acordos feitos ao ligar a conta e a sincronização no Linux (`SyncCoordinator`, `AccountViewModel`, `AccountView` e a composição do head). Onde diferem do texto acima, valem estes.

**Build de desenvolvimento.** A seção "Conta e sincronização" e tudo o que ela usa só entram no head Linux com a constante `SYNC_DEV`, definida em Debug ou com `-p:SyncDev=true`. O Release (`packaging/linux/build.sh`) não registra cofre, coordenador nem tela, e continua sem rede. O Android compila o `SlimeGoals.Sync`, mas não registra nada dele até a 9.5b.

**Endereço do servidor.** Fica em `settings` (`sync_server`), com padrão `http://127.0.0.1:8080`, e só pode ser editado com a conta desligada; com a conta ligada, vale o servidor gravado na sessão do cofre. Aceita só URL absoluta `http` ou `https`, sem credenciais embutidas (`usuario:senha@`), consulta nem fragmento. O `http` é tolerado porque a seção só existe no build de desenvolvimento; a 9.6 exige `https`.

**Aparelho revogado não perde a fila.** O `401` no ciclo vira `SyncUnauthorizedException`, o motor devolve `SyncCycleResult.Unauthorized` e grava "A sessão deste aparelho foi encerrada. Entre de novo." em `last_error`. O coordenador para o motor, descarta a sessão e passa a `SessionExpired`, sem tocar na fila, em `last_seq` nem em `sync_state.enabled`.
- `ReauthenticateAsync(senha, nome do aparelho)` (o nome, como ao entrar, para o servidor não ver um aparelho genérico) usa o e-mail e o servidor da sessão do cofre, entra, abre a chave de dados e regrava o segredo com o token e o aparelho novos, sem `EnableAsync`. O motor volta e envia a fila; a chave de dados não muda com a troca de senha, então a fila continua válida.
- A reentrada recusa uma sessão de outro servidor, antes de qualquer pedido, e uma conta trocada (chave de dados diferente), revogando o token recém-emitido. Nos dois casos, o segredo antigo fica como estava.
- "Sair" continua disponível nesse estado, com o aviso de que a fila se perde.

**Estados da conta.** `SignedOut`, `SignedIn`, `SessionExpired`, `VaultLocked` (o cofre não abriu ao iniciar; "Tentar de novo" abre o diálogo do chaveiro) e `SessionUnreadable` (segredo ilegível, ou sincronização ligada sem segredo: só "Sair" funciona). Com a sincronização desligada, o início não toca no cofre, para não abrir o diálogo do chaveiro a cada abertura de quem não tem conta.

**Uma operação de conta por vez.** Todas as operações do coordenador passam por um único portão e rodam fora da thread da interface; a segunda espera a primeira, e a tela desabilita os botões enquanto trabalha. Isso cumpre a obrigação "dois `login` juntos deixam um aparelho órfão".

**Sair em dois passos.** `FlushAsync` roda um ciclo e devolve quantos itens ficaram na fila. Com zero, a tela sai direto; senão, mostra o aviso com "Esperar" e "Sair mesmo assim". Fora de `SignedIn`, o ciclo não roda e o aviso aparece sempre que houver pendências.

**Revogar o próprio aparelho.** A lista mostra "Este aparelho" no lugar do botão, e `RevokeDeviceAsync` com o id atual lança "Para desconectar este aparelho, use Sair.".

**Perfil do cofre por banco.** O segredo ganha o atributo `profile`, o hash curto do caminho do banco (o mesmo da instância única, sem o caminho em claro), e a procura usa `application`, `name` e `profile`. Assim, dois apps de teste com `XDG_DATA_HOME` diferentes no mesmo login não dividem o `sync-session`.

**Textos para a pessoa.** Vêm só das tabelas fixas de `SyncErrorText` e `AccountMessages`; o detalhe técnico vai ao Trace, sem segredos. A `Message` de uma exceção só aparece quando a exceção foi lançada pelo próprio app, sem o sufixo " (Parameter '…')"; as do framework e do servidor viram "Algo deu errado. Tente de novo.".

**Segredos na tela.** Os campos de senha e o código de recuperação digitado são limpos depois de cada envio, com sucesso ou falha, e ao fechar a subpágina. A página do código de recuperação só fecha (Voltar ou Continuar) depois de marcar "Guardei o código", porque o servidor já trocou o código.

**Cliente HTTP do head.** Um por servidor, com 30 s de prazo e sem seguir redirecionamento, para um `3xx` não levar o token a outro host.

### Decisões depois do teste manual da 9.5a

**Código de recuperação de uso único.** Até a 9.5a, recuperar a conta trocava só a senha, e o mesmo código continuava valendo. Um código já digitado pode ter sido visto por alguém, então cada recuperação passa a trocar o código:

- O `POST /auth/recover` leva também `new_recovery_key` e `new_wrapped_key_recovery`, os do código novo, gerado no aparelho. Sem eles, ou com a mesma chave do código usado, a resposta é `400`.
- O servidor troca a senha, o código e revoga os aparelhos numa transação, e só se o código conferido ainda for o atual. De dois pedidos com o mesmo código, só o primeiro passa; o outro recebe `401`.
- O app mostra o código novo com o aviso de que o anterior deixou de valer.

**Resposta perdida na recuperação.** Depois que o servidor troca o código, só o novo vale, e ele só existe na memória do aparelho. Por isso:

- Sem resposta do `recover`, o app pergunta ao servidor qual código vale, com um `recover/begin` do código novo. Se for o novo, segue como se a resposta tivesse chegado. Se for o antigo, mostra o erro de conexão, e nada mudou.
- Se nem essa conferência chegar, o app mostra o código novo com outro texto: a pessoa guarda o novo e tenta entrar com a nova senha. Se a senha nova funcionar, vale o código novo; se não, o antigo continua valendo.
- Se a troca passou mas a entrada no aparelho falhou (rede ou cofre), o app mostra o código novo assim mesmo e pede para entrar com a nova senha.

Fica para a 9.6: gerar um código novo com a conta ligada, para quem perdeu o papel ou ficou em dúvida sobre qual código vale.

**Segredos do servidor em desenvolvimento.** `packaging/server/init-env.sh` cria o `.env` do Compose com valores aleatórios quando ele não existe, com permissão só para o dono. Em produção (9.6), os segredos entram como segredos do Docker Compose, em arquivos sob `/run/secrets`, e não como variáveis de ambiente.

**Diagnóstico sem mensagens de exceção.** No Linux, o `Trace` do app vai para o journal do sistema e fica gravado. Por isso nenhuma chamada grava a exceção inteira nem a mensagem dela: `TraceText.Of` dá os tipos da cadeia, o status HTTP e a pilha; `TraceText.Types`, só os tipos. Um teste varre o código e falha se alguma chamada voltar a interpolar a exceção. O caminho do banco também saiu dessas mensagens.

O custo é investigar sem a mensagem: o tipo e a pilha dizem onde falhou, mas às vezes não dizem por quê. Possibilidade guardada para quando fizer falta, por decisão do autor em 13/11/2026: uma variável de ambiente (por exemplo `SLIMEGOALS_TRACE_DETALHE=1`) que volta a gravar a mensagem completa, desligada por padrão.

**Arquivo do banco.** Na primeira abertura, o banco e os arquivos ao lado dele (WAL, cópias antigas) ficam com permissão só para o dono (600), além da pasta de dados, que já é privada.

**Validação dos campos da conta.** As regras de e-mail e de nome do aparelho ficam em `AccountRules` (`SlimeGoals.Sync.Contracts`), usadas pelo app e pelo servidor: o app recusa antes de enviar exatamente o que o servidor recusaria.

- **E-mail:** até 254 caracteres, um só `@`, algo antes dele e um domínio com pelo menos um ponto e sem partes vazias, sem espaços nem caracteres de controle. Quem prova que o endereço existe é o e-mail de confirmação.
- **Nome do aparelho:** de 1 a 100 caracteres, sem caracteres de controle.
- **Tela:** os campos de texto limitam o tamanho com as mesmas constantes do código. Os campos de senha não limitam, porque cortar em silêncio o que foi colado num campo mascarado trocaria a senha sem a pessoa ver; o tamanho (10 a 1024) é conferido no envio. Os botões de enviar só ligam com os campos obrigatórios preenchidos.
- **Banco do servidor:** a migração `002_limits` repete os tamanhos em restrições `CHECK` (e-mail, parâmetros de derivação, chaves, nome do aparelho, chave do registro), como segunda barreira.

## 7. Interface e Android

- Configurações ganha a seção **Conta e sincronização**, visível por `PlatformCapabilities`.
- O cadastro mostra o código de recuperação e exige confirmação antes de continuar.
- O texto explica, antes do cadastro, que perder senha e código perde a cópia da nuvem.
- **Sair da conta** tenta enviar a fila antes de chamar `DisableAsync`, que apaga a fila e as marcas de exclusão. Sem isso, uma meta excluída offline voltaria do servidor ao entrar de novo. Se o envio falhar, o app avisa que há alterações não enviadas, que elas se perdem ao sair e que metas excluídas depois de sair podem voltar, e a pessoa escolhe esperar ou sair mesmo assim.
- O Android passa a declarar `INTERNET`, com `network_security_config.xml` sem tráfego em texto puro. Build de desenvolvimento libera só o servidor local.
- README e tela Sobre deixam de dizer que os dados nunca saem do aparelho: passam a explicar que, com conta, saem cifrados.

## 8. Spike antes de implementar

Pergunta: as peças de criptografia funcionam nas duas plataformas com desempenho aceitável?

| Item | Aprovado se |
|---|---|
| `AesGcm` do .NET no Android | Cifra e decifra em API 26 e 36 |
| Biblioteca de Argon2id | Roda em x86-64 e arm64, com licença compatível |
| Tempo do Argon2id | Até 3 s em celular intermediário; senão, reduzir memória |
| Android Keystore | Guarda e recupera a chave depois de reiniciar |
| Secret Service | Guarda e recupera no GNOME, com caminho claro quando não existe |

O código do spike é descartável, como em `specs/spikes/`.

## 9. Testes

- **Convergência:** dois e três aparelhos simulados, com ordens de envio embaralhadas, terminam iguais.
- **Motor:** exclusão contra edição, registro antes da meta, página interrompida, reenvio, relógio divergente, edição com relógio atrasado depois de receber outra mais nova, versão desconhecida, meta excluída com envio em andamento ou com o aceite perdido (o item sai da fila sem gerar `delete`, e o registro que volta no `pull`, inclusive o da meta, é removido pela marca de exclusão e o aparelho envia o `delete`).
- **Totais:** conclusão com total 2, total negativo, em Hoje e nos relatórios. Compensações concorrentes em dois e três aparelhos: só a mais recente conta, em qualquer ordem de chegada; compensação feita depois de ver a outra conta normalmente.
- **Servidor** (PostgreSQL em contêiner): isolamento entre contas, envios simultâneos sem buracos, conflito parcial, `410`, limites.
- **Criptografia:** blob adulterado ou trocado é recusado; vetores fixos entre plataformas.
- **Privacidade:** busca por título, unidade e nota conhecidos no dump do banco não encontra nada.
- **Rede:** sem conta, nenhum soquete aberto.
- **Ponta a ponta:** Linux e emulador com `docker compose`, incluindo aparelho novo e recuperação.

## 10. Fases

| Fase | Entrega |
|---|---|
| 9.0 | Esta spec e o spike |
| 9.1 | Migração 005 e fila, sem rede. Limite de totais. Data de edição |
| 9.2 | Motor e merge com servidor falso. Compensações concorrentes |
| 9.3 | Servidor e Docker Compose |
| 9.4 | Criptografia e conta |
| 9.5 | Interface, permissão de internet, Linux e Android sincronizando |
| 9.6 | Publicação, depois de decidir grátis ou pago: domínio, TLS, backup, e-mail, política de privacidade, nova auditoria |

Até a 9.6, a confirmação de e-mail é simulada: o código sai no log do servidor local, e a pessoa o cola no app.

## 11. Decisões em aberto

1. Grátis com limites ou pago (fase 9.6).
2. Onde hospedar e qual serviço de e-mail (fase 9.6).
3. Sincronização em segundo plano no Android (depois da primeira versão).
4. Fila no servidor (broker e consumidor) para muitos usuários: fica para depois da funcionalidade combinada.
