# Spike: criptografia e cofre de segredos da sincronização

06/11/2026 · Responde à seção 8 de `specs/Sincronizacao_v1.md`. O código foi descartável e não entrou no repositório.

## Pergunta

As peças de criptografia funcionam em Linux e Android com desempenho aceitável?

## Resultado

| Item | Critério | Resultado |
|---|---|---|
| `AesGcm` no Android | Cifra e decifra em API 26 e 36 | **Passou** nos dois. Blob e dado autenticado adulterados são recusados |
| Compatibilidade | Mesmos resultados entre plataformas | **Passou.** Vetores de AES-GCM, HKDF, HMAC e Argon2id idênticos em Linux, API 26, API 36 e no celular real (arm64) |
| Biblioteca de Argon2id | x86-64 e arm64, licença compatível | **Passou.** Konscious e BouncyCastle (MIT) dão o mesmo resultado em x86-64 e arm64 |
| Tempo do Argon2id | Até 3 s em celular intermediário | **Passou no aparelho disponível, um topo de linha:** de 652 a 714 ms. Celular intermediário não foi medido; a folga é de mais de 4 vezes |
| Android Keystore | Recupera depois de reiniciar | **Passou** em API 26, API 36 e no celular real, com reinício confirmado pelo tempo ligado |
| Secret Service | Guarda e recupera, com caminho claro sem cofre | **Passou** em chaveiro isolado. Sem cofre, o erro é `ServiceUnknown` |

## Tempos (Release, 64 MiB, 3 passadas, 1 faixa)

| Ambiente | Konscious | BouncyCastle |
|---|---|---|
| Linux, 16 núcleos | 540 ms | 621 ms |
| Emulador API 36, x86-64 | 450 ms | 1275 ms |
| Emulador API 26, x86-64 | 466 ms | 1062 ms |
| Samsung S23 Ultra (SM-S918B), arm64, Android 17 | 652 a 714 ms | 837 a 862 ms |

O emulador roda no processador do computador e não prevê o tempo de um celular. No S23 Ultra, as faixas vêm de três aberturas do app.

## Decisões para a implementação

1. **Argon2id:** Konscious com 64 MiB, 3 passadas e 1 faixa. Foi a mais rápida em todos os ambientes medidos.
2. **Secret Service:** direto pelo `Tmds.DBus.Protocol`, já usado no projeto, sem depender do `secret-tool`.
3. **Sessão `plain`:** o segredo passa sem cifra adicional pelo D-Bus local. Outro programa da mesma sessão do usuário já consegue pedir o segredo ao cofre, então a sessão cifrada não mudaria o que ele alcança.

## Armadilha

`MessageWriter` do Tmds é uma `struct`. Passá-la por `Action<MessageWriter>` escreve o corpo numa cópia, a mensagem sai malformada e o barramento fecha a conexão com `DBusConnectionClosedException`. Mensagens sem corpo funcionam, o que confunde. Use `ref` ou monte a mensagem no mesmo método, como em `TmdsNotificationBus`.

## Não testado

- Celular intermediário ou de entrada: o único aparelho real medido é topo de linha.
- Chaveiro travado: exige o fluxo de `Unlock` com prompt, a resolver na fase 9.4.
- KDE Wallet e outros servidores de Secret Service.
- Aparelho com pouca memória rodando Argon2id de 64 MiB.
