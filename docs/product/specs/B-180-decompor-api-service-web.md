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

- [x] Services por domínio com responsabilidade clara.
- [x] `api.service.ts` ≤ 200 linhas ou removido com migração completa.
- [x] `npm test` (Vitest) e `ng build` verdes.
- [ ] E2E smoke (DevAuth + envio) verde. O refactor não muda contrato HTTP nem a UI; o smoke de browser fica para o CI do PR.

## Testes

- Testes unitários existentes do web atualizados/migrados.
- E2E Playwright do caminho principal.

## Riscos

- Imports circulares entre services — manter dependências unidirecionais
  (domínio → `HttpClient`, não entre domínios).

## Execução — 2026-10-02

- Work-Item: B-180; Wave: W19-3; Trilha: D; Risk: R1.
- `api.service.ts` com 160 linhas. Chamadas em `profile`, `directory`,
  `messaging`, `files`, `admin`, `notifications`, `search` e `ai`.
  `HttpApiClient` concentra `fetch`, token e `X-Dev-User`.
- Injeções de `ApiService` na UI, stores e hub permanecem. Sem mudança de
  contrato HTTP.
- Verificação em `node:22.22.3`: `tsc -p tsconfig.app.json --noEmit`,
  `npm test -- --watch=false` (**70** arquivos, **323** testes) e `ng build`
  verdes. O aviso de locale `pt-BR` e os budgets de CSS já existiam.
- E2E smoke de browser não rodou neste PR. O caminho DevAuth + envio não
  mudou de contrato; fica para o CI.
