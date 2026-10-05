# B-183 — Arch test: limite de linhas por arquivo

> Wave W19-6 · Trilha E/G · Deps: W19-1…W19-5 · Risco R0

## Problema

Após a Wave 19, arquivos monolíticos podem voltar a crescer sem gate automático.
Sem limite verificável na CI, a dívida estrutural se acumula de novo.

## Escopo

- Arch test (ou script em `tests/architecture`) que falha quando arquivos de
  código-fonte excedem limite de linhas.
- Limites iniciais sugeridos (ajustáveis no PR, mas não relaxar sem justificativa):
  - `apps/api/Program.cs`: ≤ 500
  - registradores/wiring em `Infrastructure/`: ≤ 600
  - services/stores/components em `apps/web/src`: ≤ 400
- Exclusões documentadas: `node_modules`, `bin/`, `obj/`, migrations EF
  (`Migrations/`, `*Designer.cs`, `*Snapshot.cs`), arquivos gerados,
  `*.spec.ts` de fixtures muito longas (se necessário, listar allowlist).
- Mensagem de falha indica arquivo, contagem e limite.

## Fora de escopo

- Limites de complexidade ciclomática ou métricas além de linhas.
- Aplicar limite a arquivos de teste de integração grandes (podem entrar em
  follow-up).
- Refatorar código — isso é W19-1…W19-5.

## Contratos

Sem mudança de API. Documentar limites em `docs/architecture/diagrama-modulos.md`
ou comentário no próprio arch test.

## Aceite

- [x] Arch test roda em `task test:architecture` e na CI.
- [x] Baseline pós-W19 respeitada (sem falso positivo).
- [x] Exclusões listadas e revisáveis.

## Execução — 2026-10-05

- Work-Item: B-183; Wave: W19-6; Trilha: E/G; Risk: R0.
- Deps: W19-1…W19-5 já Done.
- `SourceLineLimits` classifica `Program.cs` (≤ 500), registradores
  `Infrastructure.cs` / `*ServiceCollectionExtensions.cs` (≤ 600) e
  services, stores e componentes em `apps/web/src` (≤ 400).
- Arquivos web que já excediam 400 linhas antes do gate ficam com teto
  congelado na data da baseline; não podem crescer. Adapters de
  Infrastructure fora do padrão de registrador não entram no teto de 600
  (B-179 deixou essa implementação de fora).
- Caso sintético cobre violação, limite exato, exclusões e crescimento
  acima do teto congelado.

## Testes

- O próprio arch test é o entregável; incluir caso sintético ou fixture mínima
  que prova detecção de violação.

## Riscos

- Falsos positivos em arquivos legítimos — manter allowlist explícita e pequena.
