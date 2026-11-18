# SlimeGoals

Gestor de metas pessoais com lembretes, para **Linux (GNOME)** e **Android**.
Você cadastra hábitos diários ou metas semanais, recebe lembretes até cumpri-las,
registra o progresso com um check-in e acompanha relatórios semanais e mensais. Funciona offline,
com os dados guardados localmente em SQLite.

![Tela Hoje no desktop, com o slime e as metas do dia](docs/images/hoje-desktop.png)

> **Status:** em desenvolvimento. O app funciona no Linux, com pacote de instalação, e no Android, onde já dá para
> gerar um APK assinado ([Instalar no Android](#instalar-no-android)). O autor já testou num celular de verdade a instalação, as notificações e os lembretes, inclusive depois de religar o aparelho. Veja o [roteiro](#roteiro).

## Funcionalidades do MVP

- **Dois tipos de meta:** diária (alvo por dia nos dias escolhidos) e semanal (alvo por semana, de segunda a domingo)
- **Três medidas por meta:** conclusão, quantidade ou tempo (com cronômetro)
- **Lembretes** dentro de uma janela de horário, com a ação da medida (**Feito**, **+1** ou **Iniciar**), **Adiar** e **Ignorar** direto na notificação do sistema
- **Tela Hoje** com o progresso do período atual e registro rápido
- **Cronômetro à vista fora do app:** numa janela pequena por cima das outras no Linux e numa notificação fixa no Android, com o botão de parar
- **Slime** no Hoje, que muda de pose e de frase conforme o andamento do dia
- **Relatórios** semanais e mensais, com sequências em dias ou semanas
- **Arquivamento** sem perda de histórico; tema escuro roxo

## Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10 (LTS) |
| UI | Avalonia 12 + CommunityToolkit.Mvvm |
| Dados | SQLite (Microsoft.Data.Sqlite + Dapper), migrações por `PRAGMA user_version` |
| Testes | xUnit v3 (Microsoft Testing Platform) + AwesomeAssertions + `FakeTimeProvider` |

## Arquitetura

Hexagonal pragmática: o **Core** concentra domínio e regras e não depende de nada. Os demais
projetos implementam suas portas, e a composição (injeção de dependência) acontece só nos *heads*.

```
SlimeGoals.Desktop   SlimeGoals.Android
         └─────────┬─────────┘
                   ▼
            SlimeGoals.App          Views + ViewModels Avalonia
                   │
                   ▼
            SlimeGoals.Core         domínio, serviços, interfaces
                   ▲
       ┌───────────┴──────────────┐
SlimeGoals.Infrastructure   SlimeGoals.Platform.Linux
(SQLite, repositórios)      (caminhos XDG, notificações D-Bus, agendador,
                             autostart, instância única)
```

O head Android implementa as portas de lembretes direto no projeto (`AlarmManager`, notificações, permissões).
Um teste de arquitetura garante que o Core não referencia UI, banco ou plataforma.

```
src/
  SlimeGoals.Core/             Domain/, Services/, Validation/, Abstractions/
  SlimeGoals.Infrastructure/   Data/ (conexão, migrações SQL, repositórios)
  SlimeGoals.Platform.Linux/   Notifications/, Scheduling/, Autostart/, Power/, SingleInstance/
  SlimeGoals.App/              Views/, ViewModels/, Styles/ (tokens do tema), Assets/Fonts/
  SlimeGoals.Desktop/          Program.cs, CompositionRoot.cs
  SlimeGoals.Android/          MainActivity, AndroidCompositionRoot, Reminders/
tests/                         testes do Core, Infrastructure, App e Platform.Linux; SlimeGoals.Testing (falsos)
tools/SlimeGoals.Render/       grava PNGs das telas sem abrir o app
packaging/linux/               pacote .tar.gz e instalador
packaging/android/             APK assinado (build.sh) e criação da chave (make-keystore.sh)
src/SlimeGoals.Server/         servidor de sincronização (ASP.NET Core + PostgreSQL), fora do app
src/SlimeGoals.Sync/           motor de sincronização; SlimeGoals.Sync.Contracts: contratos do protocolo
specs/                         especificação, protótipos, planos e conclusões de spikes
```

## Como rodar

**Pré-requisito:** SDK do .NET 10 (`dotnet --version` ≥ 10.0.113; no Arch/CachyOS: `sudo pacman -S dotnet-sdk`).

```bash
git clone https://github.com/ALCM-bit/SlimeGoals.git
cd SlimeGoals

dotnet build SlimeGoals.slnx
dotnet test --solution SlimeGoals.slnx
dotnet run --project src/SlimeGoals.Desktop
```

Os dados ficam em `$XDG_DATA_HOME/SlimeGoals/slimegoals.db` (padrão: `~/.local/share/SlimeGoals/slimegoals.db`).
Para testar sem tocar nos seus dados, aponte para outra pasta:

```bash
XDG_DATA_HOME=/tmp/slimegoals-dev dotnet run --project src/SlimeGoals.Desktop
```

## Rodar no Android (desenvolvimento)

O head Android fica fora de `SlimeGoals.slnx` (o `dotnet test` do dia a dia não exige o workload `android`); ele entra em `SlimeGoals.Android.slnx`.

**Pré-requisitos** (os do spike): workload `android` do .NET, JDK 21, Android SDK em `~/Android/Sdk` e o AVD `slimegoals-api36`.

```bash
# compilar
dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk

# iniciar o emulador (em outro terminal)
~/Android/Sdk/emulator/emulator -avd slimegoals-api36

# instalar o APK Debug no emulador
dotnet build src/SlimeGoals.Android -t:Install -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk

# abrir o app (ou toque no ícone do slime)
~/Android/Sdk/platform-tools/adb shell monkey -p dev.alcm.slimegoals 1
```

Os dados ficam no armazenamento privado do app (`<FilesDir>/slimegoals.db`); desinstalar o app apaga tudo. O app não entra no backup do Android nem na transferência entre aparelhos: as metas ficam só neste celular.

**Lembretes:** o app precisa de duas permissões, que o Android 16 traz negadas num app recém-instalado: notificações e alarmes exatos. O aviso na tela Hoje oferece "Permitir" para as duas (Configurações mostra o estado de cada uma). Sem alarmes exatos os lembretes seguem, mas podem atrasar alguns minutos.

Comandos `adb` úteis (`adb` em `~/Android/Sdk/platform-tools`):

```bash
# notificações
adb shell pm grant dev.alcm.slimegoals android.permission.POST_NOTIFICATIONS

# alarmes exatos (use --uid; a forma por pacote não tem efeito na API 36)
adb shell appops set --uid dev.alcm.slimegoals SCHEDULE_EXACT_ALARM allow   # ou deny

# alarme armado
adb shell dumpsys alarm | grep -A3 slimegoals

# encerrar o processo como o sistema faz (o alarme continua; force-stop o cancela)
adb shell am kill dev.alcm.slimegoals
```

Para conferir os layouts sem emulador, `dotnet run --project tools/SlimeGoals.Render -- /tmp/render [phone|desktop|all]` grava PNGs de cada página em 412×915 (celular) e 1100×720 (desktop).

## Servidor de sincronização

O servidor da fase 9 (`src/SlimeGoals.Server`, ASP.NET Core + PostgreSQL) só é usado pelo build de desenvolvimento (veja [Sincronização (em desenvolvimento)](#sincronização-em-desenvolvimento)); o app instalado não fala com ele. Pré-requisito: Docker com o plugin Compose. O desenho do endurecimento está em [specs/Endurecimento_do_servidor_v1.md](specs/Endurecimento_do_servidor_v1.md).

O `init-env.sh` cria dois itens fora do repositório: `secrets/` (pasta 700, um arquivo por segredo) e `.env` (só valores que não são segredo). Os segredos nunca vão em variável de ambiente. Para fazer o `.env` à mão, copie o `.env.example`.

### Desenvolvimento

```bash
packaging/server/init-env.sh
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait
curl -fsS http://127.0.0.1:8080/health
```

- O `compose.dev.yaml` vai por cima do `compose.yaml`: o servidor sobe em `Development`, com o e-mail simulado, e a API fica em `http://127.0.0.1:8080`, publicada só no seu computador. Não há Caddy. O PostgreSQL não tem porta publicada. Não existe `compose.override.yaml`, de propósito: o modo de desenvolvimento nunca entra sem querer.
- O cadastro pede o convite, mesmo em desenvolvimento: ele está em `secrets/Server__InviteCode`. Cole o valor no campo "Código de convite" do app.
- O código de confirmação do cadastro aparece no log (`docker compose -f compose.yaml -f compose.dev.yaml logs server`), ao lado do e-mail mascarado (`a***@exemplo.com`), nunca do endereço completo. A confirmação pede também a `auth_key` de quem cadastrou.
- Use sempre `--build`: sem ele, o Compose reaproveita a imagem antiga do servidor, e o app novo pode não conversar com ela.
- Os dados ficam no volume `slimegoals_pgdata`. `docker compose down` mantém o volume; `docker compose down -v` apaga o banco.
- As rotas, os limites e os códigos de erro estão em [specs/Sincronizacao_v1.md](specs/Sincronizacao_v1.md), seção 5.

**Já tem um banco de desenvolvimento de antes do endurecimento?** O volume antigo tem tabelas de um superusuário. Faça nesta ordem, sem apagar dados:

1. `packaging/server/init-env.sh`. Se o `.env` for do formato antigo (tem `POSTGRES_PASSWORD`), ele avisa: troque o conteúdo pelo do `.env.example`. As duas variáveis antigas não são mais lidas.
2. `packaging/server/upgrade-roles.sh`. Cria os papéis `slimegoals_owner` e `slimegoals_app` e passa as tabelas para o dono. Pode rodar de novo sem problema. Ele recria o contêiner `db` com a configuração nova; o volume fica.
3. `docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait`.

### Produção

O `compose.yaml` sozinho sobe em `Production`: o Caddy na frente, com HTTPS automático (Let's Encrypt), o servidor e o PostgreSQL. Só o Caddy publica portas (80 e 443). O servidor e o banco rodam com sistema de arquivos somente leitura, sem capacidades extras e como usuário sem privilégio.

1. **DNS.** Aponte o domínio para a máquina e deixe as portas 80 e 443 livres para a internet. O Let's Encrypt precisa delas.
2. **Segredos.** `packaging/server/init-env.sh` cria `secrets/` e o `.env`. Edite o `.env` e ponha o seu domínio em `SLIMEGOALS_DOMAIN`.
3. **SMTP (obrigatório antes do `up`).** Em produção o servidor se recusa a subir com o SMTP vazio. Preencha `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME` e `SMTP_FROM` no `.env` e grave a senha em `secrets/Email__Smtp__Password`. Exemplo com o [Resend](https://resend.com):

   ```bash
   # no .env
   SMTP_HOST=smtp.resend.com
   SMTP_PORT=587
   SMTP_USERNAME=resend
   SMTP_FROM=SlimeGoals <nao-responda@seudominio.com.br>
   ```

   A chave da API do Resend é a senha: grave-a em `secrets/Email__Smtp__Password`, sem quebra de linha no fim (`printf '%s' 'CHAVE' > secrets/Email__Smtp__Password`; prefira um editor, para a chave não ficar no histórico do terminal). O domínio do remetente precisa estar verificado no Resend. O e-mail exige TLS (`StartTls` na 587).
4. **Subir.**

   ```bash
   docker compose up -d --build --wait
   curl -fsS https://seudominio.com.br/health
   ```

5. **Convite.** O cadastro só aceita quem tem o convite, em `secrets/Server__InviteCode`. Passe-o só a quem você quer que crie conta. Para trocar, edite o arquivo e reinicie o servidor (`docker compose up -d --force-recreate server`).
6. **Atualizar.** `git pull` e `docker compose up -d --build --wait`. Um banco de antes do endurecimento passa antes pelo `upgrade-roles.sh` (veja acima).

Para testar a produção no seu computador, use `SLIMEGOALS_DOMAIN=localhost`: o Caddy usa a própria CA interna (o navegador e o `curl` reclamam do certificado; use `curl -k`). O SMTP também precisa estar preenchido; um host fictício basta para subir, e o e-mail falha no envio.

Observações:

- O servidor só confia no cabeçalho `X-Forwarded-For` vindo do Caddy (a rede interna fixa, `10.89.0.0/24`). **Nunca defina `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`:** isso faria o servidor confiar em qualquer proxy e desligaria o limite por IP.
- O código de confirmação do cadastro vai por e-mail e não aparece em log. Se o SMTP falhar 3 vezes, a conta pendente é apagada, e o log registra a falha com o e-mail mascarado. E-mails ainda na fila quando o servidor para não são enviados; a conta pendente vence em 24 h.
- O token do aparelho vence depois de 30 dias sem sincronizar; é preciso entrar de novo.
- O log do servidor traz avisos do `DataProtection` sobre chaves na memória. São inofensivos: o servidor não usa nada que dependa delas.
- A resposta de redirecionamento da porta 80 ainda traz `Server: Caddy`. É conhecido e só revela o proxy.
- Backup do banco, política de privacidade e a seção de conta no app de Release continuam na fase 9.6.

**Os testes do servidor usam Docker.** Os testes de integração sobem um PostgreSQL de verdade com Testcontainers, então `dotnet test --solution SlimeGoals.slnx` agora precisa do Docker rodando. Se o Testcontainers falhar ao subir o contêiner auxiliar (ryuk), por exemplo por uma credencial antiga do Docker, baixe as imagens à mão e rode de novo:

```bash
docker pull postgres:18-alpine
docker pull testcontainers/ryuk:0.14.0   # use a versão que o erro indicar
```

**Criptografia e conta (fase 9.4).** `src/SlimeGoals.Sync` já cifra cada registro antes de ele sair do aparelho e tem o serviço de conta: cadastro com confirmação dentro do app, entrada, troca de senha, recuperação pelo código, sair e apagar a conta. A senha, a chave de dados e o conteúdo nunca vão ao servidor. No Linux, a sessão fica no chaveiro do sistema (Secret Service); sem chaveiro, a sincronização não liga. Na 9.5a, o build de desenvolvimento do Linux passa a usar tudo isso pela seção "Conta e sincronização" de Configurações. As decisões e os vetores de teste estão em [specs/Sincronizacao_v1.md](specs/Sincronizacao_v1.md), seção 6.

**Os testes do chaveiro precisam do `dbus-daemon` e do `gnome-keyring-daemon`.** Eles sobem um barramento e um chaveiro isolados, num diretório temporário, sem tocar no seu chaveiro. Sem esses programas, os testes são ignorados, não reprovados.

## Sincronização (em desenvolvimento)

No build de desenvolvimento do Linux (Debug, ou qualquer build com `-p:SyncDev=true`), Configurações ganha, no topo, a seção **Conta e sincronização**: criar conta, confirmar, entrar, recuperar, trocar a senha, ver e remover aparelhos, sair e apagar a conta. O app instalado (Release, `packaging/linux/build.sh`) não tem a seção, nem cofre, nem rede. No Android, vale o mesmo: só o APK de desenvolvimento sincroniza (veja "No Android", abaixo).

```bash
# 1. suba o servidor (veja "Servidor de sincronização > Desenvolvimento")
docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait

# 2. rode o app em Debug, com dados e configuração de teste
XDG_DATA_HOME=/tmp/sg-a/data XDG_CONFIG_HOME=/tmp/sg-a/config dotnet run --project src/SlimeGoals.Desktop
```

- O servidor padrão é `http://127.0.0.1:8080`, o do `compose.dev.yaml`. O endereço só aceita `https://`, exceto para `127.0.0.1`, `::1` e `localhost`. Para um servidor em produção, use o `https://` do seu domínio. O endereço só pode ser trocado com a conta desligada.
- Ao criar a conta, preencha o "Código de convite" (`secrets/Server__InviteCode`). O código de confirmação não chega por e-mail: ele aparece no log do servidor.

  ```bash
  docker compose -f compose.yaml -f compose.dev.yaml logs server
  ```

- A sessão fica no chaveiro do sistema (Secret Service, por exemplo o gnome-keyring), separada por banco de dados. Sem chaveiro, a sincronização não liga.
- **Dois aparelhos no mesmo computador:** rode duas cópias, cada uma com o seu `XDG_DATA_HOME` e o seu `XDG_CONFIG_HOME`, e entre com a mesma conta nas duas. Cada banco tem a sua instância e o seu segredo no chaveiro. O `XDG_CONFIG_HOME` próprio evita que ligar "Iniciar com o sistema" numa cópia de teste grave a entrada de autostart de verdade.

  ```bash
  XDG_DATA_HOME=/tmp/sg-a/data XDG_CONFIG_HOME=/tmp/sg-a/config dotnet run --project src/SlimeGoals.Desktop
  XDG_DATA_HOME=/tmp/sg-b/data XDG_CONFIG_HOME=/tmp/sg-b/config dotnet run --project src/SlimeGoals.Desktop   # em outro terminal
  ```

  Com o app aberto, uma edição feita num chega ao outro em até uns 20 s.

### No Android

O APK de distribuição não tem rede. Para sincronizar, use o APK de desenvolvimento: o Debug (`-t:Install`, só no emulador, porque é assinado com a chave de debug) ou o gerado por `build.sh --dev`, assinado com a sua chave, que instala por cima do app do celular sem perder os dados.

```bash
packaging/android/build.sh --dev                    # artifacts/slimegoals-<versão>-android-dev.apk
adb install -r artifacts/slimegoals-0.10.0-android-dev.apk
adb reverse tcp:8080 tcp:8080                       # o 127.0.0.1:8080 do celular passa a ser o do computador
```

- O servidor continua preso ao `127.0.0.1` do computador; o celular chega a ele pelo cabo. Refaça o `adb reverse` sempre que reconectar o cabo ou reiniciar o celular.
- O endereço no app é o padrão, `http://127.0.0.1:8080`. O APK de desenvolvimento só aceita texto puro para `127.0.0.1` e `localhost`.
- A sessão fica cifrada com uma chave do Android Keystore, num arquivo privado do app que não vai para backup.
- Para voltar ao app normal, instale por cima o APK sem `-dev`. A seção some, e a sessão fica guardada, sem uso.
- **Conferir a criptografia no aparelho:** o APK de desenvolvimento calcula os vetores fixos da spec e faz uma ida e volta no cofre quando o `adb` pede (só o `adb` e o sistema conseguem pedir):

  ```bash
  adb shell am broadcast -a dev.alcm.slimegoals.VERIFICAR_CRIPTO -n dev.alcm.slimegoals/dev.alcm.slimegoals.CryptoSelfCheckReceiver
  adb logcat -d -s SlimeGoals    # esperado: "cripto: 5/5 vetores conferem" e "cofre: ida e volta confere"
  ```

## Instalar no Android

O APK é assinado com uma chave sua, que fica fora do repositório. Ele traz as arquiteturas arm64 (celulares) e x64 (emulador) e exige Android 8.0 ou mais novo (API 26). Pré-requisitos: os de [Rodar no Android](#rodar-no-android-desenvolvimento).

1. **Crie a chave uma vez** e guarde uma cópia dela num lugar seguro:

   ```bash
   packaging/android/make-keystore.sh
   ```

   O script pede uma senha e grava a chave em `~/.android-keystores/slimegoals.keystore` (ou onde `SLIMEGOALS_KEYSTORE` apontar); ele não sobrescreve uma chave que já existe. **Perder a chave, ou a senha, impede atualizar o app instalado.**

2. **Gere o APK:**

   ```bash
   packaging/android/build.sh
   ```

   O script pede a senha da chave (ela não fica no histórico do shell). Para uma chave em outro lugar, aponte `SLIMEGOALS_KEYSTORE`; sem terminal (CI, por exemplo), defina `SLIMEGOALS_KEYSTORE_PASS`. O SDK do Android e o JDK vêm de `ANDROID_HOME` e `JAVA_HOME`, com padrão `~/Android/Sdk` e `/usr/lib/jvm/java-21-openjdk`.

   O resultado é `artifacts/slimegoals-0.10.0-android.apk`. Com `--dev`, o script gera `artifacts/slimegoals-0.10.0-android-dev.apk`, com a sincronização em desenvolvimento (veja [No Android](#no-android)).

3. **Instale no celular:** com a depuração USB ligada, `adb install artifacts/slimegoals-0.10.0-android.apk`; ou copie o APK para o aparelho e abra-o, permitindo "instalar apps desconhecidos" para o app que o abriu.

4. **Primeira abertura:** no aviso do Hoje, toque em "Permitir" (notificações e alarmes exatos). Depois, em Configurações → Bateria, toque em "Liberar" e aceite: sem isso o sistema pode adiar ou suspender os lembretes com o celular parado. Liberar a bateria faz "Alarmes exatos" aparecer como permitido, porque o Android trata assim os apps sem restrição de bateria; se você tirar o app dessa isenção, o bloqueio volta.

5. **No Samsung (One UI)**, confira também:
   - Configurações → Aplicativos → SlimeGoals → Bateria → "Sem restrições";
   - Configurações → Bateria → Limites de uso em segundo plano: o app não pode estar em "Apps em suspensão" nem em "Apps em suspensão profunda".

6. **Atualizar:** gere o APK de novo com o mesmo comando e instale com `adb install -r artifacts/slimegoals-<versão>-android.apk`, mantendo os dados. **Com outra chave, só desinstalando o app, o que apaga os dados.**

## Instalar no Linux

O pacote é um `.tar.gz` *self-contained* (não precisa do .NET instalado) para linux-x64. A instalação é só no seu usuário, sem root.

```bash
# gerar o pacote
bash packaging/linux/build.sh
tar -xzf artifacts/slimegoals-0.10.0-linux-x64.tar.gz
cd slimegoals-0.10.0
./install.sh
```

Ainda não há pacote pronto para baixar, então gere-o com o `build.sh`, que precisa do SDK do .NET. O `install.sh` só funciona de dentro da pasta extraída: rodar o de `packaging/linux/` dá erro, porque o app compilado não está ali.

Isso instala o app em `~/.local/opt/slimegoals/`, o link em `~/.local/bin/slimegoals`, o atalho em `~/.local/share/applications/slimegoals.desktop` e os ícones em `~/.local/share/icons/hicolor/`. Rodar `./install.sh` de novo substitui a versão instalada.

Para desinstalar, rode `./uninstall.sh` de dentro da pasta extraída. Ele pergunta se deve apagar também seus dados (a pasta `~/.local/share/SlimeGoals`, com o banco); a resposta padrão é não. Para apagar sem a pergunta, use `./uninstall.sh --apagar-dados` com o app fechado. Apagar os dados não tem volta. A entrada de "iniciar com o sistema" (`~/.config/autostart/slimegoals.desktop`) fica; apague-a à mão se quiser.

## Documentação

- [Especificação (v1.4)](specs/Planejamento_v3.md): requisitos, modelo de dados, regras de lembrete e design system
- [Protótipos visuais](specs/design/): desktop (`v4-bento-desktop.html`) e mobile (`v3-*-mobile.html`)
- [Planos de implementação](specs/plans/)

## Como contribuir

- **Feature Branch Workflow:** uma branch por funcionalidade (`feat/…`, `fix/…`, `docs/…`), integrada na `main` por Pull Request com *merge commit*
- **[Conventional Commits](https://www.conventionalcommits.org/pt-br/)** em português, com escopos por camada: `core`, `infra`, `app`, `linux`, `desktop`, `android`, `specs`, `plans`, `build`
- **TDD:** todo comportamento novo começa por um teste que falha; a `main` sempre compila sem warnings e passa nos testes

## Roteiro

| Fase | Entrega | Status |
|---|---|---|
| 0 | Fundação, tema, navegação e spikes (notificações GNOME, Android) | ✅ |
| 1 | Domínio e persistência de metas | ✅ |
| 2 | Telas de metas | ✅ |
| 3 | Medidas, registros e tela Hoje | ✅ |
| 4 | Lembretes (planejador, agendador, notificação nativa, segundo plano) | ✅ |
| 5 | Relatórios | ✅ |
| 6 | Acabamento e empacotamento Linux | ✅ |
| 7 | Android (7a interface ✅ · 7b lembretes ✅ · 7c distribuição ✅ implementado · ⏳ teste longo no aparelho) | ⏳ |
| 8 | Mascote slime | ✅ |

## Licença

[PolyForm Noncommercial 1.0.0](LICENSE.md): uso **pessoal, educacional, de pesquisa e por organizações sem fins lucrativos** é permitido,
inclusive modificar e redistribuir com a licença. **Uso comercial não é permitido.**
Por restringir o uso comercial, este projeto é *source-available*, não *open source* no sentido da OSI.

Componentes de terceiros mantêm suas licenças: veja [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
