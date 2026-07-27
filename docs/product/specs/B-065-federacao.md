# B-065 — Federação entre instâncias

> Wave 16 · Trilha B/C/A/E · Deps: B-138, B-146, D-21 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Organizações independentes não conseguem colaborar preservando suas próprias
instâncias e identidades.

## Escopo

- Federação server-to-server somente entre trust domains allowlisted.
- Domain identity, key rotation e signed events.
- Federated room/channel com membership explícita.
- Event ordering/dedupe, retries, partition recovery e state reconciliation.
- Remote identity/instance sempre visível.
- Pause/revoke trust e audit.

## Fora de escopo

- Federação pública aberta/discovery global.
- Garantir remote deletion/retention.
- Compartilhar channels locais implicitamente.

## Contratos

Protocol versioned separado da API client; canonical event IDs, signatures,
capabilities e compatibility. ADR compara adotar padrão aberto versus protocolo
mínimo próprio e deve preferir interoperabilidade OSS comprovada.

## UX

Convite mostra domínio, dados que serão copiados e limitações. Room federada tem
badge e trust state.

## Multi-tenant e authZ

Tenant/workspace escolhe trust permitido dentro da policy da instância. Inbound
é remote principal sem bypass; RLS na cópia local.

## Aceite

- [ ] Evento assinado inválido é rejeitado.
- [ ] Partition reconcilia sem duplicar.
- [ ] Revogar trust bloqueia tráfego futuro.
- [ ] Conteúdo local não autorizado não sai.
- [ ] Limitação de delete/retention é visível.

## Testes

Two-instance E2E, signature/replay, partition/clock, ACL/DLP, cross-tenant e
protocol compatibility.

## Riscos

Soberania, abuso e consistência distribuída. Allowlist, minimal state, assinatura
e operação off por default.

