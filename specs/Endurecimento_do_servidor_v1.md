# Endurecimento do servidor (v1)

> Construído em 18/11/2026 (tarefas 1 a 10 do plano). Rascunho de 17/11/2026, para o autor revisar. Fecha os 9 itens da auditoria de 13/11/2026 que bloqueiam expor o servidor, mais a obrigação da 9.6 de enviar o e-mail em segundo plano. Vale junto com `specs/Sincronizacao_v1.md`: onde as duas divergem, esta vale.

## 1. Para que serve

O autor quer que cada pessoa hospede o próprio servidor. Ele não pretende oferecer um servidor público. Mesmo assim, um servidor ligado à internet precisa aguentar quem o encontrar por acaso. Nas palavras do autor: "Podemos atuar na mais segura para garantir, mas é livre na prática. Precisamos garantir pontos como rate limit."

Hoje o servidor só é seguro porque fica preso ao 127.0.0.1. Este desenho deixa o `compose.yaml` pronto para subir numa máquina com domínio próprio, sem segredo em variável de ambiente e com o mínimo de privilégio em cada peça.

## 2. Objetivos e o que fica de fora

**Objetivos**
- Os itens 1 a 9 da auditoria resolvidos e cada um coberto por teste ou por uma conferência escrita no plano.
- O e-mail de confirmação sai de verdade, por SMTP, em segundo plano. O Resend é o exemplo documentado.
- Fora de `Development`, o servidor se recusa a subir com uma configuração insegura, em vez de subir avisando.
- O fluxo de desenvolvimento de hoje continua: o código de confirmação aparece no log.

**Fica de fora**
- Escolher onde hospedar, comprar o domínio, a política de privacidade e a seção de conta no build de Release do app (continuam na 9.6).
- Backup do banco (9.6).
- Cota em bytes por conta: a cota em número de registros, com blob de até 64 KiB, já põe um teto, e o cadastro passa a ser só por convite.
- Os itens "baratos" da auditoria que não são do servidor (permissão 600 do banco local, Trace antigo, checksum do tarball). O secret scanning, a push protection e os alertas do Dependabot já estão ligados no GitHub.

**Seria um fracasso se**
- Um segredo aparecesse em `docker inspect`, no log ou numa resposta.
- O servidor ficasse acessível em HTTP puro, ou direto, sem passar pelo proxy.
- Alguém sem convite conseguisse criar conta.
- Um atacante conseguisse impedir a vítima de entrar só sabendo o e-mail dela, com poucos pedidos.
- Um aparelho que sincroniza todo dia passasse a pedir a senha.

## 3. Decisões

| # | Item da auditoria | Decisão |
|---|---|---|
| D1 | 1. Sem TLS | Caddy no Compose, à frente do servidor, com HTTPS automático para `SLIMEGOALS_DOMAIN` (Let's Encrypt) e redirecionamento de 80 para 443. Com `SLIMEGOALS_DOMAIN=localhost`, o Caddy usa a própria CA interna, o que basta para o teste local. O servidor não publica porta nenhuma. O app só aceita `https://`. O `http://` continua valendo só para `127.0.0.1`, `::1` e `localhost` |
| D2 | 2. Compose em `Development` | O `compose.yaml` sobe em `Production`. O desenvolvimento usa `compose.dev.yaml` por cima: `Development`, e-mail simulado e porta `127.0.0.1:8080` direto no servidor, sem o Caddy. Não existe `compose.override.yaml`, para o modo de desenvolvimento nunca entrar sem querer |
| D3 | 3. IP do proxy | `ForwardedHeaders` só com `X-Forwarded-For` e `X-Forwarded-Proto`, `ForwardLimit = 1` e confiança só na rede `Server:TrustedProxyNetworks`. No Compose, é só o IP fixo do Caddy, `10.89.0.2/32`, na rede interna `edge` (sub-rede `10.89.0.0/24`, IPs automáticos só em `10.89.0.128/25`). A sub-rede inteira não: ela inclui o gateway `10.89.0.1`, por onde entram os processos do host e as portas publicadas, que poderiam forjar o cabeçalho. Fora desse IP, o cabeçalho é ignorado |
| D4 | 4. Superusuário | Três papéis. `postgres` (o superusuário da imagem) só existe para criar os outros dois. `slimegoals_owner` é dono do banco e das tabelas e só roda as migrações (`ConnectionStrings:PostgresMigrations`). `slimegoals_app` só lê e grava linhas (`ConnectionStrings:Postgres`). Fora de `Development`, o servidor se recusa a subir se o papel do app for superusuário, puder criar papéis ou bancos, ou for o mesmo das migrações |
| D5 | 5. Cadastro aberto | Cadastro só com convite: `Server:InviteCode`. Fora de `Development` e sem convite configurado, o cadastro fica fechado. Convite ausente ou errado, ou cadastro fechado, dão a mesma resposta `403 registration_closed`, conferida em tempo constante e antes de qualquer acesso ao banco. A resposta não revela quem tem conta, porque é a mesma para qualquer e-mail. O `init-env.sh` gera um convite de 128 bits. A cota passa a vir ligada: `Sync:MaxRecordsPerAccount = 100000` |
| D6 | 6. Senha padrão | O `appsettings.json` não traz string de conexão. Sem ela, o servidor não sobe, com uma mensagem que não ecoa nenhum valor |
| D7 | 7. Segredos e contêiner | Segredos do Compose em arquivos (`secrets:`), lidos pelo servidor em `/run/secrets` via `AddKeyPerFile`, com o nome do arquivo igual à chave (`ConnectionStrings__Postgres`). Nada de segredo em `environment:`. Os arquivos ficam em `secrets/`, fora do repositório: pasta com permissão 700 e arquivos com 644. O Compose sem Swarm monta o arquivo com o dono e a permissão do host, e o usuário `app` do contêiner precisa conseguir ler. A pasta 700 protege no host. Contêineres com `read_only`, `cap_drop: [ALL]` (o PostgreSQL e o Caddy voltam só com as capacidades que precisam), `no-new-privileges`, `tmpfs` onde precisa gravar e `pids_limit`. Imagens fixadas por digest, com o Dependabot atualizando |
| D8 | 8. Travar o login da vítima | O limite por e-mail de qualquer IP sai. Entram dois: (a) 10 pedidos por minuto por par e-mail + IP em `prelogin`, `login`, `register` e `recover/*`; (b) 50 falhas por hora por e-mail, de qualquer IP, só em `login` e `recover` com chave errada. O `prelogin` e os acertos não contam no (b). Para travar a vítima, o atacante precisa errar 50 vezes por hora, sem parar. Mesmo assim, os aparelhos já ligados continuam sincronizando: o (b) só barra a entrada de um aparelho novo. É o custo aceito de ter um teto contra a força bruta distribuída. O e-mail de confirmação sai no máximo 3 vezes por hora para o mesmo endereço |
| D9 | 9. Token não expira | O token vence depois de 30 dias sem uso (`Retention:DeviceIdleDays = 30`), contados por `last_seen_at`, que já é atualizado uma vez por hora. A manutenção diária revoga os aparelhos vencidos. O app já trata o 401 como sessão encerrada e guarda a fila. Só o texto muda |
| D10 | Limite geral | Além do `/auth`: 600 pedidos por minuto por IP em todas as rotas, menos `/health`, e 300 por minuto por aparelho em `/sync`. A resposta é a `429 rate_limited` com `Retry-After`, a mesma de hoje |
| D11 | E-mail (obrigação da 9.6) | `SmtpEmailSender` com MailKit, exigindo TLS (`StartTls` ou `SslOnConnect`). `None` só é aceito em `Development`. O cadastro põe o envio numa fila em memória com capacidade 100 e responde `202` na hora. Um trabalhador envia, com 3 tentativas e espera crescente. Se as 3 falharem, ou se a fila estiver cheia, a conta pendente é apagada, como hoje: nunca fica conta sem e-mail. O log registra a falha com o e-mail mascarado e sem o código. O Resend vai no README como exemplo: `smtp.resend.com`, porta 587, usuário `resend` e a chave da API como senha |

**Configuração nova** (padrões no `appsettings.json`):

| Chave | Padrão | Observação |
|---|---|---|
| `ConnectionStrings:PostgresMigrations` | vazio (usa `Postgres`) | Fora de `Development`, é obrigatória e precisa ser diferente de `Postgres` |
| `Server:InviteCode` | vazio | Em `Development`, vazio deixa o cadastro aberto. Fora dele, vazio deixa o cadastro fechado |
| `Server:TrustedProxyNetworks` | vazio | Lista em CIDR |
| `Sync:MaxRecordsPerAccount` | 100000 | Um valor vazio desliga, só por decisão de quem opera. Um JSON `null` mantém 100000 |
| `Limits:AuthPerEmailPerIpPerMinute` | 10 | Substitui `AuthPerEmailPerMinute` |
| `Limits:LoginFailuresPerEmailPerHour` | 50 | |
| `Limits:ConfirmationEmailsPerAddressPerHour` | 3 | |
| `Limits:RequestsPerIpPerMinute` | 600 | |
| `Limits:SyncPerDevicePerMinute` | 300 | |
| `Retention:DeviceIdleDays` | 30 | Pelo menos 1, em qualquer ambiente: 0 revogaria todos os aparelhos |
| `Email:Mode` | `log` | `log` ou `smtp`. `log` só em `Development` |
| `Email:Smtp:Host`, `Port`, `Username`, `Password`, `From`, `Security` | vazio, 587, vazio, vazio, vazio, `StartTls` | Com `Mode = smtp`, `Host` e `From` são obrigatórios, `From` precisa ser um endereço com domínio e `Port` fica entre 1 e 65535; em produção, o servidor não sobe sem isso. A `Password` vem de `/run/secrets`. No Compose, `Host`, `Port`, `Username` e `From` vêm do `.env` (`SMTP_*`) e viram `Email__Smtp__*` |

**Respostas novas:** `403 registration_closed` no cadastro e `500 internal_error` (`{"error":"internal_error"}`) para qualquer exceção não tratada fora de `Development`, sem detalhe. No app, o primeiro vira "Este servidor não aceita cadastro sem um convite válido. Peça o código a quem cuida do servidor."

## 4. Comportamento no app

- **Endereço do servidor:** `https://` em qualquer endereço; `http://` só em `127.0.0.1`, `::1` e `localhost`. O erro de `http://` em outro endereço diz: "Use https://. O http:// só vale para 127.0.0.1." O texto de endereço inválido continua o mesmo.
- **Cadastro:** campo "Código de convite" opcional, abaixo da confirmação da senha, com a dica "Peça a quem cuida do servidor." O código vai em `invite_code` com as pontas aparadas; vazio vai como nulo.
- **Sessão encerrada:** o texto passa a citar os 30 dias: "A sessão deste aparelho foi encerrada (a senha mudou, o aparelho foi removido ou ficou 30 dias sem sincronizar). Entre de novo para continuar sincronizando. Nada do que está na fila se perde."

## 5. Como conferir à mão

1. `packaging/server/init-env.sh` cria `secrets/` (700, arquivos 644) e o `.env`, este só com valores que não são segredo. Em produção o servidor não sobe com o SMTP vazio: preencha `SMTP_HOST` e `SMTP_FROM` no `.env` (um host fictício basta para o teste local). Depois, `SLIMEGOALS_DOMAIN=localhost docker compose up -d --build --wait` sobe o banco, o servidor e o Caddy, todos saudáveis.
2. `curl -sk https://localhost/health` devolve 200. `curl -sI http://localhost/health` devolve 308 para `https`. `curl http://127.0.0.1:8080` não conecta.
3. `docker inspect` dos três contêineres não mostra nenhum segredo em `Env`.
4. `docker compose exec db psql -U slimegoals_app -d slimegoals -c 'CREATE TABLE x(i int)'` é recusado.
5. Com `compose.dev.yaml`, o fluxo de hoje continua: cadastro (com o convite de `secrets/Server__InviteCode`), código no log e confirmação no app.

## 6. Banco do servidor de desenvolvimento que já existe

As tabelas de hoje pertencem a `slimegoals`, o superusuário. `packaging/server/upgrade-roles.sh` cria os dois papéis e passa as tabelas para `slimegoals_owner`, sem apagar dados. Rodar `docker compose down -v` só com pedido explícito.

## 7. Notas de operação

- **E-mail na fila ao parar.** Os e-mails de confirmação ainda na fila quando o servidor para não são enviados. A conta pendente vence em 24 h e a limpeza diária a remove.
- **`ASPNETCORE_FORWARDEDHEADERS_ENABLED`.** Nunca defina como `true`: reativaria a confiança em qualquer proxy e anularia D3.
- **DataProtection.** Com a raiz somente leitura, o servidor loga avisos de chaves em memória. O servidor não usa nada que dependa delas (o token é próprio, sem cookie).
- **Porta 80.** O redirecionamento 308 do Caddy ainda traz `Server: Caddy`; só o site HTTPS remove o cabeçalho. Conhecido, de baixo impacto.
- **Só IPv4 até a 9.6.** O Caddy publica as portas só em `0.0.0.0`, e o domínio não deve ter registro `AAAA`. A rede `edge` não tem IPv6: num host com as duas pilhas, o Docker atenderia o IPv6 em `[::]`, e todo cliente IPv6 chegaria ao servidor como o gateway, num balde só do limite por IP. Bastariam poucos pedidos para travar a entrada de todos os clientes IPv6, o que fere a seção 2.

## 8. Obrigações para a 9.6

Antes de expor o servidor na 9.6, além do que a seção 2 deixa de fora:

1. **IPv6** (vale só para quem expuser o servidor na internet; na tailnet, a 9.6 não precisa dela: `specs/Publicacao_na_tailnet_v1.md`, seção 6). `enable_ipv6` na rede `edge`, com uma sub-rede ULA fixa; o Caddy com IP fixo nela, que entra em `Server:TrustedProxyNetworks`; e o limite por IP agrupando os endereços IPv6 por `/64`, porque um cliente IPv6 costuma controlar o `/64` inteiro. Só então publicar as portas em `[::]` e o registro `AAAA`.
2. **Sessão gravada com `http://` fora do loopback.** O motor de sincronização se recusa a partir com uma sessão gravada que aponte para `http://` fora de `127.0.0.1`, `::1` e `localhost`, e o app mostra "sessão encerrada". Sair continua possível. *Resolvida na 9.6 (T12 de `specs/Publicacao_na_tailnet_v1.md`).*
