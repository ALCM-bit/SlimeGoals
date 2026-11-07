# Avisos de terceiros

A licença do SlimeGoals ([LICENSE.md](LICENSE.md)) cobre apenas o código deste repositório.
Os componentes abaixo mantêm suas próprias licenças.

## Distribuídos junto com o código

| Componente | Local | Licença |
|---|---|---|
| Fonte [Figtree](https://github.com/erikdkennedy/figtree) | `src/SlimeGoals.App/Assets/Fonts/` | SIL Open Font License 1.1 ([OFL.txt](src/SlimeGoals.App/Assets/Fonts/OFL.txt)) |
| 16 ícones do [Lucide](https://lucide.dev) (lucide-static 1.66.0), convertidos para path data | `src/SlimeGoals.App/Goals/GoalIcons.cs` | ISC; code, music e target também MIT, do Feather (texto abaixo) |
| 9 ícones do Lucide para botões de ação (check, x, chevron-left, chevron-right, minus, plus, play, square, ellipsis), convertidos para path data | `src/SlimeGoals.App/Styles/Theme.axaml` | ISC; todos menos play também MIT, do Feather (texto abaixo) |
| 4 ícones do Lucide para a navegação inferior (sun, list-checks, chart-column, sliders-horizontal), convertidos para path data | `src/SlimeGoals.App/Styles/Theme.axaml` | ISC (texto abaixo) |
| O ícone "target" do Lucide (um dos 16 acima) continua como ícone de meta; o ícone do app e da bandeja é o mascote do próprio projeto (código do repositório, não é do Lucide) | `src/SlimeGoals.App/Goals/GoalIcons.cs` | ISC e MIT (texto abaixo), só para o "target" |

## Dependências NuGet (baixadas na compilação)

| Pacote | Licença |
|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Android, Avalonia.Themes.Fluent | MIT |
| Avalonia.Headless, Avalonia.Skia (ferramenta de render) | MIT |
| Xamarin.AndroidX.*, Xamarin.Kotlin.*, Xamarin.KotlinX.*, Xamarin.Google.Guava.ListenableFuture, Xamarin.JSpecify, Xamarin.Jetbrains.Annotations (dependências do Android) | MIT (bindings) e Apache-2.0 |
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.DependencyInjection(.Abstractions) | MIT |
| Microsoft.Data.Sqlite | MIT |
| Dapper | Apache-2.0 |
| Tmds.DBus.Protocol | MIT |
| xunit.v3.mtp-v2 | Apache-2.0 |
| AwesomeAssertions | Apache-2.0 |
| Microsoft.Extensions.TimeProvider.Testing | MIT |
| Npgsql (servidor) | PostgreSQL License |
| Testcontainers.PostgreSql (testes do servidor) | MIT |

## Distribuídos no pacote do Linux

O `.tar.gz` do Linux (`packaging/linux/build.sh`) é *self-contained*: traz o runtime .NET, as bibliotecas gerenciadas e as nativas. Os textos ficam em `licenses/` dentro do pacote (origem em [src/SlimeGoals.App/Licenses](src/SlimeGoals.App/Licenses), embutidos no app para a tela "Sobre").

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
| Fonte Figtree (embutida em `SlimeGoals.App.dll`) | SIL OFL 1.1 | `OFL-1.1-Figtree.txt` |
| Ícones do Lucide (embutidos em `SlimeGoals.App.dll`) | ISC e MIT (Feather) | `ISC-Lucide.txt` |

## Distribuídos no APK do Android

O APK (`packaging/android/build.sh`) traz o runtime .NET para Android, as bibliotecas gerenciadas e as nativas (arm64-v8a e x86_64). Os textos têm a mesma origem do pacote do Linux, [src/SlimeGoals.App/Licenses](src/SlimeGoals.App/Licenses), e ficam embutidos no app.

| Componente | Licença | Arquivo |
|---|---|---|
| .NET para Android (Mono, `libmonosgen-2.0.so`) e bibliotecas do runtime | MIT | `MIT-dotnet-runtime-and-Microsoft.Extensions.txt` (o texto do pacote do .NET para Android é idêntico ao do runtime); avisos de terceiros em `dotnet-android-THIRD-PARTY-NOTICES.txt` |
| Avalonia, incluindo Avalonia.Android | MIT | `MIT-Avalonia.txt` |
| SkiaSharp (`libSkiaSharp.so`) e HarfBuzzSharp (`libHarfBuzzSharp.so`) | MIT | `MIT-SkiaSharp-HarfBuzzSharp.txt` |
| Skia (dentro de `libSkiaSharp.so`) | BSD-3-Clause | `BSD-3-Clause-skia.txt` |
| HarfBuzz (dentro de `libHarfBuzzSharp.so`) | "Old MIT" | `HarfBuzz-Old-MIT.txt` |
| Demais bibliotecas embutidas no Skia/HarfBuzz | licenças variadas | `SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt` |
| CommunityToolkit.Mvvm | MIT | `MIT-CommunityToolkit.txt` |
| Microsoft.Extensions.DependencyInjection(.Abstractions) | MIT | `MIT-dotnet-runtime-and-Microsoft.Extensions.txt` |
| Microsoft.Data.Sqlite | MIT | `MIT-Microsoft.Data.Sqlite.txt` |
| Dapper | Apache-2.0 | `Apache-2.0.txt`, `Apache-2.0-NOTICE.txt` |
| SQLitePCLRaw | Apache-2.0 | `Apache-2.0.txt`, `Apache-2.0-NOTICE.txt` |
| SQLite (`libe_sqlite3.so`) | domínio público | `SQLite-public-domain.txt` |
| Bindings Xamarin (AndroidX, Kotlin e outros) | MIT | `MIT-Xamarin-bindings.txt` |
| AndroidX, Kotlin e kotlinx.coroutines, Guava ListenableFuture, JSpecify, JetBrains Annotations (bibliotecas Java e Kotlin) | Apache-2.0 | `Apache-2.0.txt` |
| Fonte Figtree (embutida em `SlimeGoals.App.dll`) | SIL OFL 1.1 | `OFL-1.1-Figtree.txt` |
| Ícones do Lucide (embutidos em `SlimeGoals.App.dll`) | ISC e MIT (Feather) | `ISC-Lucide.txt` |

## Imagem Docker do servidor

O servidor de sincronização (`src/SlimeGoals.Server`) é montado numa imagem Docker, só para quem roda o servidor; ela não vai no app. A imagem traz o runtime ASP.NET Core e as bibliotecas gerenciadas do servidor.

| Componente | Licença |
|---|---|
| Runtime .NET e ASP.NET Core (imagem `aspnet:10.0-alpine`, com o Alpine Linux e seus pacotes) | MIT; licenças variadas no Alpine |
| Npgsql | PostgreSQL License |
| Dapper | Apache-2.0 |
| PostgreSQL (imagem `postgres:18-alpine` usada no Compose, não distribuída por este repositório) | PostgreSQL License |

## Onde ver os textos no app

O app mostra todos os componentes e o texto de cada licença em Configurações → Sobre → Licenças. A lista é a mesma nas duas plataformas e indica onde cada componente é distribuído (Linux, Android ou ambos).

## Texto da licença do Lucide

A licença tem duas partes: ISC para o Lucide e MIT para os ícones derivados do Feather. Dos ícones que usamos, 11 estão nessa lista: code, music, target, check, x, minus, plus, square, chevron-left, chevron-right e ellipsis (na lista, com o nome antigo more-horizontal).

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

---

The following Lucide icons are derived from the Feather project:

airplay, alert-circle, alert-octagon, alert-triangle, aperture, arrow-down-circle, arrow-down-left, arrow-down-right, arrow-down, arrow-left-circle, arrow-left, arrow-right-circle, arrow-right, arrow-up-circle, arrow-up-left, arrow-up-right, arrow-up, at-sign, calendar, cast, check, chevron-down, chevron-left, chevron-right, chevron-up, chevrons-down, chevrons-left, chevrons-right, chevrons-up, circle, clipboard, clock, code, columns, command, compass, corner-down-left, corner-down-right, corner-left-down, corner-left-up, corner-right-down, corner-right-up, corner-up-left, corner-up-right, crosshair, database, divide-circle, divide-square, dollar-sign, download, external-link, feather, frown, hash, headphones, help-circle, info, italic, key, layout, life-buoy, link-2, link, loader, lock, log-in, log-out, maximize, meh, minimize, minimize-2, minus-circle, minus-square, minus, monitor, moon, more-horizontal, more-vertical, move, music, navigation-2, navigation, octagon, pause-circle, percent, plus-circle, plus-square, plus, power, radio, rss, search, server, share, shopping-bag, sidebar, smartphone, smile, square, table-2, tablet, target, terminal, trash-2, trash, triangle, tv, type, upload, x-circle, x-octagon, x-square, x, zoom-in, zoom-out

The MIT License (MIT) (for the icons listed above)

Copyright (c) 2013-present Cole Bemis

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
