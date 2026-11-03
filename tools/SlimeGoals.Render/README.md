# SlimeGoals.Render

Ferramenta de desenvolvimento que renderiza as telas do app sem janela (Avalonia Headless + Skia) e grava PNGs para conferência visual. Fica fora da solução: `dotnet test --solution SlimeGoals.slnx` não a compila.

## Uso

```
dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all|mascot]
```

Sem o segundo argumento, vale `all`. Grava um PNG por tela e tamanho, sempre o `ShellView` inteiro:

- `today-<tamanho>.png`, `goals-<tamanho>.png`, `goal-edit-<tamanho>.png`, `goal-edit-end-<tamanho>.png` (o formulário rolado até o fim),
  `reports-week-<tamanho>.png`, `reports-month-<tamanho>.png`, `settings-<tamanho>.png`;
- `<tamanho>` é `phone` (412×915) ou `desktop` (1100×720).

O modo `mascot` grava só `mascot.png`: as quatro poses do slime (dormindo, parado, feliz, comemorando), lado a lado em 160×160 sobre o `HeroBrush`, com o nome acessível embaixo. O quadro mostra o instante zero das animações.

## Dados

Um banco SQLite temporário (`slimegoals-render-<guid>.db` em `Path.GetTempPath()`), apagado ao final; nunca toca o banco do app.
Quatro metas de exemplo (Beber água, Leitura, Treino, Aula de inglês) e registros dos últimos 10 dias. O relógio é o do sistema.

## Observações

- A variação `phone` monta o shell com as capacidades do Android (`PlatformCapabilities(true, false, false, false)` e uma porta de permissões falsa com tudo negado, para o aviso aparecer): lembretes em Hoje, sem as seções Geral e Janela nas Configurações e sem "Sair"; a `desktop`, com `PlatformCapabilities.Desktop`.
- A `RenderApp` espelha os `DataTemplates` do `App.axaml`: ao criar uma página nova, registre-a nos dois lugares.
