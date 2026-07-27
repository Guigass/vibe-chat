# B-141 — Cliente desktop

> Wave 15 · Trilha D/A/E · Deps: W10-9, D-20 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

PWA não cobre integração de sistema, update controlado e comportamento desktop
esperado em algumas organizações.

## Escopo

- Wrapper OSS fino sobre o web, escolhido por ADR.
- Windows, macOS e Linux conforme matrix.
- Deep links, notifications, tray, launch-at-login opt-in.
- Secure token storage e remote logout.
- Signed builds quando operador fornece credencial externa.
- Auto-update por feed configurável e verificável; off quando ausente.

## Fora de escopo

- Fork de UI.
- API privilegiada exclusiva.
- Certificado real no repositório.

## Contratos

Mesma API/SignalR/PWA semantics; custom URL scheme versionado; update manifest
assinado. Build reproduzível e SBOM.

## UX

Comportamento idêntico ao web, integração nativa progressiva e fallback no
browser. Permissões pedidas no contexto.

## Multi-tenant e authZ

Token no secure storage do OS; tenant do token. Logout remoto limpa sessão/cache.

## Aceite

- [ ] Contract/E2E parity com web.
- [ ] Token não fica em arquivo/log.
- [ ] Deep link valida host/destino.
- [ ] Update inválido é rejeitado.
- [ ] Build reproduzível por OS suportado.

## Testes

Contract suite, packaging smoke, protocol handler attacks, secure storage e
update signature.

## Riscos

Supply chain e divergência de cliente. Wrapper fino, CI matrix e fonte web única.

