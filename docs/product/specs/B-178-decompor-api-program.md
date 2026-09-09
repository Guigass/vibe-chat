# B-178 — Decompor composition root da API (`Program.cs`)

> Wave W19-1 · Trilha B · Deps: B-174 (recomendado) · Risco R1

## Problema

`apps/api/Program.cs` concentra ~5,5k linhas: bootstrap, DI, middleware, dezenas
de Minimal API handlers e configuração SignalR. Isso dificulta review, aumenta
conflitos de merge e mistura responsabilidades que já têm fronteiras de módulo
em `modules/*`.

## Escopo

- Extrair mapeamento de endpoints para extensões/pastas por fronteira (ex.:
  `Endpoints/MessagingEndpoints.cs`, `Endpoints/DirectoryEndpoints.cs`).
- Manter `Program.cs` como composition root fino: bootstrap, pipeline, `Map*`
  por módulo e registro de serviços de alto nível.
- Mover helpers locais (resolvers, mapeadores) para arquivos coesos junto da
  fronteira correspondente.
- Preservar rotas, status codes, authZ e contratos existentes byte-a-byte no
  comportamento observável.
- Meta: `Program.cs` ≤ 500 linhas após a decomposição.

## Fora de escopo

- Mudar contratos HTTP/SignalR ou permissões.
- Implementar B-174 (filtro `RequirePermission`) — deve preceder ou integrar
  neste PR se ainda não estiver `Done`.
- Novos endpoints ou refatoração de domínio em `modules/*`.

## Contratos

Sem mudança de API pública. Se o padrão de registro de endpoints mudar, atualizar
`docs/architecture/diagrama-modulos.md` (composition root).

## Multi-tenant e authZ

Mover código sem alterar ordem de middleware, `TenantContext` nem checagens de
permissão existentes.

## Aceite

- [x] `Program.cs` ≤ 500 linhas; handlers agrupados por fronteira.
- [x] `dotnet build` e `task test` verdes.
- [x] `task test:integration` e `task test:security` verdes.
- [x] Nenhuma rota removida ou renomeada.

## Testes

- Suítes existentes de integration, security e arch tests — sem regressão.
- Smoke manual opcional: login DevAuth + envio de mensagem.

## Riscos

- Conflito com PRs abertos que tocam `Program.cs` — coordenar merge ou esperar
  B-174.
- Regressão sutil em ordem de middleware — validar com testes de auth/tenant.

## Execução — 2026-09-09

- Work-Item: B-178; Wave: W19-1; Trilha: B; Risk: R1.
- Prioridade: override humano explícito da ordem W10; B-174 já Done.
- Base: `ebf70685117bbfa9381c242dadc7eb3c3da720f0`.
- Plano: extrair handlers e helpers por fronteira, manter bootstrap/pipeline,
  conservar tipos públicos e ordem dos registros, verificar equivalência e
  executar build + unit + architecture + integration + security em Docker.
- Superfícies: somente composição HTTP em `apps/api`, teste de registro das
  rotas e documentação de B-178. Nenhuma mudança em `modules/*`, persistência,
  permissões ou contratos públicos.
- Implementação: `Program.cs` com 220 linhas; maps em `Endpoints/`, helpers
  junto da fronteira, records em `Contracts/`, DevAuth em `Authentication/`.
  Maps múltiplos por fronteira mantêm a sequência original de registros.
- Verificação estrutural contra a base: corpos e ordem dos registros,
  36 helpers, 89 records e bootstrap/middleware equivalentes, descontando
  indentação e modificadores necessários à extração dos helpers.
- Regressão: `EndpointRegistrationIntegrationTests` compara as 85 rotas v1
  anteriores com o registro real, incluindo métodos, ordem, permissões,
  acesso anônimo e justificativas de exceção ao filtro.
- Gates locais: `dotnet build VibeChat.slnx --no-restore` verde;
  equivalentes de `task test`, `task test:architecture`,
  `task test:integration` e `task test:security` em SDK .NET 10 no Docker:
  **87 / 11 / 87 / 56 testes aprovados**, respectivamente; zero falhas/skips.
  Comando por suíte: `dotnet test tests/<suite>/*.csproj --no-build --no-restore
  --verbosity minimal --logger trx`. `git diff --check` verde.
- Ambiente: Postgres 16.6, Redis 7.4 e MinIO efêmeros na mesma rede do runner;
  o test host existente detecta os serviços no loopback. A tentativa inicial
  com Testcontainers em bridge falhou antes dos testes porque o fixture fixa
  o endpoint MinIO em `127.0.0.1`; nenhuma alteração no fixture foi necessária.
  O runner com rede host do Docker Desktop também não concluiu a comunicação
  do test runner e foi descartado. O teste de arquitetura recebeu o
  `apps/web/nginx.conf` exigido pelo seu check, ausente na primeira cópia.
- Build reporta aviso preexistente NU1903 em SSH.NET 2025.1.0, transitivo das
  dependências de teste. Nenhuma dependência foi alterada neste refactor.
- Artefatos locais: `artifacts/B-178/` (logs e TRX, fora do Git).
- Stop reason: `GOAL_MET` para implementação e gates locais; publicação/CI
  ficam registrados no PR. `Done` na branch só é autoritativo após merge.

### Follow-up dos gates — 2026-09-09

O PR #162 foi mergeado em `e9bfd5e`, com CI e QA aprovados. O snapshot
`EndpointRegistrationIntegrationTests.cs` está versionado nesse commit.
Apesar disso, os dois gates offline de autorização continuavam lendo apenas
`Program.cs` e passavam com zero mapas: os 11 testes de arquitetura verdes da
extração não demonstravam a cobertura desses gates.

- Correção R1, restrita a B-178: inventário recursivo dos C# de `apps/api`,
  excluindo `bin`/`obj`, incluindo `Endpoints/` e `GroupDmEndpoints.cs`.
- Inventário vazio falha. Cada mutação precisa da própria declaração encadeada;
  comentários, strings, helpers e permissões de endpoints seguintes não contam.
- Matriz validada por par método HTTP + path, não por simples menção ao path.
- Evidência antes/depois: os gates corrigidos inicialmente falharam para
  `PUT /me` sem exceção explícita e para as quatro rotas de DM em grupo ausentes
  da matriz. A exceção caller-only de locale foi declarada em metadata e no
  snapshot, sem mudar autorização efetiva; as quatro rotas foram documentadas.
- Sete casos de regressão do inventário; suite de arquitetura: **18/18**.
  Build, unit **87/87**, integration **87/87** (inclui snapshot de 85 rotas)
  e security **56/56** verdes em Docker; nenhum skip. Total: **248** testes.
- Logs/TRX: `artifacts/B-178-gates/`; `git diff --check` verde.
