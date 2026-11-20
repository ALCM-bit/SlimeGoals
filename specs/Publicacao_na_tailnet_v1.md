# Publicação na tailnet (v1)

> Aprovada pelo autor em 20/11/2026, depois de revisada por um leitor que não participou da conversa. Esta é a fase 9.6, no formato decidido pelo autor: o servidor numa VM própria, alcançável só pela tailnet do Tailscale e usado só pelos aparelhos dele. Vale junto com `specs/Sincronizacao_v1.md` e `specs/Endurecimento_do_servidor_v1.md`. Onde esta diverge delas, esta vale.

## 1. Para que serve

Hoje a sincronização só existe nos builds de desenvolvimento, e o servidor só roda no PC de desenvolvimento, preso ao 127.0.0.1. O autor quer que o app instalado no Linux e no Galaxy (Release) sincronize de verdade, por um servidor que fica numa VM Ubuntu dentro do Umbrel de casa.

O servidor é só para os aparelhos do autor. Por isso ele fica fora da internet: só quem está na tailnet do autor alcança a VM. O Tailscale já está instalado na VM, que entrou na tailnet como uma máquina própria. O autor descartou expor a VM na internet (domínio, porta aberta no roteador) e hospedar no Railway. As duas opções trariam uma superfície de ataque e um custo que um servidor de uso pessoal não precisa.

## 2. Objetivos e o que fica de fora

**Objetivos**
- O app de Release, no Linux e no Android, tem a seção de conta e sincroniza com o servidor da VM por `https://<máquina>.<tailnet>.ts.net`.
- O HTTPS usa o certificado da tailnet (`tailscale cert`). Um script com timer do systemd, na VM, pede e renova o certificado. O contêiner do Caddy só lê os arquivos.
- O Caddy publica a porta 443 só no IP da tailnet da VM. Nada escuta em `0.0.0.0` nem na rede de casa.
- O e-mail de confirmação do cadastro sai por SMTP de verdade, como na D11 da spec de endurecimento.
- O banco tem backup diário na VM, e restaurar o backup está documentado e foi testado uma vez.
- O motor cumpre a obrigação 2 da spec de endurecimento: uma sessão gravada com `http://` fora do loopback não sincroniza.
- Depois de reiniciar a VM, a pilha volta sozinha, com o certificado válido.

**Fica de fora**
- Expor o servidor na internet. O `compose.yaml` continua pronto para isso, com Caddy e Let's Encrypt, mas não é o caminho do autor agora.
- IPv6 (veja a seção 6).
- Política de privacidade, cobrança, grátis ou pago: não há usuários além do autor.
- Cópia do backup para fora da VM. Os aparelhos guardam os próprios dados localmente, e o servidor guarda só blobs cifrados. Fica como evolução.
- Sincronização em segundo plano no Android (decisão em aberto 3 da spec de sincronização).
- Implantação automática (CI que publica na VM). A atualização é manual, com `git pull` e `docker compose up --build`.
- Gerar um código de recuperação novo com a conta ligada, que a spec de sincronização deixou "para a 9.6". Não é preciso para publicar e é uma funcionalidade à parte (rota, tela e testes); fica para depois. **Chamada de quem escreveu.**

**Seria um fracasso se**
- O servidor ficasse alcançável de fora da tailnet, ou na rede de casa sem passar pela tailnet.
- O nome da tailnet, o IP da VM, um segredo ou a chave do certificado entrassem no repositório, que é público.
- O contêiner do Caddy ganhasse acesso ao `tailscaled` (socket) ou permissão de gravar o certificado.
- Todos os aparelhos chegassem ao servidor com o mesmo IP e dividissem um balde só no limite por IP.
- Atualizar o APK instalado apagasse os dados do celular. A chave de assinatura continua a mesma (`e88d…`), e a atualização usa `adb install -r`.
- O certificado vencesse sem ninguém perceber, e a sincronização parasse em silêncio.

## 3. Abordagens consideradas

Para o HTTPS, o problema é só o certificado: o Let's Encrypt precisa de domínio público e das portas 80 e 443 abertas na internet, o que a tailnet não tem. O Tailscale emite certificado válido para o nome `.ts.net` da máquina.

1. **Script e timer na VM, o Caddy só lê os arquivos. Escolhida pelo autor.** O contêiner do Caddy não ganha acesso nenhum ao Tailscale, só monta dois arquivos somente leitura. É o mínimo privilégio. O custo é uma peça fora do Docker, instalada uma vez.
2. **O Caddy pede o certificado direto ao `tailscaled`**, montando `/var/run/tailscale/tailscaled.sock`. Descartada: esse socket controla o Tailscale da VM, e o Caddy roda como root no contêiner. Um Caddy comprometido poderia mexer na ligação da VM com a tailnet.
3. **Contêiner do Tailscale no Compose**, com o Caddy na mesma rede dele. Descartada: muda a topologia de redes endurecida (IP fixo do Caddy, rede `edge`), traz uma chave de autenticação do Tailscale como segredo novo e duplica o Tailscale que já roda na VM.

Também foram descartados, antes desta escolha: o Tailscale Serve no Umbrel (o app do Umbrel não expõe a CLI, e o servidor dependeria do repasse do Umbrel); o repasse atual da porta 40000 pelo Umbrel (não se sabe se preserva o IP do cliente, e o certificado seria do Umbrel, não da VM); a CA interna do Caddy (o Android não confia em CA instalada pelo usuário dentro dos apps).

## 4. Decisões

| # | Assunto | Decisão |
|---|---|---|
| T1 | Compose | Novo `compose.tailnet.yaml`, aplicado por cima do `compose.yaml`: `docker compose -f compose.yaml -f compose.tailnet.yaml up -d --build --wait`. Ele troca, e não soma, as portas do Caddy: só `${TAILNET_IP}:443:443`, sem a porta 80. Monta um `Caddyfile.tailnet` e a pasta do certificado, os dois somente leitura. O resto do `compose.yaml` não muda: redes `edge` e `data`, IP fixo do Caddy `10.89.0.2`, proxy confiável, segredos em arquivo e endurecimento dos contêineres |
| T2 | `.env` | Entram `TAILNET_IP` (o IPv4 `100.x` da VM) e `TAILNET_CERT_DIR` (padrão `/etc/slimegoals/tls`). `SLIMEGOALS_DOMAIN` passa a ser o nome `.ts.net` completo da VM, que também vai para `AllowedHosts`. Os valores reais só existem no `.env` da VM. O `.env.example` traz marcadores |
| T3 | `Caddyfile.tailnet` | O mesmo site do `Caddyfile` (`header Strict-Transport-Security`, `header -Server`, `reverse_proxy server:8080`), com `tls` apontando para os dois arquivos montados. Sem ACME e sem redirecionamento da porta 80 |
| T4 | Certificado | `packaging/server/tailnet-cert.sh` roda como root na VM: `tailscale cert` para o nome em `SLIMEGOALS_DOMAIN`, gravando o `.crt` e o `.key` em `TAILNET_CERT_DIR` (o script cria a pasta com 700; uma pasta que já existia precisa ser do root, sem escrita para grupo e outros; arquivos 600, dono root; caminho absoluto com pelo menos dois componentes e fora das pastas do sistema). Se o conteúdo mudou, recarrega o Caddy (`caddy reload` dentro do contêiner). Se não mudou, não faz nada. Falha com saída diferente de zero e mensagem no journal, sem mostrar o conteúdo da chave. O reload usa `--force` e um marcador de pendência garante que o certificado novo chegue ao Caddy, inclusive quando o Docker ainda não subiu na partida (o script não chama o Docker parado: ele esperaria a si mesmo) |
| T5 | Timer | Unidades do systemd versionadas em `packaging/server/systemd/`: um serviço `oneshot` que roda o `tailnet-cert.sh` e um timer diário, com `Persistent=true` e atraso aleatório. O `.service` também roda antes da pilha subir na partida da VM. A renovação depende de o `tailscale cert` renovar quando falta pouco para vencer (conferir o comportamento atual na documentação do Tailscale ao construir) |
| T6 | Partida da VM | O Docker só pode publicar a porta no IP da tailnet depois de o `tailscaled` ter o IP. A pilha precisa voltar sozinha depois de reiniciar a VM, sem passo manual. Como garantir isso (ordem de unidades do systemd, espera pelo IP, ou `net.ipv4.ip_nonlocal_bind`) fica com quem constrói, com a conferência da seção 8 |
| T7 | IP do cliente | Cada aparelho precisa chegar ao Caddy com o próprio IP `100.x`, para o limite por IP (D3, D8, D10) funcionar. Se o Docker da VM entregar as conexões com o IP do gateway (proxy de usuário do Docker), o guia manda desligar o `userland-proxy` no `daemon.json`. A conferência está na seção 8 |
| T7a | Só pela tailnet | A regra de NAT do Docker para a porta publicada olha o destino (`TAILNET_IP:443`), não a interface de entrada: um aparelho da rede de casa que roteie `100.x` pelo IP local da VM chegaria ao Caddy sem passar pela tailnet. Uma regra na cadeia `DOCKER-USER` aceita a porta 443 do Caddy só vinda do `tailscale0` e descarta o resto. A regra é versionada em `packaging/server/` e instalada junto com as unidades do systemd, e sobrevive a reiniciar a VM e o Docker. **Chamada de quem escreveu**, para não depender de como a VM está ligada à rede de casa |
| T8 | Backup | `packaging/server/backup.sh`, com timer diário em `packaging/server/systemd/`: `pg_dump` em formato custom, pelo contêiner `db`, gravado em `/var/backups/slimegoals` (pasta 700, arquivos 600). Guarda os 14 mais recentes. O guia descreve como restaurar num volume novo, e a restauração foi testada uma vez na VM antes de fechar a fase |
| T9 | E-mail | SMTP de verdade, com a D11 como está. O provedor é escolha de quem opera e fica no `.env` e em `secrets/Email__Smtp__Password`. O guia mostra o Gmail com senha de app (`smtp.gmail.com`, 587, `StartTls`) e o Resend. Sem domínio próprio, o Resend só entrega para o e-mail da própria conta do Resend; confira a regra atual ao configurar. Nenhuma exceção ao `Email:Mode=log` fora de `Development` |
| T10 | Conta no Release | A seção de conta, o motor, o cofre e a sincronização passam a fazer parte do Release no Linux e no Android. O que hoje é `SYNC_DEV` se divide: a conta vale em todos os builds, e só os extras de desenvolvimento continuam no dev. No Android, esses extras são o `network_security_config_dev` (texto puro para `127.0.0.1` e `localhost`) e o `CryptoSelfCheckReceiver`. O Release declara `INTERNET` e usa o `network_security_config` sem texto puro nenhum. O `build.sh --dev` continua existindo para o APK de desenvolvimento |
| T11 | Endereço padrão | No Release, o campo do servidor começa vazio, com a dica `https://servidor.exemplo.ts.net`. Hoje o padrão é `http://127.0.0.1:8080`, que no Android Release nem conecta (sem texto puro). No build de desenvolvimento, o padrão continua `http://127.0.0.1:8080`. Nenhum nome de tailnet real entra no código |
| T12 | Sessão com `http://` | Obrigação 2 da spec de endurecimento. O motor se recusa a partir quando a sessão gravada aponta para `http://` fora do loopback, com a mesma regra de `ServerUrlRules` (`Uri.IsLoopback`: `127.0.0.0/8`, `::1` e `localhost`). O app mostra o estado de sessão encerrada. "Entrar de novo" não pode funcionar, porque a reentrada usa o servidor gravado na sessão e `RequireServer` recusa esse endereço; por isso, nesse caso o app mostra só "Sair", com o aviso de que a fila deste aparelho não será enviada. **Chamada de quem escreveu:** nenhuma sessão assim existe nos aparelhos do autor (os builds de desenvolvimento só usaram o loopback), e a regra existe para não sincronizar em texto puro, não para salvar uma fila que nunca existiu. Começa por teste que falha |
| T13 | Tailnet | A VM não é compartilhada com ninguém (o compartilhamento do Tailscale é de mão única: uma máquina compartilhada com o autor não alcança as dele). O guia manda conferir isso no painel. ACL do Tailscale fica opcional e fora do repositório |
| T13a | Nome público | O certificado do `tailscale cert` sai do Let's Encrypt e entra nos logs públicos de Certificate Transparency, então o nome `<máquina>.<tailnet>.ts.net` fica visível para quem procurar. Aceito pelo autor: ninguém fora da tailnet conecta. Mesmo assim, o nome e o IP reais continuam fora do repositório, por higiene |
| T13b | Porta 40000 do Umbrel | O autor desliga o repasse da porta 40000 do Umbrel para a VM. Com a VM na tailnet, ele não é mais necessário e seria uma segunda entrada que não passa pela tailnet. O guia registra esse passo |
| T15 | Aviso de sincronização parada | Decidido pelo autor. Com a conta ligada e sem nenhuma sincronização bem-sucedida há mais de 3 dias, o app mostra um aviso fora de Configurações, na tela Hoje, nos dois heads: "Não sincroniza há 3 dias. Confira a conexão com o servidor." Tocar no aviso abre a página da conta. O aviso some sozinho na próxima sincronização bem-sucedida e não pode ser dispensado. O texto não cita o Tailscale, porque o app não sabe como o servidor é alcançado. Cobre as falhas que param tudo em silêncio: o certificado que não renovou, a VM fora do ar, a VM fora da tailnet (por exemplo, quando vence a chave dela no Tailscale) e o Tailscale desligado no celular. "Sincronização bem-sucedida" é um ciclo que falou com o servidor sem erro, mesmo sem nada para enviar ou receber; quem constrói confere se o `LastSyncAt` de hoje já tem esse sentido e ajusta, se não tiver. O prazo de 3 dias é uma constante. Começa por teste que falha, e o layout é conferido na Render em várias larguras |
| T14 | Versão | A fase fecha com a versão 0.12.0, pacote Linux e APK assinado. O Galaxy recebe a atualização com `adb -s <serial> install -r`, sem desinstalar. Em 20/11/2026 o Galaxy está com a 0.11.0 Release, assinada com a chave `e88d…` e com dados desde 19/11 (conferido pelo certificado do APK instalado e pela data da primeira instalação), então o `install -r` vale |

## 5. Comportamento no app

- **Seção de conta no Release:** a mesma do build de desenvolvimento de hoje (9.5a e 9.5b): criar conta com convite, entrar, recuperar, aparelhos, sair com fila.
- **Endereço do servidor:** vazio no primeiro uso do Release, com a dica da T11. As regras de hoje continuam: `https://` em qualquer endereço; `http://` só no loopback. No Android Release, o `http://` no loopback é aceito pela tela, mas a conexão falha pela configuração de rede; não vale um texto novo para isso.
- **Sem a tailnet ligada:** o servidor não responde, e o app trata como servidor fora do ar. A fila guarda tudo e envia quando voltar.
- **Sincronização parada há mais de 3 dias:** aviso na tela Hoje (T15).
- **Sessão gravada com `http://` fora do loopback:** estado de sessão encerrada (T12).

## 6. IPv6

O Caddy publica só no IPv4 da tailnet. O MagicDNS responde também o IPv6 `fd7a:…` da VM, e um cliente que tente o IPv6 primeiro recebe recusa de conexão na hora e cai para o IPv4. A conferência da seção 8 confirma isso nos dois apps.

A obrigação 1 da spec de endurecimento (IPv6 com agrupamento por `/64`) continua valendo só para quem expuser o servidor na internet. Na tailnet, ela nem serviria: todos os IPv6 do Tailscale ficam no mesmo `fd7a:115c:a1e0::/48`, e agrupar por `/64` poria todos os aparelhos num balde só.

## 7. Operação na VM (guia em `docs/servidor.md`)

1. No painel do Tailscale: MagicDNS e HTTPS Certificates ligados (já estão).
2. Na VM: Tailscale (feito), Docker do repositório oficial (feito), clonar o repositório.
3. `packaging/server/init-env.sh`; preencher no `.env` o `SLIMEGOALS_DOMAIN`, o `TAILNET_IP` e o SMTP; gravar a senha SMTP em `secrets/`.
4. Instalar as unidades do systemd do certificado e do backup; rodar o certificado uma vez.
5. `docker compose -f compose.yaml -f compose.tailnet.yaml up -d --build --wait`.
6. No app, criar a conta com o convite de `secrets/Server__InviteCode`; o código chega por e-mail.
7. Atualizar: `git pull` e o mesmo `up --build`.
8. No Umbrel: desligar o repasse da porta 40000 para a VM (T13b).
9. Opcional: desligar o vencimento da chave da VM no painel do Tailscale (*Disable key expiry*). Sem isso, a VM sai da tailnet a cada 180 dias até alguém autenticá-la de novo; o aviso da T15 mostra quando isso acontece.

**No PC do autor:** o Tailscale avisa que o systemd-resolved e o NetworkManager estão mal integrados, e o MagicDNS pode não resolver o nome `.ts.net`. O guia aponta a correção (https://tailscale.com/s/resolved-nm). Isso é configuração da máquina, não do app.

**No Galaxy:** o app do Tailscale precisa ficar ligado para sincronizar.

## 8. Como conferir à mão

1. Da VM e de um aparelho da tailnet, `curl -s https://<nome>.ts.net/health` devolve 200 com certificado válido, sem `-k`.
2. Da rede de casa, pelo IP local da VM, nada responde em 80 nem em 443. Um aparelho da rede de casa com uma rota manual para o `100.x` da VM pelo IP local dela também não chega ao Caddy (T7a). De um aparelho fora da tailnet, o nome não resolve nem conecta.
3. Com o log de acesso do Caddy ligado só para esta conferência e desligado depois (o `Caddyfile.tailnet` não grava acessos, para não guardar IPs), dois aparelhos fazendo pedidos aparecem com os dois IPs `100.x` distintos, não com o `10.89.0.1`.
4. `docker inspect` não mostra segredo em `Env`; o contêiner do Caddy não tem o socket do Tailscale e monta o certificado só para leitura.
5. Rodar o serviço do certificado duas vezes seguidas: a segunda não recarrega o Caddy. Trocar a data de validade à força (ou apagar os arquivos e rodar de novo) recarrega, e o `/health` continua respondendo.
6. Reiniciar a VM: a pilha volta saudável sem passo manual.
7. Backup gerado com permissão 600; restaurado num volume novo, o servidor sobe com as contas e os registros.
8. Release no Linux e no Galaxy: criar conta (código pelo e-mail), entrar no segundo aparelho, edição chega nos dois sentidos. Galaxy com o Tailscale desligado: a fila guarda e envia ao religar. Como o Linux (x64) e o Galaxy (arm64) abrem o que o outro cifrou, isso também vale como a conferência da criptografia num celular de verdade, que o receiver de vetores (só no APK de desenvolvimento) nunca fez no Galaxy. **Chamada de quem escreveu.**
9. Sessão gravada com `http://` fora do loopback (montada em teste ou à mão): estado de sessão encerrada; "Sair" funciona.
10. Atualização do APK com `install -r`: a data da primeira instalação não muda e as metas continuam lá.
11. Aviso da T15: com o servidor parado e o relógio adiantado mais de 3 dias depois da última sincronização (em teste, com `FakeTimeProvider`; à mão, parando a pilha), o aviso aparece na tela Hoje nos dois apps, abre a conta ao tocar e some depois de uma sincronização bem-sucedida.
12. Depois de desligar o repasse da porta 40000, `<IP do Umbrel>:40000` não responde.

## 9. Documentos que mudam

- `docs/servidor.md`: nova seção "Na tailnet", com o guia da seção 7.
- `docs/sincronizacao.md` e `README.md`: o Release passa a ter a conta; o roteiro marca a 9.6. O README e a tela Sobre deixam de dizer que os dados não saem do aparelho (`specs/Sincronizacao_v1.md`, §7).
- `docs/android.md`: "as metas ficam só neste celular" passa a valer só sem conta; com conta, elas vão cifradas para o servidor.
- `specs/Sincronizacao_v1.md`: a linha da 9.6 e as decisões em aberto 1 e 2 apontam para esta spec.
- `specs/Endurecimento_do_servidor_v1.md`: a obrigação 2 resolvida aqui (T12); a 1 vale só para quem expuser na internet (seção 6).

## 10. Segurança

Antes de fechar a fase, uma revisão de segurança cobre o diff inteiro e a VM em funcionamento (a "nova auditoria" da 9.6). Foco: nada sensível em respostas, logs, telas ou no repositório; a chave do certificado só legível pelo root no host e pelo Caddy; nada publicado fora do IP da tailnet; o Release Android sem texto puro e sem o receiver de desenvolvimento.

## 11. O que fica com quem constrói

- Como garantir a ordem de partida na VM (T6).
- O formato exato das unidades do systemd e onde o script descobre a pasta do repositório.
- Como dividir o `SYNC_DEV` nos dois heads (símbolo novo, ou o Release passar a compilar a conta e só os extras ficarem sob `SYNC_DEV`), desde que o APK de desenvolvimento siga igual ao de hoje.
- Como o `compose.tailnet.yaml` substitui a lista de portas do Caddy (por exemplo, `!override` do Compose), conferido com `docker compose config` no CI.
