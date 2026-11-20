# Sincronização

Com uma conta, as metas e os registros passam de um aparelho para outro, cifrados de ponta a ponta. A conta existe em todos os builds, inclusive no app instalado (Release), no Linux e no Android. O servidor fica numa VM sua, alcançável só pela sua tailnet (veja [Servidor, "Na tailnet"](servidor.md#na-tailnet)). O que o build de desenvolvimento (Debug, `-p:SyncDev=true` no Linux, `build.sh --dev` no Android) muda é pouco: o servidor padrão passa a ser `http://127.0.0.1:8080`, o Android aceita texto puro para o loopback (`network_security_config_dev`) e o APK ganha o `CryptoSelfCheckReceiver`.

Configurações ganha no topo a seção **Conta e sincronização**: criar conta, confirmar, entrar, recuperar, trocar a senha, ver e remover aparelhos, sair e apagar a conta. O app continua funcionando sem conta.

## O que fica protegido

- **Cada registro é cifrado no aparelho antes de sair** (AES-256-GCM), com uma chave de dados aleatória criada no cadastro. A senha, a chave de dados e o conteúdo nunca vão ao servidor.
- **A chave de dados vai ao servidor cifrada duas vezes:** uma com uma chave derivada da senha (Argon2id), outra com uma chave derivada do código de recuperação, mostrado uma vez no cadastro. Um aparelho novo baixa essa cópia e a abre localmente.
- **Sem a senha e sem o código, a cópia da nuvem é irrecuperável.** Os dados de cada aparelho continuam nele.
- **A sessão fica no cofre do sistema:** o chaveiro (Secret Service) no Linux e o Android Keystore no Android. Sem cofre disponível, a sincronização não liga; ela nunca guarda a sessão num arquivo sem proteção.
- **O servidor ainda sabe** o e-mail, o IP, quantos aparelhos existem, quantos registros e de que tamanho, e quando cada mudança aconteceu.

As decisões, os vetores de teste e os limites estão em [specs/Sincronizacao_v1.md](../specs/Sincronizacao_v1.md), seção 6.

## No app instalado (Release)

- **O campo do servidor começa vazio**, com a dica `https://servidor.exemplo.ts.net`. Só `https://` é aceito, exceto para `127.0.0.1`, `::1` e `localhost`. O endereço só pode ser trocado com a conta desligada.
- **O cadastro pede o convite** (`secrets/Server__InviteCode`) e o código de confirmação chega por e-mail.
- **Sem a tailnet ligada, o servidor não responde.** O app trata como servidor fora do ar: a fila guarda tudo e envia quando a conexão voltar. No celular, mantenha o app do Tailscale ligado.
- **Aviso na tela Hoje:** com a conta ligada e sem sincronizar há mais de 3 dias, aparece "Não sincroniza há 3 dias. Confira a conexão com o servidor." Tocar abre a conta; o aviso some na próxima sincronização bem-sucedida. Costuma ser o certificado que não renovou, a VM fora do ar ou da tailnet, ou o Tailscale desligado.
- **Sessão antiga com `http://` fora do loopback** (só existiria se gravada por um build antigo): a conta mostra "Sessão sem https" e só oferece **Sair**, porque o token iria em texto puro. A fila deste aparelho não é enviada.

## No Linux, em desenvolvimento

```bash
# 1. suba o servidor (veja docs/servidor.md, "Desenvolvimento")
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait

# 2. rode o app em Debug, com dados e configuração de teste
XDG_DATA_HOME=/tmp/sg-a/data XDG_CONFIG_HOME=/tmp/sg-a/config dotnet run --project src/SlimeGoals.Desktop
```

- **O servidor padrão é `http://127.0.0.1:8080`**, o do `compose.dev.yaml`. O endereço só aceita `https://`, exceto para `127.0.0.1`, `::1` e `localhost`. Para outro servidor, use o `https://` dele. O endereço só pode ser trocado com a conta desligada.
- **Ao criar a conta, preencha o "Código de convite"** (`secrets/Server__InviteCode`). O código de confirmação não chega por e-mail: ele aparece no log do servidor.

  ```bash
  docker compose -f compose.yaml -f compose.dev.yaml logs server
  ```

- **A sessão fica no chaveiro do sistema** (por exemplo, o gnome-keyring), separada por banco de dados.

### Dois aparelhos no mesmo computador

Rode duas cópias, cada uma com o seu `XDG_DATA_HOME` e o seu `XDG_CONFIG_HOME`, e entre com a mesma conta nas duas. Cada banco tem a sua instância e o seu segredo no chaveiro. O `XDG_CONFIG_HOME` próprio evita que ligar "Iniciar com o sistema" numa cópia de teste grave a entrada de autostart de verdade.

```bash
XDG_DATA_HOME=/tmp/sg-a/data XDG_CONFIG_HOME=/tmp/sg-a/config dotnet run --project src/SlimeGoals.Desktop
XDG_DATA_HOME=/tmp/sg-b/data XDG_CONFIG_HOME=/tmp/sg-b/config dotnet run --project src/SlimeGoals.Desktop   # em outro terminal
```

Uma edição sai do aparelho uns 5 s depois de feita, mas o outro só a busca no próximo ciclo dele. O app sincroniza ao abrir, 5 s depois de uma mudança local e a cada 15 min com o app aberto; no Android, também ao voltar ao primeiro plano. No Linux, ganhar foco não dispara um ciclo, e não há botão de sincronizar. Para ver a mudança no outro aparelho sem esperar os 15 min, feche e abra o app ou altere algo nele. As regras estão na [spec](../specs/Sincronizacao_v1.md), §4, "Quando roda".

## No Android

O APK de distribuição (Release) tem a conta e a permissão `INTERNET`, sem texto puro: ele só fala `https://`, com o servidor da tailnet. Para testar contra o servidor de desenvolvimento no seu computador (`http://127.0.0.1:8080`), use um APK de desenvolvimento:

- o **Debug** (`-t:Install`, veja [Android](android.md#rodar-no-emulador)), só no emulador, porque é assinado com a chave de debug;
- o gerado por **`build.sh --dev`**, assinado com a sua chave, que instala por cima do app do celular sem perder os dados.

```bash
packaging/android/build.sh --dev                    # artifacts/slimegoals-<versão>-android-dev.apk
adb -s <serial> install -r artifacts/slimegoals-0.12.0-android-dev.apk
adb -s <serial> reverse tcp:8080 tcp:8080                       # o 127.0.0.1:8080 do celular passa a ser o do computador
```

- **O servidor continua preso ao `127.0.0.1` do computador**, e o celular chega a ele pelo cabo. Refaça o `adb reverse` sempre que reconectar o cabo ou reiniciar o celular.
- **O endereço no app é o padrão**, `http://127.0.0.1:8080`. O APK de desenvolvimento só aceita texto puro para `127.0.0.1` e `localhost`.
- **A sessão fica cifrada com uma chave do Android Keystore**, num arquivo privado do app que não vai para backup.
- **Para voltar ao app normal**, instale por cima o APK sem `-dev` (`adb -s <serial> install -r`). A conta continua, mas o endereço `http://127.0.0.1:8080` deixa de funcionar sem texto puro: saia da conta e entre no servidor `https://`.

### Conferir a criptografia no aparelho

O APK de desenvolvimento (só ele tem o receiver) calcula os vetores fixos da spec e faz uma ida e volta no cofre quando o `adb` pede. Só o `adb` e o sistema conseguem pedir:

```bash
adb -s <serial> shell am broadcast -a dev.alcm.slimegoals.VERIFICAR_CRIPTO -n dev.alcm.slimegoals/dev.alcm.slimegoals.CryptoSelfCheckReceiver
adb -s <serial> logcat -d -s SlimeGoals    # esperado: "cripto: 5/5 vetores conferem" e "cofre: ida e volta confere"
```
