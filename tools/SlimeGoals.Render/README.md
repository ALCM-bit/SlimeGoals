# SlimeGoals.Render

Ferramenta de desenvolvimento que renderiza as telas do app sem janela (Avalonia Headless + Skia) e grava PNGs para conferência visual. Fica fora da solução: `dotnet test --solution SlimeGoals.slnx` não a compila.

## Uso

```
dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all|mascot|narrow|account]
```

Sem o segundo argumento, vale `all`. Grava um PNG por tela e tamanho, sempre o `ShellView` inteiro:

- `today-<tamanho>.png`, `goals-<tamanho>.png`, `goal-edit-<tamanho>.png`, `goal-edit-end-<tamanho>.png` (o formulário rolado até o fim),
  `reports-week-<tamanho>.png`, `reports-month-<tamanho>.png`, `settings-<tamanho>.png`;
- `<tamanho>` é `phone` (412×915) ou `desktop` (1100×720).

O modo `account` grava a seção "Conta e sincronização" de Configurações sobre uma conta falsa (`SampleAccount`, em `Sample.cs`: nenhuma rede, códigos de mentira), a 320×1400, 360×1400 e 1100×800, como `account-<tela>-<largura>.png`:

- estados da seção: `signed-out`, `signed-in-error` (com `last_error`), `session-expired`, `vault-locked`, `session-unreadable`;
- subpáginas: `register`, `recovery-code`, `confirm`, `login`, `login-busy` (operação presa, com a barra de ocupado), `login-error`, `recover`, `recovered-code`, `reauthenticate`, `change-password`, `devices`, `sign-out` (aviso com 3 pendências), `delete-account`.

O cadastro pendente da conta falsa usa o construtor interno de `PendingRegistration` (`InternalsVisibleTo` no `SlimeGoals.Sync.csproj`).

O modo `narrow` grava o editor de meta com a confirmação de exclusão a 320 e 360 px (`goal-edit-delete-<largura>.png`) e o Hoje com um cronômetro rodando a 320, 360 e 1100 px (`today-timer-<largura>.png`): é o cartão mais apertado, com o tempo, o botão de parar e o de mais opções.

O modo `mascot` grava só `mascot.png`: as quatro poses do slime (dormindo, parado, feliz, comemorando), lado a lado em 160×160 sobre o `HeroBrush`, com o nome acessível embaixo. O quadro é capturado cerca de 0,8 s depois de montar a tela, com as animações já em andamento.

## Dados

Um banco SQLite temporário (`slimegoals-render-<guid>.db` em `Path.GetTempPath()`), apagado ao final; nunca toca o banco do app.
Quatro metas de exemplo (Beber água, Leitura, Treino, Aula de inglês) e registros dos últimos 10 dias. O relógio é o do sistema.

## Observações

- A variação `phone` monta o shell com as capacidades do Android (`PlatformCapabilities(true, false, false, false)` e uma porta de permissões falsa com tudo negado, para o aviso aparecer): lembretes em Hoje, sem as seções Geral e Janela nas Configurações e sem "Sair"; a `desktop`, com `PlatformCapabilities.Desktop`.
- A `RenderApp` espelha os `DataTemplates` do `App.axaml`: ao criar uma página nova, registre-a nos dois lugares.
- O slime sai no quadro parado da pose (partes da pose visíveis, sem movimento): a janela headless não fica ativa, então a pseudo-classe `:live` não liga. É o esperado; a captura não precisa da animação.
