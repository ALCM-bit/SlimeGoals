# Spike: janela pequena sempre por cima no GNOME

> Cronômetro fora do app, Tarefa 1 · executado em 13–14/11/2026 · código descartável na branch local `spike/janelinha-cronometro` (não mesclada)

**Ambiente:** CachyOS, GNOME Shell 51.1, sessão Wayland, app pelo X11 (XWayland), Avalonia 12.1.4. Três telas de 1920×1080; a principal começa em x = 1920 e tem a área útil a partir de y = 32 (barra do GNOME).

**Método:** um app Avalonia mínimo (`dotnet run --project spikes/janelinha [-- --sair-em N]`) abre, cinco segundos depois de iniciar, uma janela de 280 px sem borda, por cima, sem foco e no canto inferior direito da tela principal, com o tempo correndo e um botão de fechar. O programa grava no terminal as telas, a posição pedida, a posição dois segundos depois e qualquer evento de foco. Uma rodada automática conferiu as propriedades da janela com `xprop`; depois o autor usou a janelinha com outras janelas abertas.

## Medição automática (14/11/2026)

- `_NET_WM_STATE` = `_NET_WM_STATE_SKIP_TASKBAR`, `_NET_WM_STATE_ABOVE` e `_NET_WM_STATE_DEMANDS_ATTENTION`.
- Posição pedida `3536, 990`, mantida dois segundos depois.
- Nenhum evento de foco (`Activated` não disparou).
- A posição só vale se for definida **antes** de `Show()`, com `WindowStartupLocation.Manual`. Mudar `Position` depois de mostrar não move a janela de forma confiável.
- No Avalonia 12, a propriedade é `WindowDecorations = WindowDecorations.None`. `SystemDecorations` está obsoleta e quebra o build com `TreatWarningsAsErrors`.

## Respostas do autor

| # | Pergunta | Resposta |
|---|---|---|
| 1 | A janelinha fica por cima de outra janela em foco? | **Sim.** |
| 2 | Ela aparece sem roubar o foco de quem estava digitando? | **Sim.** Não tomou o foco. |
| 3 | Aparece na posição pedida (canto inferior direito da tela principal)? | **Sim.** |
| 4 | Arrastar move a janela? | **Sim.** |
| 5 | Os cantos arredondados aparecem sem fundo quadrado? | **Sim.** Ficam arredondados. |
| 6 | O GNOME mostra aviso de "janela pronta" ou põe a janelinha na lista de janelas? | **Não.** Pareceu certo. |

## Decisões resultantes

1. **O desenho do Linux fica como na spec.** Nenhuma resposta pediu outro caminho.
2. **Janela:** `WindowDecorations.None`, `Topmost`, `ShowInTaskbar = false`, `ShowActivated = false`, `CanResize = false`, `SizeToContent`, fundo transparente com `TransparencyLevelHint = Transparent`, e `WindowStartupLocation.Manual` com a posição definida antes de `Show()`.
3. **Arrastar:** `BeginMoveDrag` no `PointerPressed` da janela. A posição nova é gravada quando o arrasto termina.
4. **`DEMANDS_ATTENTION`:** o estado apareceu, mas o GNOME não mostrou aviso. Não é preciso contornar.
5. **Posição gravada fora das telas:** com três monitores, um pode ser desligado. O app confere a posição contra as telas atuais antes de mostrar, e volta ao canto padrão se ela não couber em nenhuma.
