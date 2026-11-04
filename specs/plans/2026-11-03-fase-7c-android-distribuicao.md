# Fase 7c — Android: distribuição — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** um APK Release assinado com uma chave do autor, que instala e roda num Samsung de verdade. O app mostra a versão e as licenças numa seção "Sobre" e avisa quando a otimização de bateria pode segurar os lembretes. Critério de pronto da §12: "Instalável em um aparelho real".

**Architecture:**
- **Empacotamento:** `packaging/android/` ganha dois scripts:
  - `make-keystore.sh` cria a chave uma vez, fora do repositório; quem roda é o autor.
  - `build.sh` publica o APK Release assinado, lendo a senha de uma variável de ambiente, e o verifica com `apksigner`.
- **Versão:** a versão continua só em `Directory.Build.props`, que passa a `0.7.0`. O `versionCode` do Android é derivado dela.
- **Licenças:**
  - Os textos passam a ser recursos embutidos do App (`EmbeddedResource`), lidos sem Avalonia.
  - Um catálogo (`LicenseCatalog`) lista componente, licença, arquivo e plataforma.
  - O `.tar.gz` do Linux passa a copiar os textos da nova pasta.
- **Configurações:**
  - A seção "Sobre", em todas as plataformas, mostra a versão e abre a lista de licenças e o texto de cada uma.
  - A linha "Bateria" fica na seção Lembretes, só no Android.
  - O Voltar do sistema fecha primeiro o texto da licença, depois a lista.
- **Bateria:**
  - `ReminderPermissionStatus` ganha `BatteryUnrestricted`.
  - O adaptador Android lê `PowerManager.IsIgnoringBatteryOptimizations` e pede a liberação com `ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`.

**Tech Stack:** as mesmas versões da Fase 8, sem pacote novo. A assinatura usa as propriedades `AndroidSigning*` do .NET for Android, com `env:` para as senhas. A chave sai do `keytool` (JDK 21) e a verificação do `apksigner` (build-tools do SDK).

**Spec:** `specs/Planejamento_v3.md` v1.4:
- §3 (RNF de distribuição e de lembretes);
- §9.9 (permissões no Android);
- §12, linha 7c;
- tabela de riscos (restrições de fabricantes);
- "Pendências" (versão mínima do Android).

**Fora deste plano:**
- AAB e Play Store: o autor escolheu só o APK;
- atualização automática e distribuição por loja ou F-Droid;
- detectar "Apps em suspensão" da Samsung (não há API pública); fica no README, como ajuste manual;
- tela de boas-vindas.

## Decisões deste plano (o autor confirma na revisão)

1. **Versão mínima: Android 8.0 (API 26),** a sugestão do spike, que já está no projeto (`SupportedOSPlatformVersion` 26). Ela fecha a pendência da spec.
2. **Versão e `versionCode`:**
   - `Directory.Build.props` passa de `0.6.0` para `0.7.0`.
   - O `ApplicationVersion` (o `versionCode`) passa a ser calculado: `maior × 10000 + menor × 100 + patch`. Para 0.7.0, isso dá **700**.
   - O `ApplicationDisplayVersion` continua `$(Version)`.
   - Assim cada versão nova instala por cima da anterior sem mexer em dois lugares.
3. **APK só para `android-arm64` e `android-x64`:**
   - O Samsung é arm64; o x64 é para o emulador.
   - `armeabi-v7a` e `x86` ficam de fora, porque são aparelhos antigos de 32 bits.
   - Isso fica só no `build.sh` (`-p:RuntimeIdentifiers=...`); o Debug do dia a dia não muda.
4. **Chave de assinatura fora do repositório:**
   - Fica em `${SLIMEGOALS_KEYSTORE:-$HOME/.android-keystores/slimegoals.keystore}`, com alias `slimegoals`, RSA 4096 e validade de 10000 dias.
   - A senha vem de `SLIMEGOALS_KEYSTORE_PASS`, a mesma para o arquivo e para a chave.
   - `make-keystore.sh` não sobrescreve uma chave que já existe.
   - O README avisa que **perder a chave impede atualizar o app instalado**, então é preciso guardar uma cópia.
   - O assistente nunca cria nem lê a chave real; nos testes usa uma chave descartável no scratchpad.
5. **Trimming do Release:**
   - O padrão do .NET for Android fica, se o *smoke test* (Task 1) passar.
   - Se algo quebrar, a ordem de correção é: primeiro `TrimmerRootAssembly` para o assembly que quebrou; só se isso não bastar, `PublishTrimmed=false`. A escolha vai para a spec.
6. **Licenças embutidas no App,** como `EmbeddedResource` com `LogicalName` `SlimeGoals.Licenses.<arquivo>`:
   - A fonte única é `src/SlimeGoals.App/Licenses/`: os arquivos de `packaging/linux/licenses/` vão para lá, mais o `LICENSE.md` do projeto, linkado.
   - O catálogo mostra **todos** os componentes em todas as plataformas, com a coluna "Onde" (`"Linux e Android"`, `"Linux"` ou `"Android"`). Isso evita uma capacidade nova de plataforma só para filtrar a lista.
   - Os componentes só do Android entram depois de conferir o conteúdo do APK (`unzip -l`) e os `LICENSE` dos pacotes no cache do NuGet: runtime do .NET for Android, bindings `Xamarin.AndroidX.*` (MIT) e as bibliotecas AndroidX e Kotlin que eles trazem (Apache-2.0).
7. **"Sobre" em Configurações, em todas as plataformas:**
   - Mostra `"SlimeGoals <versão>"` e `"Licença: PolyForm Noncommercial 1.0.0"`, além do botão `"Licenças"`.
   - O botão abre a lista dentro da própria página, com `"← Configurações"`.
   - Tocar num item mostra o texto completo, com `"← Licenças"`.
   - A seção não conta para `IsEmpty`, que continua falando só das seções de configuração.
8. **Voltar genérico:**
   - `PageViewModel` ganha `public virtual bool TryGoBack() => false`.
   - `GoalsPageViewModel` sobrescreve com `CloseEditorIfOpen()`. `SettingsPageViewModel` fecha o texto e depois a lista.
   - `ShellViewModel.GoBack()` chama `CurrentPage.TryGoBack()` antes de voltar para Hoje.
9. **Bateria:**
   - Na seção Lembretes, abaixo de "Alarmes exatos", entra a linha `"Bateria"`, com `"Sem restrição"` ou `"Otimizada"` e o botão `"Liberar"` quando otimizada.
   - Ela só aparece com a porta de permissões, ou seja, no Android.
   - "Liberar" abre o diálogo do sistema (`ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`), o que exige `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` no manifesto. É aceitável fora da Play Store.
   - O estado é relido quando o app volta, pelo aviso de dados mudados que já existe.
   - **Não** entra no aviso do Hoje, que continua só com notificações e alarmes exatos.
10. **Samsung:** o README ganha os ajustes manuais:
    - Bateria → "Sem restrições";
    - tirar o app de "Apps em suspensão" e de "Apps em suspensão profunda".
    Eles também entram na verificação manual.

## Global Constraints

- Tudo das Fases 1–8 continua valendo:
  - `TreatWarningsAsErrors`, `Nullable`, versões centralizadas;
  - Core sem dependências; App sem referência a Platform.Linux nem ao Android;
  - textos em pt-BR e identificadores em inglês;
  - ViewModels sem tipos visuais, bindings compilados, cores do tema só por `DynamicResource`, alvos de toque ≥ 44 px.
- Textos exatos:
  - Sobre: `"Sobre"`, `"SlimeGoals 0.7.0"` (a versão vem do assembly), `"Licença: PolyForm Noncommercial 1.0.0"`, `"Licenças"`, `"← Configurações"`, `"← Licenças"`;
  - Bateria: `"Bateria"`, `"Sem restrição"`, `"Otimizada"`, `"Liberar"`.
- Identidade: `dev.alcm.slimegoals`; APK final em `artifacts/slimegoals-<versão>-android.apk`.
- **Segurança:**
  - nenhuma chave, senha ou `*.keystore` entra no repositório: `.gitignore` ganha `*.keystore` e `*.jks`;
  - não use a chave real do autor, nem crie uma em `~/.android-keystores`;
  - nos testes, use `SLIMEGOALS_KEYSTORE=<scratchpad>/test.keystore` e uma senha de teste.
- **Nunca** mexa no app instalado em `~/.local/opt/slimegoals` nem no banco real `~/.local/share/SlimeGoals`; não use `pkill`.
- No emulador (`slimegoals-api36`), trocar o APK Debug pelo Release exige desinstalar, porque a assinatura é outra. Isso é permitido só no emulador e apaga os dados de lá.
- Branch `feat/fase-7c-android-distribuicao`. Commits: Conventional Commits com escopo `android`, `app`, `packaging` ou `specs`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Integração por PR.
- **Testes:**
  - `dotnet test --solution SlimeGoals.slnx` e `dotnet build SlimeGoals.Android.slnx -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk`, os dois sem avisos;
  - imagens com `dotnet run --project tools/SlimeGoals.Render -- <pasta no scratchpad> [phone|desktop|all]`.

## Review Focus

1. **Release com trimming quebra em tempo de execução,** por exemplo o Dapper mapeando `Goal`, a DI ou os bindings, e não na compilação. Criar uma meta, registrar e receber um lembrete têm que funcionar no APK Release do emulador. Coberto na Task 1 (*smoke test*).
2. **Recurso de licença ausente:** um arquivo do catálogo que não foi embutido faria a tela mostrar um texto vazio ou lançar exceção. Um teste lê cada item do catálogo e exige texto não vazio. Coberto na Task 2.
3. **Voltar do Android dentro do Sobre:** do texto volta para a lista, da lista volta para Configurações, e de Configurações volta para Hoje; não pode sair do app nem pular etapas. Coberto na Task 3 (`ShellViewModelTests`).
4. **Bateria liberada enquanto o app está em segundo plano:** ao voltar, a linha passa de "Otimizada" para "Sem restrição" sem reabrir a página. Coberto na Task 4, pelo `DataChangeNotifier`.
5. **Atualização por cima:** um APK 0.7.0 assinado com a mesma chave instala sobre ele mesmo e mantém os dados, e o `versionCode` é 700. Coberto na Task 1 (`aapt dump badging` e `adb install -r`).

---

## Mapa de arquivos

```
Directory.Build.props                                   (alterado: 0.7.0)
.gitignore                                              (alterado: *.keystore, *.jks)
packaging/android/make-keystore.sh, build.sh            (novos)
packaging/linux/build.sh                                (alterado: textos de src/SlimeGoals.App/Licenses)
packaging/linux/licenses/*                              (movidos para src/SlimeGoals.App/Licenses/)
src/SlimeGoals.Android/SlimeGoals.Android.csproj        (alterado: versionCode)
src/SlimeGoals.Android/Properties/AndroidManifest.xml   (alterado: REQUEST_IGNORE_BATTERY_OPTIMIZATIONS)
src/SlimeGoals.Android/Reminders/AndroidReminderPermissions.cs (alterado: bateria)
src/SlimeGoals.App/SlimeGoals.App.csproj                (alterado: EmbeddedResource)
src/SlimeGoals.App/Licenses/*.txt                       (movidos + novos do Android)
src/SlimeGoals.App/About/LicenseCatalog.cs, LicenseTexts.cs (novos)
src/SlimeGoals.App/Hosting/IReminderPermissions.cs      (alterado)
src/SlimeGoals.App/ViewModels/PageViewModel.cs, ShellViewModel.cs, GoalsPageViewModel.cs, SettingsPageViewModel.cs (alterados)
src/SlimeGoals.App/Views/SettingsView.axaml             (alterado)
tests/SlimeGoals.App.Tests/About/LicenseCatalogTests.cs (novo)
tests/SlimeGoals.App.Tests/ViewModels/SettingsPageViewModelTests.cs, ShellViewModelTests.cs (alterados)
README.md, THIRD-PARTY-NOTICES.md, specs/Planejamento_v3.md (alterados)
```

---

### Task 1: APK Release assinado

**Files:**
- Modify: `Directory.Build.props`, `.gitignore`, `src/SlimeGoals.Android/SlimeGoals.Android.csproj`
- Create: `packaging/android/make-keystore.sh`, `packaging/android/build.sh`

**Interfaces:**
- Produces:
  - `packaging/android/build.sh` gera `artifacts/slimegoals-<versão>-android.apk`. Ele lê `SLIMEGOALS_KEYSTORE` (padrão `$HOME/.android-keystores/slimegoals.keystore`) e `SLIMEGOALS_KEYSTORE_PASS` (obrigatória), e também `ANDROID_HOME` (padrão `$HOME/Android/Sdk`) e `JAVA_HOME` (padrão `/usr/lib/jvm/java-21-openjdk`).
  - `make-keystore.sh` cria a chave no mesmo caminho padrão.

- [ ] **Step 1: Versão.**
  - Em `Directory.Build.props`, `<Version>0.7.0</Version>`.
  - No csproj do Android, `ApplicationVersion` é calculado da `$(Version)` por funções de propriedade do MSBuild (`[System.Version]::Parse`, `[MSBuild]::Multiply`/`Add`), seguindo a decisão 2.
  - **Verifique:** `dotnet build SlimeGoals.Android.slnx …` sem avisos. Depois, `$ANDROID_HOME/build-tools/<versão>/aapt dump badging <apk Debug>` mostra `versionCode='700' versionName='0.7.0'`.
- [ ] **Step 2: Scripts.**
  - `make-keystore.sh`:
    - usa `set -euo pipefail`;
    - recusa rodar se o arquivo já existe;
    - cria a pasta com `chmod 700`;
    - roda `keytool -genkeypair -v -keystore … -alias slimegoals -keyalg RSA -keysize 4096 -validity 10000 -dname "CN=SlimeGoals"`;
    - pede a senha de forma interativa, ou usa `SLIMEGOALS_KEYSTORE_PASS` se estiver definida, com `-storepass:env` e `-keypass:env`;
    - no fim, lembra de guardar uma cópia da chave.
  - `build.sh`:
    - lê a versão como o `packaging/linux/build.sh`;
    - falha com mensagem clara sem `SLIMEGOALS_KEYSTORE_PASS` ou sem o arquivo da chave;
    - roda `dotnet publish src/SlimeGoals.Android -c Release -f net10.0-android -p:RuntimeIdentifiers="android-arm64;android-x64" -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=… -p:AndroidSigningKeyAlias=slimegoals -p:AndroidSigningStorePass=env:SLIMEGOALS_KEYSTORE_PASS -p:AndroidSigningKeyPass=env:SLIMEGOALS_KEYSTORE_PASS -p:AndroidSdkDirectory=… -p:JavaSdkDirectory=…`;
    - copia o `*-Signed.apk` para `artifacts/slimegoals-<versão>-android.apk`;
    - roda `apksigner verify --print-certs` nele.
  - Em `.gitignore`, acrescente `*.keystore` e `*.jks`.
- [ ] **Step 3: Gerar com chave de teste.**
  - Rode `SLIMEGOALS_KEYSTORE=<scratchpad>/test.keystore SLIMEGOALS_KEYSTORE_PASS=<teste> packaging/android/make-keystore.sh` e depois o `build.sh` com as mesmas variáveis.
  - **Expected:** o APK sai, e o `apksigner` mostra o certificado `CN=SlimeGoals`.
  - Rode `make-keystore.sh` de novo no mesmo caminho. **Expected:** recusa, e o arquivo continua igual.
- [ ] **Step 4: Smoke test no emulador** (`slimegoals-api36`, com cold start se ele não estiver rodando):
  1. Desinstale o Debug e instale o APK com `adb install`.
  2. Abra o app e dê as permissões pelo `appops`/`pm grant`, como no README.
  3. Crie uma meta de quantidade com lembrete a cada 1 min numa janela que inclua agora.
  4. Registre um +1.
  5. Encerre com `am kill` e espere o lembrete.
  6. Toque "+1" na notificação.
  7. Reabra e confira o registro.
  8. Rode `adb install -r` do mesmo APK e confira que a meta continua.
  - Se algo quebrar por trimming, aplique a decisão 5 e registre no relatório o assembly e a correção.
- [ ] **Step 5: Commit** — `feat(packaging): APK Release assinado do Android` (com o csproj, `Directory.Build.props` e `.gitignore`).

---

### Task 2: Licenças embutidas e catálogo

**Files:**
- Move: `packaging/linux/licenses/*` → `src/SlimeGoals.App/Licenses/` (com `git mv`)
- Modify: `src/SlimeGoals.App/SlimeGoals.App.csproj`, `packaging/linux/build.sh`
- Create: `src/SlimeGoals.App/About/LicenseCatalog.cs`, `src/SlimeGoals.App/About/LicenseTexts.cs`, os textos novos do Android em `src/SlimeGoals.App/Licenses/`
- Test: `tests/SlimeGoals.App.Tests/About/LicenseCatalogTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace SlimeGoals.App.About;
/// Um componente com a sua licença. Where: "Linux e Android", "Linux" ou "Android".
public sealed record LicenseEntry(string Component, string License, string File, string Where);
public static class LicenseCatalog
{
    /// O primeiro item é o próprio SlimeGoals (LICENSE.md, "PolyForm Noncommercial 1.0.0", "Linux e Android").
    public static IReadOnlyList<LicenseEntry> All { get; }
}
public static class LicenseTexts
{
    /// Lê o recurso embutido "SlimeGoals.Licenses.<file>"; lança FileNotFoundException se não existir.
    public static string Read(string file);
}
```

- [ ] **Step 1: Write the failing tests**
  - `Catalog_starts_with_slimegoals`: `All[0]` é `("SlimeGoals", "PolyForm Noncommercial 1.0.0", "LICENSE.md", "Linux e Android")`.
  - `Every_catalog_file_is_embedded`: para cada item, `LicenseTexts.Read(File)` não é vazio.
  - `Read_unknown_file_throws`: `Read("nada.txt")` lança `FileNotFoundException`.
  - `Every_embedded_license_is_listed`: todo recurso `SlimeGoals.Licenses.*` do assembly aparece em algum `File` do catálogo, para não sobrar texto que nenhuma tela mostra.
- [ ] **Step 2: Run to verify they fail** (compilação).
- [ ] **Step 3: Implement.**
  - Mova os textos com `git mv`.
  - No csproj:
    - `<EmbeddedResource Include="Licenses\*.txt" LogicalName="SlimeGoals.Licenses.%(Filename)%(Extension)" />`;
    - o `LICENSE.md` da raiz, linkado com `LogicalName` `SlimeGoals.Licenses.LICENSE.md`.
  - Em `packaging/linux/build.sh`, copie de `src/SlimeGoals.App/Licenses/` para `licenses/` do pacote. O nome da pasta dentro do `.tar.gz` não muda.
  - O catálogo segue as tabelas de `THIRD-PARTY-NOTICES.md` ("Distribuídos no pacote" e "Distribuídos junto com o código"):
    - Tmds.DBus.Protocol fica com "Linux";
    - os itens novos do Android ficam com "Android".
  - Para o Android:
    - liste o APK da Task 1 com `unzip -l`;
    - confira os `LICENSE`/`*.txt` em `~/.nuget/packages/` dos pacotes `xamarin.androidx.*`, `xamarin.kotlin.stdlib*` e `avalonia.android`, e do runtime do .NET for Android (`microsoft.android.runtime.*`);
    - crie os textos que faltarem, por exemplo `MIT-dotnet-android.txt` (do pacote, não de memória). O `Apache-2.0.txt` já existente serve para AndroidX e Kotlin;
    - registre no relatório o que encontrou no APK.
- [ ] **Step 4: Run tests.** Expected: PASS. Rode também `packaging/linux/build.sh` e confira, com `tar -tzf`, que o `licenses/` do pacote tem os mesmos arquivos de antes, mais os novos. Apague o `artifacts/` gerado depois.
- [ ] **Step 5: Commit** — `feat(app): licenças embutidas e catálogo de componentes`

---

### Task 3: Seção "Sobre" e Voltar genérico

**Files:**
- Modify: `src/SlimeGoals.App/ViewModels/PageViewModel.cs`, `ShellViewModel.cs`, `GoalsPageViewModel.cs`, `SettingsPageViewModel.cs`, `src/SlimeGoals.App/Views/SettingsView.axaml`
- Test: `tests/SlimeGoals.App.Tests/ViewModels/SettingsPageViewModelTests.cs`, `tests/SlimeGoals.App.Tests/ShellViewModelTests.cs`

**Interfaces:**
- Consumes: `LicenseCatalog.All`, `LicenseTexts.Read` (Task 2).
- Produces, em `SettingsPageViewModel`:
  - um parâmetro opcional novo no fim do construtor, `string? appVersion = null`. Nulo vale a `AssemblyInformationalVersion` do App, sem o sufixo depois de `+`;
  - `string VersionText` (`"SlimeGoals " + versão`);
  - `string LicenseText` (`"Licença: PolyForm Noncommercial 1.0.0"`);
  - `IReadOnlyList<LicenseEntry> Licenses`;
  - `[ObservableProperty] bool LicensesOpen`;
  - `[ObservableProperty] LicenseEntry? SelectedLicense`;
  - `string? SelectedLicenseText`;
  - os comandos `OpenLicensesCommand`, `CloseLicensesCommand` e `CloseLicenseTextCommand`;
  - `override bool TryGoBack()`.
- `PageViewModel` ganha `public virtual bool TryGoBack() => false;`.
- `GoalsPageViewModel` ganha `public override bool TryGoBack() => CloseEditorIfOpen();`.

- [ ] **Step 1: Write the failing tests**
  - `About_shows_version`: com `appVersion: "0.7.0"`, `VersionText` é `"SlimeGoals 0.7.0"` e `LicenseText` é `"Licença: PolyForm Noncommercial 1.0.0"`.
  - `Licenses_open_list_then_text`:
    - `OpenLicensesCommand` → `LicensesOpen`;
    - `SelectedLicense = Licenses[0]` → `SelectedLicenseText` começa com `"Required Notice:"`;
    - `CloseLicenseTextCommand` → `SelectedLicense` nulo e a lista continua aberta;
    - `CloseLicensesCommand` → `LicensesOpen` falso.
  - `TryGoBack_closes_text_then_list`: com o texto aberto, o primeiro `TryGoBack()` dá verdadeiro e fecha o texto; o segundo dá verdadeiro e fecha a lista; o terceiro dá falso.
  - `Sections_follow_capabilities` (já existe) continua passando sem mudança: o Sobre não altera `IsEmpty`.
  - `ShellViewModelTests.GoBack_walks_out_of_settings_licenses`: na página Configurações com o texto aberto, `GoBack()` dá verdadeiro três vezes (texto, lista, Hoje) e depois falso.
  - Os testes `GoBack_closes_goal_editor_first` e `GoBack_returns_to_today_then_lets_system_handle` continuam passando.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.**
  - No VM, `SelectedLicenseText` vem de `LicenseTexts.Read`, recalculado quando muda `SelectedLicense`. Se a leitura lançar, o texto vira `"Não foi possível abrir a licença."` e o erro vai para o `Trace`.
  - Em `SettingsView.axaml`, três estados na mesma página:
    1. as seções de sempre, mais o card "Sobre" no fim;
    2. a lista de licenças, com o botão-link `"← Configurações"` e uma linha por item: componente, licença e "Onde" em `Muted`. Cada linha é um botão de largura toda, com ≥ 44 px;
    3. o texto, com `"← Licenças"`, o nome do componente e o texto em `SelectableTextBlock` com `TextWrapping="Wrap"` e fonte monoespaçada, dentro de um `ScrollViewer`.
  - Siga o padrão visual dos cards e do link "← Metas" do editor.
- [ ] **Step 4: Run tests** e Render `all`. Abra `settings-phone.png` e `settings-desktop.png` e confira que o card Sobre aparece no fim. O Render não abre a lista; deixe isso para a verificação manual.
- [ ] **Step 5: Commit** — `feat(app): seção Sobre com versão e licenças`

---

### Task 4: Otimização de bateria

**Files:**
- Modify: `src/SlimeGoals.App/Hosting/IReminderPermissions.cs`, `src/SlimeGoals.App/ViewModels/SettingsPageViewModel.cs`, `src/SlimeGoals.App/Views/SettingsView.axaml`, `src/SlimeGoals.Android/Reminders/AndroidReminderPermissions.cs`, `src/SlimeGoals.Android/Properties/AndroidManifest.xml`, os falsos de teste que implementam `IReminderPermissions` (em `SettingsPageViewModelTests`, `TodayPageViewModelTests` e no Render `DeniedPermissions`)
- Test: `tests/SlimeGoals.App.Tests/ViewModels/SettingsPageViewModelTests.cs`

**Interfaces:**
- `public sealed record ReminderPermissionStatus(bool Notifications, bool ExactAlarms, bool BatteryUnrestricted = true);`
- `IReminderPermissions` ganha `void RequestBatteryUnrestricted();`.
- `SettingsPageViewModel` ganha:
  - `[ObservableProperty] bool BatteryUnrestricted` (começa verdadeiro);
  - `string BatteryText` (`"Sem restrição"` ou `"Otimizada"`);
  - `bool CanRequestBattery`;
  - `RequestBatteryCommand`.
  - O `RefreshPermissions` também atualiza a bateria.

- [ ] **Step 1: Write the failing tests**
  - `Settings_shows_battery_state`:
    - com o status `(true, true, false)`: `BatteryText` é `"Otimizada"` e `CanRequestBattery` é verdadeiro;
    - `RequestBatteryCommand` → `FakePermissions.BatteryRequests` vale 1.
  - `Settings_rechecks_battery_on_data_change`: o status muda para `(true, true, true)`, `changes.NotifyChanged()` → `"Sem restrição"` e `CanRequestBattery` falso.
  - `Today_warning_ignores_battery`, em `TodayPageViewModelTests`: com o status `(true, true, false)`, `HasPermissionWarning` é falso.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.**
  - VM e linha "Bateria" na view, no mesmo molde de "Alarmes exatos".
  - Adaptador Android:
    - `BatteryUnrestricted` vem de `((PowerManager)context.GetSystemService(Context.PowerService)!).IsIgnoringBatteryOptimizations(context.PackageName)`;
    - `RequestBatteryUnrestricted` abre `Settings.ActionRequestIgnoreBatteryOptimizations` com `package:<pacote>` pela `CurrentActivity`. Se a Activity não resolver, cai em `Settings.ActionIgnoreBatteryOptimizationSettings`.
  - Manifesto: `<uses-permission android:name="android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS" />`.
- [ ] **Step 4: Run tests.** Depois, no emulador, com o APK da Task 1 gerado de novo:
  - Configurações mostra "Bateria — Otimizada";
  - "Liberar" abre o diálogo; aceitar e voltar mostra "Sem restrição";
  - `adb shell dumpsys deviceidle whitelist` lista `dev.alcm.slimegoals`.
- [ ] **Step 5: Commit** — `feat(android): aviso e atalho da otimização de bateria`

---

### Task 5: README, avisos de terceiros e spec

**Files:**
- Modify: `README.md`, `THIRD-PARTY-NOTICES.md`, `specs/Planejamento_v3.md`

- [ ] **Step 1: README.**
  - Troque o banner antigo ("Fases 0 e 1 concluídas") pelo estado atual.
  - Crie a seção "Instalar no Android" com:
    1. criar a chave uma vez (`packaging/android/make-keystore.sh`) e guardar uma cópia;
    2. `SLIMEGOALS_KEYSTORE_PASS=… packaging/android/build.sh`;
    3. instalar por `adb install` com depuração USB, ou copiando o APK e permitindo "instalar apps desconhecidos";
    4. primeira abertura: "Permitir" no Hoje e "Liberar" em Configurações → Bateria;
    5. no Samsung: Configurações → Aplicativos → SlimeGoals → Bateria → "Sem restrições", e conferir que o app não está em Bateria → Limites de uso em segundo plano → "Apps em suspensão" nem em "Apps em suspensão profunda";
    6. atualizar: mesmo comando, `adb install -r`. **Com outra chave, só desinstalando, o que apaga os dados.**
  - Na tabela do roteiro, a linha 7c fica "✅ implementado · ⏳ aparelho real".
- [ ] **Step 2: THIRD-PARTY-NOTICES.md.**
  - Atualize os caminhos de `packaging/linux/licenses` para `src/SlimeGoals.App/Licenses`.
  - Crie a seção "Distribuídos no APK do Android", com os componentes e arquivos da Task 2.
  - Diga que o app mostra tudo em Configurações → Sobre → Licenças.
- [ ] **Step 3: Spec** (`Planejamento_v3.md`).
  - Linha 7c da §12: "Implementado (verificação no aparelho pelo autor pendente)", mais um resumo.
  - "Pendências": a versão mínima fica definida como API 26.
  - Tabela de riscos: Samsung coberto pelos ajustes manuais e pela linha Bateria.
  - §9.9: a linha Bateria.
  - §15: as "Decisões da Fase 7c" (as 10 deste plano, mais o que a Task 1 decidiu sobre trimming).
- [ ] **Step 4: Verify.** `dotnet test --solution SlimeGoals.slnx`, o build Android e `git grep -n "packaging/linux/licenses"`, que não pode deixar nenhuma referência antiga.
- [ ] **Step 5: Commit** — `docs(specs): Fase 7c no README, avisos e spec`

---

## Verificação manual (autor, depois do PR aberto)

No computador:
1. `packaging/android/make-keystore.sh`: escolha uma senha e guarde uma cópia de `~/.android-keystores/slimegoals.keystore`.
2. `packaging/android/build.sh` (pede a senha) → `artifacts/slimegoals-0.7.0-android.apk`.

No Samsung:
1. Instale o APK (por `adb install` ou copiando o arquivo).
2. Abra o app: o Hoje mostra o aviso de permissões, e "Permitir" pede notificações e alarmes exatos.
3. Em Configurações → Bateria, toque "Liberar" e aceite: a linha passa a "Sem restrição". Confira também os ajustes da Samsung no README.
4. Em Configurações → Sobre, aparece "SlimeGoals 0.7.0". "Licenças" abre a lista, e tocar num item mostra o texto. O Voltar do sistema volta texto → lista → Configurações → Hoje. Abra também os dois avisos grandes (cerca de 140 KB) e role-os com o toque.
5. Crie uma meta com lembrete a cada 15 min, bloqueie a tela e deixe o celular parado por 1 h ou mais: os lembretes chegam no horário, com "+1", "Adiar" e "Ignorar" funcionando sem abrir o app.
6. Reinicie o celular sem abrir o app: o próximo lembrete chega.
7. Toque um campo de texto no formulário de meta e feche o teclado: as margens continuam (correção do PR #18 num teclado de verdade).
8. No Hoje, o slime anima e para depois de cerca de 1 minuto. A bateria do app em Configurações do Samsung não mostra uso alto.
