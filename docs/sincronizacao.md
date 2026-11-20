# Sincronização (em desenvolvimento)

Com uma conta, as metas e os registros passam de um aparelho para outro, cifrados de ponta a ponta. Até a fase 9.6, isso existe só nos builds de desenvolvimento: no Linux, o Debug, ou qualquer build com `-p:SyncDev=true`; no Android, o APK Debug ou o de `build.sh --dev`. O app instalado (Release) não tem a seção de conta, nem cofre, nem rede.

No build de desenvolvimento, Configurações ganha no topo a seção **Conta e sincronização**: criar conta, confirmar, entrar, recuperar, trocar a senha, ver e remover aparelhos, sair e apagar a conta. O app continua funcionando sem conta.

## O que fica protegido

- **Cada registro é cifrado no aparelho antes de sair** (AES-256-GCM), com uma chave de dados aleatória criada no cadastro. A senha, a chave de dados e o conteúdo nunca vão ao servidor.
- **A chave de dados vai ao servidor cifrada duas vezes:** uma com uma chave derivada da senha (Argon2id), outra com uma chave derivada do código de recuperação, mostrado uma vez no cadastro. Um aparelho novo baixa essa cópia e a abre localmente.
- **Sem a senha e sem o código, a cópia da nuvem é irrecuperável.** Os dados de cada aparelho continuam nele.
- **A sessão fica no cofre do sistema:** o chaveiro (Secret Service) no Linux e o Android Keystore no Android. Sem cofre disponível, a sincronização não liga; ela nunca guarda a sessão num arquivo sem proteção.
- **O servidor ainda sabe** o e-mail, o IP, quantos aparelhos existem, quantos registros e de que tamanho, e quando cada mudança aconteceu.

As decisões, os vetores de teste e os limites estão em [specs/Sincronizacao_v1.md](../specs/Sincronizacao_v1.md), seção 6.

## No Linux

```bash
# 1. suba o servidor (veja docs/servidor.md, "Desenvolvimento")
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait

# 2. rode o app em Debug, com dados e configuração de teste
XDG_DATA_HOME=/tmp/sg-a/data XDG_CONFIG_HOME=/tmp/sg-a/config dotnet run --project src/SlimeGoals.Desktop
```

- **O servidor padrão é `http://127.0.0.1:8080`**, o do `compose.dev.yaml`. O endereço só aceita `https://`, exceto para `127.0.0.1`, `::1` e `localhost`. Para um servidor em produção, use o `https://` do seu domínio. O endereço só pode ser trocado com a conta desligada.
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

Com o app aberto, uma edição feita num chega ao outro em até uns 20 s.

## No Android

O APK de distribuição não tem rede. Para sincronizar, use um APK de desenvolvimento:

- o **Debug** (`-t:Install`, veja [Android](android.md#rodar-no-emulador)), só no emulador, porque é assinado com a chave de debug;
- o gerado por **`build.sh --dev`**, assinado com a sua chave, que instala por cima do app do celular sem perder os dados.

```bash
packaging/android/build.sh --dev                    # artifacts/slimegoals-<versão>-android-dev.apk
adb install -r artifacts/slimegoals-0.11.0-android-dev.apk
adb reverse tcp:8080 tcp:8080                       # o 127.0.0.1:8080 do celular passa a ser o do computador
```

- **O servidor continua preso ao `127.0.0.1` do computador**, e o celular chega a ele pelo cabo. Refaça o `adb reverse` sempre que reconectar o cabo ou reiniciar o celular.
- **O endereço no app é o padrão**, `http://127.0.0.1:8080`. O APK de desenvolvimento só aceita texto puro para `127.0.0.1` e `localhost`.
- **A sessão fica cifrada com uma chave do Android Keystore**, num arquivo privado do app que não vai para backup.
- **Para voltar ao app normal**, instale por cima o APK sem `-dev`. A seção some, e a sessão fica guardada, sem uso.

### Conferir a criptografia no aparelho

O APK de desenvolvimento calcula os vetores fixos da spec e faz uma ida e volta no cofre quando o `adb` pede. Só o `adb` e o sistema conseguem pedir:

```bash
adb shell am broadcast -a dev.alcm.slimegoals.VERIFICAR_CRIPTO -n dev.alcm.slimegoals/dev.alcm.slimegoals.CryptoSelfCheckReceiver
adb logcat -d -s SlimeGoals    # esperado: "cripto: 5/5 vetores conferem" e "cofre: ida e volta confere"
```
