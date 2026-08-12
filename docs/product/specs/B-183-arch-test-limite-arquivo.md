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

- [ ] Arch test roda em `task test:architecture` e na CI.
- [ ] Baseline pós-W19 respeitada (sem falso positivo).
- [ ] Exclusões listadas e revisáveis.

## Testes

- O próprio arch test é o entregável; incluir caso sintético ou fixture mínima
  que prova detecção de violação.

## Riscos

- Falsos positivos em arquivos legítimos — manter allowlist explícita e pequena.
