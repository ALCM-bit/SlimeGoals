# Segurança segundo a OWASP (v1)

> Conferência de 23/11/2026, a pedido do autor: "Garanta que o APP segue os princípios de segurança da OWASP". Vale junto com `specs/Sincronizacao_v1.md`, `specs/Endurecimento_do_servidor_v1.md` e `specs/Publicacao_na_tailnet_v1.md`.

## 1. Referências

- **Apps (Android e Linux):** OWASP MASVS v2, perfil L1, mais três controles do L2 e do perfil R pedidos depois pelo autor ("Pode fazer esses pontos que faltam"): pinning (O4), tapjacking (O5) e a conferência de integridade (O6). A ofuscação fica de fora: o código é aberto e o segredo do app não depende de esconder o binário.
- **Servidor de sincronização:** OWASP API Security Top 10 (2023) e os capítulos de configuração e de dados sensíveis do ASVS.

A conferência foi feita por leitura de código e configuração, mais `dotnet list package --vulnerable --include-transitive` (nenhum pacote vulnerável). Não houve teste dinâmico.

## 2. O que mudou

| # | Controle | Antes | Agora |
|---|---|---|---|
| O1 | MASVS-STORAGE-2 (teclado) | Os campos de senha só mascaravam o texto. Nada dizia ao teclado do sistema para não aprender o que se digitava nos códigos de recuperação, de confirmação e de convite. | Classe de estilo `secret` em `Controls.axaml`, com `TextInputOptions.ContentType=Password`, `IsSensitive=True` e `ShowSuggestions=False`. A classe vale nos 13 campos secretos de `AccountView`. Um teste reprova campo com `PasswordChar` sem a classe. |
| O2 | MASVS-PLATFORM-3 (captura de tela) | O código de recuperação e as senhas apareciam em capturas, gravações e na miniatura dos recentes. | Decisão do autor: proteger só as páginas de conta. Com uma subpágina da conta à vista em Configurações, o Android liga o `FLAG_SECURE` (`ISecureScreen`, `AndroidSecureScreen`). Trocar de aba desliga a proteção, e voltar liga de novo. Para guardar o código, a pessoa usa o Copiar ou anota. O Linux não tem equivalente. |
| O3 | API8 / ASVS V14.4 (cabeçalhos) | As respostas não diziam que não podiam ir para cache. Só o Caddy mandava o HSTS. | Toda resposta do servidor sai com `Cache-Control: no-store` e `X-Content-Type-Options: nosniff`, inclusive a resposta de erro e as respostas de limite (`SecurityHeaders`). |
| O4 | MASVS-NETWORK-2 (pinning) | O cliente aceitava qualquer certificado que o sistema aceitasse. | Decisão do autor: só Let's Encrypt. `CertificatePins` fixa o SPKI das raízes ISRG Root X1, X2, YR e YE (até 2035, 2040 e 2045), e o `SyncHttp` só conecta se a validação do sistema passar e uma delas estiver no caminho validado. No Android a cadeia chega sem montar, só com os certificados apresentados: ela é montada de novo com a política da plataforma, sem refazer a revogação, para que uma raiz anexada à toa não valha. O loopback fica só com a validação do sistema. O Caddyfile usa só o Let's Encrypt (`acme_ca`), sem cair para o ZeroSSL. |
| O5 | MASVS-PLATFORM-3 (tapjacking) | Um app com sobreposição podia induzir toques nas telas de conta. | Junto com o `FLAG_SECURE` (O2), nas mesmas páginas: a janela descarta o toque que chega com outra janela por cima (`FilterTouchesWhenObscured`) e, da API 31 em diante, esconde as sobreposições de outros apps (`SetHideOverlayWindows`, permissão `HIDE_OVERLAY_WINDOWS`). |
| O6 | MASVS-RESILIENCE-1/2/4 (integridade) | Nada. | Decisão do autor: avisar e pausar a conta. No APK de distribuição, `AndroidAppIntegrity` confere o SHA-256 do certificado de assinatura (o da chave oficial) e se há depurador. Assinatura estranha ou depurador põem a conta em `UntrustedApp`: o cofre nem é lido, a sincronização não liga, nenhuma operação de conta sai, e a tela explica e oferece "Tentar de novo"; o aviso de sincronização parada da tela Hoje diz o motivo. Sair, nesse estado, só limpa o aparelho. Root (`su` visível ao app ou build `test-keys`) só gera aviso na tela de conta. Metas e lembretes funcionam sempre. O APK de desenvolvimento (chave de depuração) e o Linux não fazem a conferência. |

## 3. MASVS: onde cada controle está

| Controle | Como o app atende |
|---|---|
| STORAGE-1 | Os segredos da sessão ficam no cofre do sistema: no Android, a chave AES-256 fica no Keystore e o arquivo em `no_backup/cofre`; no Linux, no Secret Service. O banco fica em pasta privada (Android: `FilesDir`; Linux: arquivo 600 em pasta 700). |
| STORAGE-2 | `allowBackup=false` e `data_extraction_rules` excluem tudo do backup e da transferência entre aparelhos. O Trace nunca leva a mensagem da exceção (`TraceText`, com teste que reprova o código que a escreve). O código copiado vai como sensível e sai da área de transferência em 60 s. As notificações têm versão pública na tela de bloqueio. O1 e O2 cobrem o teclado e as capturas. |
| CRYPTO-1/2 | AES-256-GCM com nonce aleatório e dado autenticado por registro, subchaves por HKDF e Argon2id na senha. Comparação em tempo constante e chaves zeradas ao descartar. Nada de primitiva própria; vetores fixos conferidos em `CryptoSelfCheck`. |
| AUTH-1/2/3 | Token de aparelho por `Bearer`, guardado no cofre. Trocar a senha e apagar a conta pedem a senha de novo. O servidor revoga o aparelho e o token vence após 30 dias sem uso. |
| NETWORK-1 | Só `https://` (`ServerUrlRules`). O `http://` vale só para o loopback, e o Release do Android recusa texto puro (`network_security_config`). O cliente não segue redirecionamento e valida o certificado pelo sistema. |
| NETWORK-2 | O4. |
| PLATFORM-1 | Componentes exportados: a Activity principal (o "Parar" pede o código da instalação), o receiver de eventos do sistema (aceita só as ações protegidas) e o receiver de conferência da cripto (só no APK de desenvolvimento, exige `DUMP`). Os outros não são exportados. Todo `PendingIntent` é imutável. |
| PLATFORM-2 | Não há WebView. |
| PLATFORM-3 | O2 e O5. |
| CODE-1/3/4 | `targetSdk` 36. Dependências sem vulnerabilidade conhecida, com o Dependabot ligado. SQL sempre com parâmetros (Dapper). JSON por geração de código, com campo faltando tratado como erro. |
| RESILIENCE | O6, sem ofuscação. |
| PRIVACY | Sem analytics nem rastreador. O app funciona sem conta. Com conta, o conteúdo vai cifrado ponta a ponta, e só as permissões que o app usa são pedidas. |

## 4. Servidor: API Security Top 10

| Risco | Como o servidor atende |
|---|---|
| API1 (BOLA) | Toda consulta de registro e de aparelho filtra por `account_id` do token. Revogar o aparelho de outra conta dá 404. |
| API2 (autenticação) | `auth_key` e token guardados por hash e comparados em tempo constante. Limite por e-mail + IP e por falhas, `prelogin` que não revela quem tem conta, e cadastro só com convite. |
| API3 (propriedades) | As respostas são DTOs fechados, e os erros saem por código, sem e-mail nem detalhe interno. |
| API4 (recursos) | Limite de corpo, limites por IP e por aparelho, e cota de registros por conta. |
| API5 (funções) | Não há rota de administração. |
| API6 (fluxos sensíveis) | Convite para o cadastro e no máximo 3 e-mails de confirmação por hora. |
| API7 (SSRF) | O servidor não busca URL nenhuma. |
| API8 (configuração) | Página de erro genérica fora de `Development`, recusa subir com papel de banco privilegiado, contêineres endurecidos, HSTS no Caddy e O3. |
| API9 (inventário) | Rotas e implantação descritas em `docs/servidor.md`. |
| API10 (consumo de terceiros) | O SMTP exige TLS. |

## 5. Riscos aceitos

Os riscos aceitos na auditoria de 13/11/2026 continuam: o banco local não é cifrado (depende da cifra do disco), o Secret Service usa sessão `plain`, a senha em `string` não é zerada e o servidor vê metadados (quantidade, tamanho e horário dos registros). Entram também:

- **Manutenção dos pins (O4):** uma raiz nova do Let's Encrypt precisa entrar em `CertificatePins` antes de emitir sob ela; senão o app deixa de conectar até a próxima versão. O Let's Encrypt anuncia raízes com anos de antecedência, e a geração Y já está fixada. Sem o ZeroSSL de reserva, um Let's Encrypt fora do ar atrasa a renovação do Caddy em produção; o certificado vale 90 dias e o Caddy renova com 30 de folga. Servidor próprio com certificado de outra CA não conecta (`docs/servidor.md`).
- **Integridade (O6) é só um obstáculo:** quem controla o aparelho (root, Frida, binário modificado) desliga a conferência. Ela tira a sessão do caminho fácil de um APK refeito ou de um depurador, não de um atacante decidido. A detecção de root é heurística e o Magisk a esconde; por isso ela só avisa. Não há ofuscação nem conferência pelo Play Integrity, que exigiria um serviço do Google.
- **Linux sem tapjacking nem integridade:** o desktop não tem sobreposição de outro app nem assinatura de pacote que o app possa conferir.
