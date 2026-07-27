# B-138 — Framework de bridges

> Wave 15 · Trilha B/C/E · Deps: B-136, D-21 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Conectar redes externas exige mapping de identidade, conversa, edição, delete e
arquivo, além de explicar que dados serão copiados.

## Escopo

- Bridge service contract fora do processo da API.
- Plumb explícito channel↔remote room.
- Remote identities marcadas e mapping opaco.
- Message/edit/delete/reaction/file capability negotiation.
- Loop prevention, idempotency e checkpoint.
- Consent banner, pause, disconnect e export de mapping.

## Fora de escopo

- Puppeting silencioso.
- Bridge global com acesso a todos os channels.
- Prometer delete remoto garantido.

## Contratos

Bridge protocol sobre eventos B-139 + scoped API; namespace/capabilities;
delivery state e remote event IDs.

## UX

Channel mostra bridge e destino, limitações e estado. Mensagem externa tem badge
e link; admin pode pausar.

## Multi-tenant e authZ

Scope por channel; identidade do bridge é Bot. Dados só saem após consentimento
e DLP; inbound resolve mapping autenticado.

## Aceite

- [ ] Loop não ocorre.
- [ ] Replay não duplica.
- [ ] Remote identity é distinguível.
- [ ] Disconnect bloqueia tráfego.
- [ ] Delete limitation é visível/auditada.

## Testes

Fake remote network, loop/replay/order, ACL/DLP, cross-tenant e resilience.

## Riscos

Perda de soberania e impersonation. Mapping marcado, scope mínimo, consentimento
e documentação de irreversibilidade.

