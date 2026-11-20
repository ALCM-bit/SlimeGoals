# Servidor de sincronização

O servidor da fase 9 (`src/SlimeGoals.Server`, ASP.NET Core + PostgreSQL) é o que o app fala quando você liga a conta (veja [Sincronização](sincronizacao.md)). Há três jeitos de subi-lo: em desenvolvimento, no seu computador; [na tailnet](#na-tailnet), numa VM só sua, que é o caminho do autor; e [na internet](#produção), com Caddy e Let's Encrypt. Pré-requisito: Docker com o plugin Compose.

O desenho do protocolo está em [specs/Sincronizacao_v1.md](../specs/Sincronizacao_v1.md), o do endurecimento em [specs/Endurecimento_do_servidor_v1.md](../specs/Endurecimento_do_servidor_v1.md) e o da publicação na tailnet em [specs/Publicacao_na_tailnet_v1.md](../specs/Publicacao_na_tailnet_v1.md). Onde os documentos divergem, vale o mais novo: a tailnet vale sobre o endurecimento, e este sobre a sincronização.

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

## Na tailnet

O servidor roda numa VM Ubuntu que só a sua [tailnet](https://tailscale.com) alcança. Nada é aberto na internet nem no roteador: o Caddy publica só a porta 443, só no IP `100.x` da VM, com o certificado que o próprio Tailscale emite para o nome `.ts.net`. Um script com timer, fora do Docker, pede e renova o certificado, e o Caddy só o lê. Os exemplos abaixo usam `servidor.exemplo.ts.net` e `100.64.0.10`: troque pelos seus, e nunca ponha o nome nem o IP reais no repositório, que é público.

A VM precisa de Tailscale (já entrou na tailnet), Docker com o plugin Compose e `git`. Rode os comandos a partir da pasta do repositório.

### 1. Painel do Tailscale

No painel de administração, em **DNS**, ligue **MagicDNS** e **HTTPS Certificates**. Sem o HTTPS ligado, o `tailscale cert` falha.

### 2. Clonar e configurar

```bash
git clone https://github.com/ALCM-bit/SlimeGoals.git
cd SlimeGoals
packaging/server/init-env.sh
```

Edite o `.env` (fora do repositório) e preencha:

```bash
SLIMEGOALS_DOMAIN=servidor.exemplo.ts.net   # o nome .ts.net completo da VM; também vira o AllowedHosts
TAILNET_IP=100.64.0.10                      # o IPv4 100.x da VM (tailscale ip -4)
# TAILNET_CERT_DIR=/etc/slimegoals/tls      # opcional; é o padrão
```

Com o `compose.tailnet.yaml`, as variáveis `HTTP_PORT` e `HTTPS_PORT` são ignoradas: o Caddy publica só `${TAILNET_IP}:443`.

O SMTP é obrigatório, porque o código de confirmação do cadastro vai por e-mail. Preencha `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME` e `SMTP_FROM` e grave a senha em `secrets/Email__Smtp__Password`, sem quebra de linha no fim e de preferência com um editor, para ela não ficar no histórico do terminal. Duas opções:

- **Gmail, com senha de app.** A senha de app, gerada na conta Google, é a senha do SMTP. Use `SMTP_HOST=smtp.gmail.com`, `SMTP_PORT=587` e, em `SMTP_USERNAME` e `SMTP_FROM`, o seu endereço do Gmail. O servidor usa `StartTls`.
- **[Resend](https://resend.com).** `SMTP_HOST=smtp.resend.com`, `SMTP_PORT=587`, `SMTP_USERNAME=resend`, e a chave da API como senha. Sem domínio próprio verificado, o Resend só entrega para o e-mail da própria conta do Resend (confira a regra atual ao configurar); com domínio, ele precisa estar verificado.

### 3. Instalar as unidades e subir

```bash
sudo packaging/server/install-systemd.sh
docker compose -f compose.yaml -f compose.tailnet.yaml up -d --build --wait
curl -fsS https://servidor.exemplo.ts.net/health
```

O `install-systemd.sh` recusa se `TAILNET_IP`, `SLIMEGOALS_DOMAIN` ou a pasta do certificado estiverem inválidos, e instala, como root:

- `slimegoals-tls.service` e `slimegoals-tls.timer`: pedem o certificado (`tailscale cert`) para `SLIMEGOALS_DOMAIN` e o renovam todo dia. O serviço também roda antes do Docker na partida da VM, então a pilha volta sozinha, com o certificado válido, depois de reiniciar. A pasta do certificado fica com permissão 700 e os arquivos com 600, do root. Se o `tailscale cert` falhar, os arquivos atuais ficam intactos e o Caddy não recarrega. Os logs vão para o journal: `journalctl -u slimegoals-tls`.
- `slimegoals-firewall.service`: insere uma regra na cadeia `DOCKER-USER` do iptables que descarta a porta 443 do IP da tailnet quando a conexão não entra pelo `tailscale0`. Sem ela, um aparelho da rede de casa com uma rota manual para o `100.x` chegaria ao Caddy sem passar pela tailnet.
- `/etc/sysctl.d/60-slimegoals.conf` (`net.ipv4.ip_nonlocal_bind`): deixa o Docker publicar a porta no IP da tailnet mesmo se o `tailscaled` ainda não o tiver na partida.
- `slimegoals-backup.timer`: o [backup diário](#backup-e-restauração).

O `curl` não pode precisar de `-k`: o certificado é válido.

**Confira a regra do firewall** depois de instalar e depois de atualizar o Docker:

```bash
sudo iptables -S DOCKER-USER   # deve listar um -j DROP para o seu TAILNET_IP, porta 443, com ! -i tailscale0
```

Se a regra não puder ser instalada, a pilha sobe mesmo assim, e a porta fica protegida só por estar no IP da tailnet. Por isso a conferência é sua. A regra também bloqueia um contêiner que chame o nome público do servidor pela rede do Docker; hoje nenhum contêiner faz isso.

### 4. Criar a conta

No app (Release), em Configurações, **Conta e sincronização**: preencha o servidor com `https://servidor.exemplo.ts.net`, o e-mail, a senha e o convite de `secrets/Server__InviteCode`. O código de confirmação chega por e-mail. Veja [Sincronização](sincronizacao.md).

### 5. Atualizar

```bash
git pull
docker compose -f compose.yaml -f compose.tailnet.yaml up -d --build --wait
```

Nunca use `docker compose down -v` ao atualizar: ele apaga o banco. `down` sem `-v` mantém o volume.

Se a atualização mexeu nas unidades do systemd, rode o `sudo packaging/server/install-systemd.sh` de novo; ele pode repetir sem problema. Depois de atualizar o Docker em si, confira a regra do firewall (passo 3).

### Backup e restauração

O timer `slimegoals-backup.timer` roda o `packaging/server/backup.sh` todo dia. Ele faz um `pg_dump` em formato custom pelo contêiner `db`, grava `slimegoals-AAAAMMDD-HHMMSS.dump` em `/var/backups/slimegoals` (pasta 700, arquivos 600), confere o arquivo com `pg_restore -l` e guarda os 14 mais recentes. Para rodar à mão, ou para outra pasta: `sudo packaging/server/backup.sh [pasta]`. Os logs vão para o journal (`journalctl -u slimegoals-backup`).

O backup fica só na VM. Os aparelhos guardam os próprios dados, e o servidor guarda só blobs cifrados; copiar os arquivos para fora da VM fica por sua conta.

Os comandos abaixo supõem que o seu usuário está no grupo `docker`; sem isso, ponha `sudo` na frente de cada `docker`. O `sudo cat` é só para ler o dump, que é do root (600).

**O backup guarda só o banco.** Guarde uma cópia de `secrets/` e do `.env` fora da VM, num lugar seguro e só seu: sem eles, um volume novo não abre com os mesmos segredos. Se perder `secrets/`, o banco restaurado continua válido, mas as senhas dos papéis e o convite precisam ser refeitos.

**Conferir um backup** (sem tocar na pilha). Num PostgreSQL descartável, os papéis precisam existir antes, porque o dump dá permissões ao `slimegoals_app`:

```bash
docker run -d --name restore -e POSTGRES_PASSWORD=x postgres:18-alpine
docker exec restore psql -U postgres -c "CREATE ROLE slimegoals_owner NOLOGIN" -c "CREATE ROLE slimegoals_app NOLOGIN"
docker exec restore createdb -U postgres slimegoals
sudo cat /var/backups/slimegoals/slimegoals-AAAAMMDD-HHMMSS.dump | docker exec -i restore pg_restore -U postgres -d slimegoals
docker exec restore psql -U postgres -d slimegoals -Atc "select count(*) from accounts"
docker rm -f restore
```

**Restaurar depois de um desastre** (ensaiado numa pilha descartável: backup, `down -v`, restauração, `/health` 200, mesma contagem e tabelas do dono `slimegoals_owner`). Antes, tenha um backup conferido pelo passo acima. Este é o **único** caso em que se usa `docker compose down -v`, que apaga o volume do banco: ele só serve para recomeçar com um volume novo, e só depois de conferir o backup. A ordem importa: o banco sobe sozinho, para o servidor não criar as tabelas antes, e a restauração não usa `--no-owner`, para as tabelas continuarem do `slimegoals_owner` (os papéis são criados pela primeira partida do `db`).

```bash
F="-f compose.yaml -f compose.tailnet.yaml"
docker compose $F down -v                    # só aqui; apaga o banco atual
docker compose $F up -d --wait db            # volume novo: só o banco, com os papéis
sudo cat /var/backups/slimegoals/slimegoals-AAAAMMDD-HHMMSS.dump | docker compose $F exec -T db pg_restore -U postgres -d slimegoals --clean --if-exists
docker compose $F up -d --build --wait       # agora o servidor e o Caddy
curl -fsS https://servidor.exemplo.ts.net/health
docker compose $F exec -T db psql -U postgres -d slimegoals -Atc "select count(*) from accounts" -c "select tablename||' '||tableowner from pg_tables where schemaname='public'"
```

As contas devem aparecer, e todas as tabelas devem ser de `slimegoals_owner`.

### Outros passos na VM e no Umbrel

- **Desligue o repasse da porta 40000 do Umbrel** para a VM. Com a VM na tailnet, ele não é mais necessário e seria uma segunda entrada que não passa pela tailnet. Depois, `<IP do Umbrel>:40000` não pode responder.
- **Opcional: desligue o vencimento da chave da VM** no painel do Tailscale (*Disable key expiry*). Sem isso, a VM sai da tailnet a cada 180 dias, até alguém autenticá-la de novo; o aviso de "Não sincroniza há 3 dias" do app mostra quando isso acontece.
- **Confira que a VM não está compartilhada** com ninguém no painel. O compartilhamento do Tailscale é de mão única: uma máquina compartilhada com você não alcança as suas. Se a VM aparecer compartilhada, remova o compartilhamento.
- **ACL do Tailscale** é opcional e fica fora do repositório.
- **O nome do servidor é público.** O certificado do `tailscale cert` entra nos logs de Certificate Transparency, então o nome `.ts.net` aparece para quem procurar. Ninguém fora da tailnet conecta; mesmo assim, o nome fica fora do repositório.

### Se os IPs dos aparelhos não aparecerem

O limite por IP precisa ver o `100.x` de cada aparelho. Se todos chegarem como o gateway da rede do Docker (`10.89.0.1`), o proxy de usuário do Docker está no meio: desligue-o em `/etc/docker/daemon.json` com `{ "userland-proxy": false }` (junte à configuração que já existir), reinicie o Docker (`sudo systemctl restart docker`) e confira a regra do firewall de novo. O Caddyfile da tailnet não grava log de acesso, para não guardar IPs; para esta conferência, ligue o log por um momento e desligue depois.

### IPv6

O Caddy publica só no IPv4 da tailnet. O MagicDNS também responde o IPv6 `fd7a:…` da VM: um cliente que tente o IPv6 primeiro recebe recusa na hora e cai para o IPv4. A obrigação de IPv6 do endurecimento (limite por `/64`) vale só para quem expuser o servidor na internet.

### No seu computador e no celular

- **MagicDNS no PC.** Se o Tailscale avisar que o `systemd-resolved` e o NetworkManager estão mal integrados, o nome `.ts.net` pode não resolver. A correção está em <https://tailscale.com/s/resolved-nm>. É configuração do computador, não do app.
- **No celular, mantenha o app do Tailscale ligado.** Sem ele, o servidor não responde: a fila guarda tudo e envia quando a conexão volta.

## Produção

O `compose.yaml` sozinho sobe em `Production`: o Caddy na frente, com HTTPS automático (Let's Encrypt), o servidor e o PostgreSQL. Só o Caddy publica portas (80 e 443). O servidor e o banco rodam com sistema de arquivos somente leitura, sem capacidades extras e como usuário sem privilégio.

1. **DNS.** Aponte o domínio para a máquina só com registro `A` (IPv4), sem registro `AAAA`, e deixe as portas 80 e 443 livres para a internet. O Let's Encrypt precisa delas. O servidor atende só IPv4 (veja as [observações](#observações)).
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
- **Só IPv4.** O Caddy publica as portas em `0.0.0.0`. Com IPv6, todo cliente IPv6 chegaria ao servidor com o mesmo endereço e dividiria um único limite por IP. Por isso, não crie registro `AAAA`. Na [tailnet](#ipv6), isso não se aplica.
- **Fica para quem expuser o servidor na internet:** IPv6 com limite por `/64` (obrigação 1 do endurecimento) e política de privacidade. Na tailnet, o backup e a conta no Release já existem.
