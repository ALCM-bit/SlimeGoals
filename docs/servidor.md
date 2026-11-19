# Servidor de sincronização

O servidor da fase 9 (`src/SlimeGoals.Server`, ASP.NET Core + PostgreSQL) só é usado pelo build de desenvolvimento do app (veja [Sincronização](sincronizacao.md)). O app instalado não fala com ele. Pré-requisito: Docker com o plugin Compose.

O desenho do protocolo está em [specs/Sincronizacao_v1.md](../specs/Sincronizacao_v1.md), e o do endurecimento em [specs/Endurecimento_do_servidor_v1.md](../specs/Endurecimento_do_servidor_v1.md). Onde os dois divergem, vale o do endurecimento.

## Segredos e configuração

O `packaging/server/init-env.sh` cria dois itens fora do repositório:

- **`secrets/`**, uma pasta com permissão 700 e um arquivo por segredo: as senhas dos três papéis do banco, as duas *connection strings*, o segredo do pré-login, o convite e a senha do SMTP (vazia até você preencher);
- **`.env`**, só com valores que não são segredo, como o domínio e o servidor SMTP.

Os segredos nunca vão em variável de ambiente: o servidor os lê como arquivos montados em `/run/secrets`. Para fazer o `.env` à mão, copie o `.env.example`.

## Desenvolvimento

```bash
packaging/server/init-env.sh
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait
curl -fsS http://127.0.0.1:8080/health
```

- **O `compose.dev.yaml` vai por cima do `compose.yaml`.** O servidor sobe em `Development`, com o e-mail simulado, e a API fica em `http://127.0.0.1:8080`, publicada só no seu computador. Não há Caddy, e o PostgreSQL não tem porta publicada. Não existe `compose.override.yaml`, de propósito: o modo de desenvolvimento nunca entra sem querer.
- **O cadastro pede o convite, mesmo em desenvolvimento.** Ele está em `secrets/Server__InviteCode`. Cole o valor no campo "Código de convite" do app.
- **O código de confirmação do cadastro aparece no log** (`docker compose -f compose.yaml -f compose.dev.yaml logs server`), ao lado do e-mail mascarado (`a***@exemplo.com`), nunca do endereço completo. A confirmação pede também a `auth_key` de quem cadastrou.
- **Use sempre `--build`.** Sem ele, o Compose reaproveita a imagem antiga do servidor, e o app novo pode não conversar com ela.
- **Os dados ficam no volume `slimegoals_pgdata`.** `docker compose down` mantém o volume; `docker compose down -v` apaga o banco.
- **As rotas, os limites e os códigos de erro** estão em [specs/Sincronizacao_v1.md](../specs/Sincronizacao_v1.md), seção 5.

### Banco de desenvolvimento de antes do endurecimento

O volume antigo tem tabelas de um superusuário. Faça nesta ordem, sem apagar dados:

1. `packaging/server/init-env.sh`. Se o `.env` for do formato antigo (tem `POSTGRES_PASSWORD`), ele avisa: troque o conteúdo pelo do `.env.example`. As duas variáveis antigas não são mais lidas.
2. `packaging/server/upgrade-roles.sh`. Cria os papéis `slimegoals_owner` e `slimegoals_app` e passa as tabelas para o dono. Pode rodar de novo sem problema. Ele recria o contêiner `db` com a configuração nova; o volume fica.
3. `docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait`.

## Produção

O `compose.yaml` sozinho sobe em `Production`: o Caddy na frente, com HTTPS automático (Let's Encrypt), o servidor e o PostgreSQL. Só o Caddy publica portas (80 e 443). O servidor e o banco rodam com sistema de arquivos somente leitura, sem capacidades extras e como usuário sem privilégio.

1. **DNS.** Aponte o domínio para a máquina só com registro `A` (IPv4), sem registro `AAAA`, e deixe as portas 80 e 443 livres para a internet. O Let's Encrypt precisa delas. O servidor atende só IPv4 até a fase 9.6 (veja as [observações](#observações)).
2. **Segredos.** `packaging/server/init-env.sh` cria `secrets/` e o `.env`. Edite o `.env` e ponha o seu domínio em `SLIMEGOALS_DOMAIN`.
3. **SMTP (obrigatório antes do `up`).** Em produção, o servidor se recusa a subir com o SMTP vazio. Preencha `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME` e `SMTP_FROM` no `.env` e grave a senha em `secrets/Email__Smtp__Password`. Exemplo com o [Resend](https://resend.com):

   ```bash
   # no .env
   SMTP_HOST=smtp.resend.com
   SMTP_PORT=587
   SMTP_USERNAME=resend
   SMTP_FROM=SlimeGoals <nao-responda@seudominio.com.br>
   ```

   A chave da API do Resend é a senha. Grave-a em `secrets/Email__Smtp__Password`, sem quebra de linha no fim: `printf '%s' 'CHAVE' > secrets/Email__Smtp__Password`, ou, melhor, com um editor, para a chave não ficar no histórico do terminal. O domínio do remetente precisa estar verificado no Resend. O e-mail exige TLS (`StartTls` na 587).
4. **Subir.**

   ```bash
   docker compose up -d --build --wait
   curl -fsS https://seudominio.com.br/health
   ```

5. **Convite.** O cadastro só aceita quem tem o convite, em `secrets/Server__InviteCode`. Passe-o só a quem você quer que crie conta. Para trocar, edite o arquivo e reinicie o servidor (`docker compose up -d --force-recreate server`).
6. **Atualizar.** `git pull` e `docker compose up -d --build --wait`. Um banco de antes do endurecimento passa antes pelo `upgrade-roles.sh` (veja [acima](#banco-de-desenvolvimento-de-antes-do-endurecimento)).

### Testar a produção no seu computador

Use `SLIMEGOALS_DOMAIN=localhost`: o Caddy usa a própria CA interna, e o navegador e o `curl` reclamam do certificado (use `curl -k`). O SMTP também precisa estar preenchido. Um host fictício basta para subir; o e-mail falha no envio.

## Observações

- **Proxy confiável.** O servidor só confia no cabeçalho `X-Forwarded-For` vindo do Caddy, pelo IP fixo dele na rede interna (`10.89.0.2`; a rede é `10.89.0.0/24`). O resto da rede, o gateway incluso, não é de confiança. **Nunca defina `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`:** isso faria o servidor confiar em qualquer proxy e desligaria o limite por IP.
- **E-mail de confirmação.** O código vai por e-mail e não aparece em log. Se o SMTP falhar 3 vezes, a conta pendente é apagada, e o log registra a falha com o e-mail mascarado. E-mails ainda na fila quando o servidor para não são enviados; a conta pendente vence em 24 h.
- **Token do aparelho.** Ele vence depois de 30 dias sem sincronizar; é preciso entrar de novo.
- **Avisos do `DataProtection`.** O log traz avisos sobre chaves na memória. São inofensivos: o servidor não usa nada que dependa delas.
- **`Server: Caddy`.** A resposta de redirecionamento da porta 80 ainda traz esse cabeçalho. É conhecido e só revela o proxy.
- **Só IPv4.** O Caddy publica as portas em `0.0.0.0`. Com IPv6, todo cliente IPv6 chegaria ao servidor com o mesmo endereço e dividiria um único limite por IP. Por isso, não crie registro `AAAA` até a 9.6.
- **Fica para a fase 9.6:** backup do banco, política de privacidade e a seção de conta no app de Release.
