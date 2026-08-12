# B-175 — Auditoria de cobertura authZ (membership vs permission)

> Wave W7-11 · Trilha E · Deps: B-174 (recomendado) · Decisões: — · Risco R2

## Problema

Parte dos endpoints valida apenas membership no workspace/canal, sem
`HasPermissionAsync`. Para leitura isso pode ser aceitável; para mutações ou
dados sensíveis, membership sozinha pode ser insuficiente. Não há matriz
publicada endpoint → gate aplicado.

## Escopo

- Inventariar todos os endpoints `/api/v1` e hub SignalR: gate de membership,
  permissão explícita, ou ambos.
- Publicar matriz em `docs/security/` (ou seção de `multi-tenant.md`) —
  endpoint × permissão esperada.
- Corrigir gaps onde mutação ou leitura sensível depende só de membership sem
  permissão adequada.
- Adicionar testes security negativos para cada gap corrigido (Member vs
  Auditor vs Admin conforme catálogo).

## Fora de escopo

- Implementar o filtro centralizado (B-174) — pode rodar em paralelo, mas
  correções devem seguir o padrão adotado em B-174 quando merged.
- Guests (B-040).

## Contratos

Sem mudança de contrato HTTP, exceto se um endpoint incorreto passa a retornar
403 — documentar em `contratos.md` se comportamento público muda.

## Multi-tenant e authZ

Cada teste negativo deve incluir caso cross-tenant e cross-workspace quando
aplicável.

## Aceite

- [ ] Matriz endpoint × authZ publicada e linkada no roadmap.
- [ ] Gaps de mutação/leitura sensível corrigidos ou justificados na matriz.
- [ ] Testes security cobrem regressões dos gaps fechados.
- [ ] `task test:security` verde.

## Evidência

- Matriz em docs + saída de `task test:security`.
