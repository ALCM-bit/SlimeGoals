# SlimeGoals.Render

Ferramenta de desenvolvimento que renderiza as telas do app sem janela (Avalonia Headless + Skia) e grava PNGs para conferência visual. Fica fora da solução: `dotnet test --solution SlimeGoals.slnx` não a compila.

## Uso

```
dotnet run --project tools/SlimeGoals.Render -- <pasta-saída> [phone|desktop|all]
```

Sem o segundo argumento, vale `all`. Grava um PNG por tela e tamanho, sempre o `ShellView` inteiro:

- `today-<tamanho>.png`, `goals-<tamanho>.png`, `goal-edit-<tamanho>.png`,
  `reports-week-<tamanho>.png`, `reports-month-<tamanho>.png`, `settings-<tamanho>.png`;
- `<tamanho>` é `phone` (412×915) ou `desktop` (1100×720).

## Dados

Um banco SQLite temporário (`slimegoals-render-<guid>.db` em `Path.GetTempPath()`), apagado ao final; nunca toca o banco do app.
Quatro metas de exemplo (Beber água, Leitura, Treino, Aula de inglês) e registros dos últimos 10 dias. O relógio é o do sistema.

## Observações

- A variação `phone` ainda usa os ViewModels com o padrão do desktop; `Sample.CreateShell(bool phone)` é o ponto onde passar as capacidades do Android.
- A `RenderApp` espelha os `DataTemplates` do `App.axaml`: ao criar uma página nova, registre-a nos dois lugares.
