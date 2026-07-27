# B-137 — Registry assinado de plugins

> Wave 15 · Trilha B/C/D/E · Deps: B-136, D-18 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Instalação local não oferece descoberta, atualização e revogação governadas.

## Escopo

- Registry protocol aberto com catálogo oficial, privado ou allowlisted.
- Manifest, artifact references, checksum, assinatura e provenance.
- Compatibility/dependency resolution determinístico.
- Install/update/rollback/revoke e kill switch.
- Trust roots configuradas pelo operador.
- Security advisory e quarantine de versão.

## Fora de escopo

- Billing/checkout.
- Executar código não confiável in-process.
- Registry arbitrário habilitado por default.

## Contratos

`registry-index.v1` e pacote assinado; offline mirror; signatures verificadas
antes de persistir/ativar. ADR escolhe formato/algoritmo OSS.

## UX

Admin vê publisher, source, assinatura, capabilities, versão e risco antes de
instalar. Update nunca amplia capability silenciosamente.

## Multi-tenant e authZ

Trust config pode ser instance-level; instalação e scopes são tenant/workspace.
Plugin não recebe acesso até grant explícito.

## Aceite

- [ ] Assinatura/checksum inválidos bloqueiam.
- [ ] Capability nova exige confirmação/policy.
- [ ] Revogação desativa versão.
- [ ] Rollback restaura versão/config compatível.
- [ ] Mirror funciona sem serviço público.

## Testes

Signature vectors, tamper, downgrade/dependency, revocation, SSRF e E2E
install/update/rollback.

## Riscos

Supply-chain compromise. Trust roots mínimas, provenance, transparency log
quando viável e revogação rápida.

