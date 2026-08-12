# B-180 — Decompor camada HTTP do web (`api.service.ts`)

> Wave W19-3 · Trilha D · Deps: — · Risco R1

## Problema

`apps/web/src/app/core/api/api.service.ts` concentra ~1,3k linhas com chamadas
HTTP para Directory, Messaging, Admin, Files, Search e demais domínios. Um único
service viola responsabilidade única e dificulta testes e manutenção.

## Escopo

- Extrair services por domínio (ex.: `messaging-api.service.ts`,
  `directory-api.service.ts`, `admin-api.service.ts`, `files-api.service.ts`).
- Manter `ApiService` como fachada fina (re-export ou delegação) durante a
  transição, ou substituir injeções gradualmente no mesmo PR.
- Preservar headers (`Authorization`, `Idempotency-Key`, `X-Dev-User`), tipagem
  forte e tratamento de erro existente.
- Meta: `api.service.ts` ≤ 200 linhas (fachada) ou removido se injeções
  migrarem integralmente.

## Fora de escopo

- Mudar contratos HTTP ou DTOs compartilhados.
- Refatorar stores (B-181) ou componentes de UI (B-182).
- Novas features de API.

## Contratos

Sem mudança de API pública. Tipos em `chat.models.ts` permanecem canônicos.

## Multi-tenant e authZ

Preservar propagação de token e headers de tenant; sem atalho cross-tenant.

## Aceite

- [ ] Services por domínio com responsabilidade clara.
- [ ] `api.service.ts` ≤ 200 linhas ou removido com migração completa.
- [ ] `npm test` (Vitest) e `ng build` verdes.
- [ ] E2E smoke (DevAuth + envio) verde.

## Testes

- Testes unitários existentes do web atualizados/migrados.
- E2E Playwright do caminho principal.

## Riscos

- Imports circulares entre services — manter dependências unidirecionais
  (domínio → `HttpClient`, não entre domínios).
