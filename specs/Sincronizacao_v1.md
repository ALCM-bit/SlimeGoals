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
- `sync_outbox` ganha `record_key`, gravada antes de cada envio. Assim a exclusão por chave encontra o item cujo aceite se perdeu.
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
- Exclusão usa `GoalRepository.DeleteAsync` e retira as notificações como `ReminderService.DeleteGoalAsync`, sob o mesmo `_gate`. Hoje `DeleteAsync` sempre enfileira, então a 9.2 precisa de um caminho de exclusão sem fila, com a mesma limpeza.
- Linha que não converte vai para o Trace e não trava a sincronização, como no `RowReader`.
- Registro que não pode ser lido mantém a cópia local: o `seq` dele e o `base_seq` da fila avançam, e ele continua confirmado.
- Versão de formato desconhecida pausa a sincronização, com o aviso "atualize o app". `last_seq` não avança e nada se perde.
- Um `DataChangeNotifier.Defer` agrupa a página num único aviso.
- Dados aplicados não voltam para a fila (inclusive a exclusão recebida, que usa o caminho sem fila).

**Quando roda:** ao abrir o app, 5 s depois da última mudança local, a cada 15 min com o app aberto e, no Android, ao voltar ao primeiro plano. Falhas de rede repetem com espera crescente, até 15 min.

## 5. Servidor

ASP.NET Core em `src/SlimeGoals.Server`, com PostgreSQL. Docker Compose para rodar localmente.

```
accounts(id, email, email_confirmed_at, kdf_salt, kdf_params, auth_hash, recovery_hash,
         wrapped_key_password, wrapped_key_recovery, next_seq, created_at)
devices(id, account_id, name, token_hash, last_seen_at, revoked_at)
records(account_id, record_key, seq, blob, deleted_at)   -- PK (account_id, record_key); índice (account_id, seq)
```

| Rota | Função |
|---|---|
| `POST /auth/prelogin` | Devolve sal e parâmetros do e-mail |
| `POST /auth/register`, `/auth/confirm`, `/auth/login`, `/auth/logout` | Conta e sessão |
| `POST /auth/recover`, `/auth/password` | Recuperar com o código, trocar senha |
| `GET /devices`, `DELETE /devices/{id}` | Aparelhos da conta |
| `DELETE /account` | Apaga conta e dados |
| `GET /sync/pull?after=N&limit=M&full=1\|0` | Registros com `seq > N`, em ordem |
| `POST /sync/push` | Lote de `{record_key, base_seq, blob ou delete}` |

**Regras:**

1. **Ordem sem buracos.** Cada envio roda numa transação que trava a linha da conta e usa `accounts.next_seq`. Uma sequência global do PostgreSQL permitiria a um `pull` pular um `seq` ainda não confirmado, e esse dado se perderia.
2. **Conflito por registro.** O item cujo `base_seq` difere do atual volta na lista de conflitos. Os demais do lote são aceitos. Um `delete` com `base_seq` 0 para uma chave que o servidor não tem é aceito.
3. **Reenvio seguro.** Repetir um envio já aceito responde com conflito, resolvido no ciclo seguinte sem duplicar.
4. **Exclusão.** Zera `blob`, grava `deleted_at` e aumenta o `seq`.
5. **Marcas antigas.** Limpas depois de 180 dias. A limpeza remove só marcas de exclusão, então registros vivos mantêm `seq` antigos. Com `full=0`, o servidor responde `410` quando `N` está abaixo do ponto da limpeza, inclusive `N = 0`, depois que uma limpeza aconteceu. Com `full=1`, nunca responde `410`. Sem o `full`, um aparelho que lê tudo receberia `410` na segunda página.
   - O aparelho usa `full=1` quando não tem registros confirmados, durante a sincronização completa e depois de um `410` no mesmo ciclo. Nos demais casos usa `full=0`, inclusive em `N = 0`: uma resposta sem `410` nunca remove dados locais, então uma resposta vazia de servidor restaurado de backup não faz perder nada.
   - Depois do `410`, o aparelho zera `last_seq`, marca `sync_records.confirmed = 0` e baixa tudo. O que não voltou e não está na fila foi excluído há mais de 180 dias, e sai do aparelho sem voltar para a fila.
   - No máximo um `410` por ciclo. O segundo conta como falha.
6. **Isolamento.** Toda consulta filtra pela conta da sessão, nunca por parâmetro do cliente.
7. **Limites.** Taxa por IP e por conta nas rotas `/auth`; tamanho máximo de blob e de lote. Cotas por conta configuráveis, desligadas por padrão.
8. **Logs.** Nunca registram e-mail completo, token ou blob.

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

Até a 9.6, a confirmação de e-mail é simulada: o link sai no log do servidor local.

## 11. Decisões em aberto

1. Grátis com limites ou pago (fase 9.6).
2. Onde hospedar e qual serviço de e-mail (fase 9.6).
3. Sincronização em segundo plano no Android (depois da primeira versão).
4. Fila no servidor (broker e consumidor) para muitos usuários: fica para depois da funcionalidade combinada.
