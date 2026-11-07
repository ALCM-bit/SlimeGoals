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
| `POST /auth/recover/begin`, `/auth/recover` | Recuperar com o código, em duas etapas: o `begin` confere o código e devolve `wrapped_key_recovery`, sem a qual o aparelho não abre a chave de dados; o `recover` confere o código de novo, define a nova senha e revoga todos os aparelhos |
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
| Código de recuperação | 128 bits aleatórios, mostrado uma vez. Gera outro par de chaves, como a senha |
| Guarda no servidor | Hash da `auth_key` e do código, e a chave de dados cifrada duas vezes |
| Guarda no aparelho | Android Keystore; Secret Service no Linux. Sem cofre disponível, a sincronização não liga: nunca em arquivo sem proteção |
| Senha | Mínimo de 10 caracteres |

- **`prelogin`:** e-mail desconhecido recebe um sal falso determinístico, para não revelar quem tem conta.
- **Novo aparelho:** e-mail e senha trazem a chave de dados cifrada, que é aberta localmente.
- **Trocar senha:** recifra só a chave de dados e revoga as outras sessões. Os aparelhos entram de novo, sem baixar tudo.
- **Recuperar:** o código abre a chave de dados e define nova senha.
- **Senha e código perdidos:** a cópia da nuvem é irrecuperável. A pessoa apaga a conta e recomeça a partir de um aparelho, sem perder os dados dele.

**O que o servidor ainda sabe:** e-mail, IP, quantos aparelhos existem, quantos registros e de que tamanho, e quando cada mudança aconteceu. O ritmo de mudanças pode sugerir hábitos. **Senha fraca:** quem roubar o banco pode testar senhas contra a chave cifrada; o Argon2id encarece isso, mas não impede.

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
