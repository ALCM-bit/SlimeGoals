# Avisos de terceiros

A licença do SlimeGoals ([LICENSE.md](LICENSE.md)) cobre apenas o código deste repositório.
Os componentes abaixo mantêm suas próprias licenças.

## Distribuídos junto com o código

| Componente | Local | Licença |
|---|---|---|
| Fonte [Figtree](https://github.com/erikdkennedy/figtree) | `src/SlimeGoals.App/Assets/Fonts/` | SIL Open Font License 1.1 ([OFL.txt](src/SlimeGoals.App/Assets/Fonts/OFL.txt)) |
| 16 ícones do [Lucide](https://lucide.dev) (lucide-static 1.66.0), convertidos para path data | `src/SlimeGoals.App/Goals/GoalIcons.cs` | ISC (texto abaixo) |
| 7 ícones do Lucide para botões de ação (check, x, minus, plus, play, square, ellipsis), convertidos para path data | `src/SlimeGoals.App/Styles/Theme.axaml` | ISC (texto abaixo) |
| O ícone "target" do Lucide (um dos 16 acima) continua como ícone de meta; o ícone do app e da bandeja é o mascote do próprio projeto (código do repositório, não é do Lucide) | `src/SlimeGoals.App/Goals/GoalIcons.cs` | ISC (texto abaixo), só para o "target" |

## Dependências NuGet (baixadas na compilação)

| Pacote | Licença |
|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | MIT |
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.DependencyInjection(.Abstractions) | MIT |
| Microsoft.Data.Sqlite | MIT |
| Dapper | Apache-2.0 |
| Tmds.DBus.Protocol | MIT |
| xunit.v3.mtp-v2 | Apache-2.0 |
| AwesomeAssertions | Apache-2.0 |
| Microsoft.Extensions.TimeProvider.Testing | MIT |

## Distribuídos no pacote

O `.tar.gz` do Linux (`packaging/linux/build.sh`) é *self-contained*: traz o runtime .NET, as bibliotecas gerenciadas e as nativas. Os textos ficam em `licenses/` dentro do pacote (origem em [packaging/linux/licenses](packaging/linux/licenses)).

| Componente | Licença | Arquivo |
|---|---|---|
| Runtime .NET (`libcoreclr.so`, `libclrjit.so`, `System.*.dll`, `libSystem.*.so`) | MIT | `MIT-dotnet-runtime-and-Microsoft.Extensions.txt`; avisos de terceiros do runtime em `dotnet-runtime-THIRD-PARTY-NOTICES.txt` |
| Avalonia (`Avalonia*.dll`, `MicroCom.Runtime.dll`) | MIT | `MIT-Avalonia.txt` |
| SkiaSharp (`SkiaSharp.dll`, `libSkiaSharp.so`) e HarfBuzzSharp (`HarfBuzzSharp.dll`, `libHarfBuzzSharp.so`) | MIT | `MIT-SkiaSharp-HarfBuzzSharp.txt` |
| Skia (dentro de `libSkiaSharp.so`) | BSD-3-Clause | `BSD-3-Clause-skia.txt` |
| HarfBuzz (dentro de `libHarfBuzzSharp.so`) | "Old MIT" | `HarfBuzz-Old-MIT.txt` |
| Demais bibliotecas embutidas no Skia/HarfBuzz (FreeType, libpng, ICU, libjpeg-turbo, libwebp, expat etc.) | licenças variadas | `SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt` |
| CommunityToolkit.Mvvm | MIT | `MIT-CommunityToolkit.txt` |
| Microsoft.Extensions.DependencyInjection(.Abstractions) | MIT | `MIT-dotnet-runtime-and-Microsoft.Extensions.txt` |
| Microsoft.Data.Sqlite | MIT | `MIT-Microsoft.Data.Sqlite.txt` |
| Tmds.DBus.Protocol | MIT | `MIT-Tmds.DBus.Protocol.txt` |
| Dapper | Apache-2.0 | `Apache-2.0.txt`, `Apache-2.0-NOTICE.txt` |
| SQLitePCLRaw (`SQLitePCLRaw.*.dll`) | Apache-2.0 | `Apache-2.0.txt`, `Apache-2.0-NOTICE.txt` |
| SQLite (`libe_sqlite3.so`) | domínio público | `SQLite-public-domain.txt` |
| Fonte Figtree (embutida em `slimegoals.dll`) | SIL OFL 1.1 | `OFL-1.1-Figtree.txt` |
| Ícones do Lucide (embutidos em `SlimeGoals.App.dll`) | ISC | `ISC-Lucide.txt` |

## Texto da licença do Lucide

```
ISC License

Copyright (c) 2026 Lucide Icons and Contributors

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```
