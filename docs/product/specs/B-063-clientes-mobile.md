# B-063 — Clientes mobile

> Wave 15 · Trilha D/A/E · Deps: B-141, D-20 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

PWA cobre uso básico, mas organizações podem exigir distribuição mobile,
notifications e integrações de sistema consistentes.

## Escopo

- App iOS/Android por wrapper/hybrid OSS escolhido em ADR.
- Mesma UI/contratos onde viável; componentes adaptativos.
- Secure storage, biometric lock opcional e remote logout.
- Deep links, share target, camera/file picker e Web Push/native bridge conforme
  capacidade sem vendor lock obrigatório no servidor.
- Support matrix e release reproducível.

## Fora de escopo

- Reescrever domínio em stack paralela.
- Feature mobile-only de negócio.
- Publicar em store sem conta/credencial externa.

## Contratos

Mesma API/events; device registration revogável; push provider adapter e payload
mínimo. Store signing é `External action`.

## UX

Mobile-first navigation, offline/error claros, a11y e battery/data awareness.

## Multi-tenant e authZ

Device/session por user/tenant, secure storage e revoke. Share target pede
destino e confirmação.

## Aceite

- [ ] Contract suite passa nos dois clients.
- [ ] Remote logout limpa tokens.
- [ ] Deep/share links não enviam ao tenant errado.
- [ ] Push não contém conteúdo sensível por default.
- [ ] Sem conta de store, artifact sideload/documentação ainda é gerado.

## Testes

Device/emulator E2E, storage, deep link, network transitions, push mocks e
security.

## Riscos

Store/vendor e fragmentação. Adapter, PWA canônica e paridade automatizada.

