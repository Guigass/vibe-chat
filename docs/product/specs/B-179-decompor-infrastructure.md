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

- [x] Registradores por área; orquestrador fino.
- [x] Nenhum arquivo de registro > 600 linhas.
- [x] `task test` + testes de integração verdes.
- [x] API sobe com o registro novo contra Postgres, Redis e MinIO (host de integração).

## Testes

- Integration tests existentes.
- Arch tests de fronteira de módulo, se aplicável.

## Riscos

- Ordem de registro DI sensível — comparar comportamento antes/depois com testes
  de integração.

## Execução — 2026-10-02

- Work-Item: B-179; Wave: W19-2; Trilha: B/A; Risk: R1.
- Deps: W19-1 já Done. Nenhum PR aberto implementava B-179.
- `Infrastructure.cs` ficou com 33 linhas e só chama `Add*Infrastructure`.
- Registradores por área, todos abaixo de 50 linhas. Adapters movidos sem
  alterar o corpo (DbContext e MessageWriter permanecem tipos únicos).
- Ordem de registro preservada; `InfrastructureRegistrationTests` trava a
  sequência da API e do worker.
- Verificação no SDK .NET 10 em container: build da solution verde;
  unit **152/152**, architecture **18/18**, integration **126/126**,
  security **82/82**. Zero falhas. Aviso preexistente NU1903 em SSH.NET.
- O host de integração sobe a API com `AddVibeChatInfrastructure` no data
  plane local (Postgres, Redis e MinIO healthy). `apps/web` não foi alterado.
