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

- [ ] `Program.cs` ≤ 500 linhas; handlers agrupados por fronteira.
- [ ] `dotnet build` e `task test` verdes.
- [ ] `task test:integration` e `task test:security` verdes.
- [ ] Nenhuma rota removida ou renomeada.

## Testes

- Suítes existentes de integration, security e arch tests — sem regressão.
- Smoke manual opcional: login DevAuth + envio de mensagem.

## Riscos

- Conflito com PRs abertos que tocam `Program.cs` — coordenar merge ou esperar
  B-174.
- Regressão sutil em ordem de middleware — validar com testes de auth/tenant.
