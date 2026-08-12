# B-179 — Decompor registro de Infrastructure

> Wave W19-2 · Trilha B/A · Deps: W19-1 (recomendado) · Risco R1

## Problema

`src/VibeChat.Infrastructure/Infrastructure.cs` concentra ~3,1k linhas de
registro DI, adapters e wiring transversal. O arquivo mistura persistência,
Redis, MinIO, outbox, settings e resolvers — dificultando navegação e review.

## Escopo

- Dividir em registradores por área (ex.: `Persistence/`, `Redis/`,
  `Files/`, `Outbox/`, `RuntimeSettings/`) com método de extensão
  `Add*Infrastructure` por área.
- Manter `Infrastructure.cs` (ou `ServiceCollectionExtensions.cs` equivalente)
  como orquestrador fino que chama os registradores.
- Preservar lifetimes, interfaces e ordem de registro equivalente.
- Meta: nenhum arquivo de registro > 600 linhas após a decomposição.

## Fora de escopo

- Mudar implementação de adapters ou contratos.
- Mover domínio para `modules/*` (só wiring/DI em Infrastructure).
- Alterar migrations ou schema.

## Contratos

Sem mudança de API pública. Atualizar `diagrama-modulos.md` se a estrutura de
pastas de Infrastructure mudar materialmente.

## Multi-tenant e authZ

Preservar registro de `TenantContext`, RLS session e roles runtime sem
privilégio de bypass.

## Aceite

- [ ] Registradores por área; orquestrador fino.
- [ ] Nenhum arquivo de registro > 600 linhas.
- [ ] `task test` + testes de integração verdes.
- [ ] Compose profile `apps` sobe healthy (`task apps` smoke).

## Testes

- Integration tests existentes.
- Arch tests de fronteira de módulo, se aplicável.

## Riscos

- Ordem de registro DI sensível — comparar comportamento antes/depois com testes
  de integração.
