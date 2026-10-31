# Spike: notificações com ações no GNOME

> Fase 0, Task 3 · executado em 31/10/2026 · código descartável na branch local `spike/notificacao-gnome` (não mesclada)

**Ambiente:** CachyOS, GNOME Shell 51.1, sessão Wayland, servidor `gnome-shell` (spec de notificações 1.2), Avalonia 12.1.4, `Tmds.DBus.Protocol` 0.95.2.

**Método:** `gdbus` para as capacidades do servidor; um ouvinte em Python (PyGObject) e depois um app Avalonia mínimo em C# enviaram notificações com as ações `done/Feito`, `snooze/Adiar`, `ignore/Ignorar` e `default/Abrir` e registraram os sinais recebidos. O autor clicou nas notificações em cada cenário.

## Respostas

| # | Pergunta | Resposta | Evidência |
|---|---|---|---|
| 1 | O servidor anuncia `actions`? | **Sim.** Capacidades: `actions`, `body`, `body-markup`, `icon-static`, `persistence`, `sound`. | `GetCapabilities` |
| 2 | Os botões chegam como `ActionInvoked` com a janela oculta e pela central? | **Sim, mas só para a conexão D-Bus que enviou o `Notify`.** O GNOME envia os sinais apenas para o remetente; um monitor externo não os vê. Com o processo vivo, funciona no banner, na central e com a janela oculta. | Testes 3, 4, A e B: `ActionInvoked (id, 'snooze' / 'done' / 'default')` |
| 2b | E se o processo já tiver encerrado? | **O clique é perdido sem aviso:** a notificação some, nenhum erro aparece e a ação não chega a lugar nenhum. | Teste 5 |
| 3 | Com o Não Perturbe ativo? | **Sem banner;** a notificação vai para a central, e os botões funcionam ao expandir o card (setinha ⌄). | Testes 1–3 |
| 4 | `expire_timeout` é respeitado? `NotificationClosed` tem qual motivo? | **Ignorado:** uma notificação com 10 s continuava na central 30 min depois. Fechar no X ou clicar numa ação gera `NotificationClosed` com **motivo 2** (fechada pelo usuário). | Teste 1 (print) e logs |
| 5a | A dica `desktop-entry` define nome e ícone? | **Não.** O cabeçalho mostrou o nome do executável ("GnomeNotify") e o ícone genérico; `app_name` e `desktop-entry` foram ignorados. O GNOME identifica o app pelo processo e pela janela. | Teste A (print 3) |
| 5b | O clique traz a janela à frente no Wayland? | **Sim.** Com a janela oculta, `Show()` + `Activate()` ao receber `default` trouxe a janela à frente, sem o aviso "está pronto". O GNOME também envia `ActivationToken` (xdg-activation) antes da ação, disponível se for preciso. | Teste B (print 5) |
| 6 | `Tmds.DBus.Protocol` 0.95.2 serve? | **Sim.** `Notify` com `a{sv}` e `WatchSignalAsync` para os três sinais funcionaram de primeira, num app Avalonia 12. | Teste A/B |

## Decisões resultantes

1. **Adaptador Linux (Fase 4c):** `Tmds.DBus.Protocol`, com uma **única conexão D-Bus de vida longa** no processo do app, que envia e escuta.
2. **Expiração:** o próprio app controla o prazo do lembrete (como a spec previa). `NotificationClosed` com motivo 2 **sem** `ActionInvoked` antes conta como **Ignorar**.
3. **Processo encerrado:** ao sair, o app chama `CloseNotification` nas notificações pendentes, para não deixar na central botões que não funcionam. Na Fase 4c, avaliar a alternativa com ativação D-Bus (`org.gtk.Notifications` ou o portal `org.freedesktop.portal.Notification`), que inicia o app ao clicar mesmo com ele fechado, ao custo de `.desktop` com `DBusActivatable=true` e de um arquivo de serviço D-Bus.
4. **Nome e ícone (Fase 6):** instalar `slimegoals.desktop` com `StartupWMClass=slimegoals` e o executável `slimegoals` (já é o `AssemblyName` do head Desktop); validar que o cabeçalho mostra "SlimeGoals" e o ícone do app.
5. **Fallback em janela própria:** continua previsto, mas deixa de ser crítico no GNOME; fica para servidores de notificação sem `actions`.
6. **Não Perturbe:** documentar que os lembretes vão para a central sem banner; avaliar um aviso na tela Hoje (Fase 3/4).
