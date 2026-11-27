# Instalar no Linux

O pacote é um `.tar.gz` *self-contained* para linux-x64: não precisa do .NET instalado. A instalação fica só no seu usuário, sem root. O app foi feito para o GNOME; as notificações com botões usam o D-Bus.

## Gerar e instalar

Ainda não há pacote pronto para baixar. Gere-o com o `build.sh`, que precisa do SDK do .NET (veja [Desenvolvimento](desenvolvimento.md#pré-requisitos)):

```bash
bash packaging/linux/build.sh
tar -xzf artifacts/slimegoals-0.15.0-linux-x64.tar.gz
cd slimegoals-0.15.0
./install.sh
```

O `build.sh` grava ao lado do pacote o `slimegoals-0.15.0-linux-x64.tar.gz.sha256`. Se o pacote for copiado para outro computador, leve os dois e, antes de extrair, confira na pasta onde eles estão:

```bash
sha256sum -c slimegoals-0.15.0-linux-x64.tar.gz.sha256
```

A resposta `SUCESSO` (`OK` com o sistema em inglês) diz que o arquivo chegou inteiro e igual ao que o `build.sh` gerou. `FALHOU` (`FAILED`) diz que ele mudou no caminho: gere ou copie de novo, e não instale esse.

O `install.sh` só funciona de dentro da pasta extraída. Rodar o de `packaging/linux/` dá erro, porque o app compilado não está ali.

A instalação grava:

| O quê | Onde |
|---|---|
| O app | `~/.local/opt/slimegoals/` |
| O link para o terminal | `~/.local/bin/slimegoals` |
| O atalho do menu | `~/.local/share/applications/slimegoals.desktop` |
| Os ícones | `~/.local/share/icons/hicolor/` |

Os seus dados ficam em `~/.local/share/SlimeGoals/slimegoals.db`. Sem conta, nada sai do computador. Com conta, os dados vão cifrados para o seu servidor (veja [Sincronização](sincronizacao.md)).

## Atualizar

Feche o app com **Sair** (fechar a janela pode só escondê-lo, conforme Configurações), gere o pacote da versão nova e rode o `./install.sh` dela. Ele substitui a versão instalada e mantém os seus dados. Com o app aberto, o script se recusa a instalar.

## Desinstalar

Rode `./uninstall.sh` de dentro da pasta extraída. Ele pergunta se deve apagar também os seus dados (a pasta `~/.local/share/SlimeGoals`, com o banco); a resposta padrão é não. Para apagar sem a pergunta, use `./uninstall.sh --apagar-dados` com o app fechado. **Apagar os dados não tem volta.**

A entrada de "Iniciar com o sistema" (`~/.config/autostart/slimegoals.desktop`) fica. Apague-a à mão se quiser.
