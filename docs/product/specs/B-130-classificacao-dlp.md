# B-130 — Classificação de dados e DLP

> Wave 14 · Trilha B/C/D/E · Deps: B-129, D-23 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Organizações não conseguem rotular sensibilidade nem impedir ações incompatíveis
com a política de dados.

## Escopo

- Labels built-in/custom: Public, Internal, Confidential, Restricted.
- Herança workspace→space→channel com override controlado.
- Rules para share, guest, export, webhook, AI, download e federation.
- Detectors OSS/regex configuráveis com false-positive workflow.
- Ação warn, block ou quarantine.
- Audit, métricas e simulation mode.

## Fora de escopo

- Inspecionar conteúdo E2EE.
- Garantir conformidade legal automática.
- Enviar conteúdo a detector SaaS por default.

## Contratos

Policy/Label/Detection tenant-scoped e versionados; decisão retorna reason code.
Hook central antes de efeitos externos; detectors não alteram body.

## UX

Label visível na conversa; bloqueio explica regra e recurso de revisão. Admin
simula impacto antes de enforce.

## Multi-tenant e authZ

Somente policy admins alteram. Detector e logs não cruzam tenant nem guardam
trecho completo desnecessário.

## Aceite

- [ ] Regra block impede efeito antes da outbox externa.
- [ ] Simulation não bloqueia e mede.
- [ ] Guest/AI/export respeitam label.
- [ ] Exceção é limitada, expira e auditada.
- [ ] E2EE é marcado “não inspecionável”.

## Testes

Policy matrix/property, detectors, false-positive review, cross-tenant e E2E
warn/block.

## Riscos

Falso positivo interromper trabalho e falso negativo gerar confiança excessiva.
Simulation, métricas e texto de limitação.

