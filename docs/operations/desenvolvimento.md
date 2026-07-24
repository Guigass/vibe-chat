# Guia de Desenvolvimento — VibeChat

## Pré-requisitos

- Docker + Docker Compose v2
- .NET 10 SDK
- Node.js LTS (para Angular 22) + npm/pnpm conforme repo
- `make` ou scripts em `infra/scripts` (quando disponíveis)
- Acesso à rede para puxar imagens (Keycloak, Postgres, etc.)

## Subir o ambiente local

```bash
# Na raiz do repositório
cd /workspace

# Subir dependências + apps (quando compose estiver pronto)
docker compose -f infra/compose/docker-compose.yml up -d

# API (dev)
# cd apps/api && dotnet watch run

# Web (dev)
# cd apps/web && npm start
```

> Enquanto o Compose ainda está sendo montado pela fundação, siga os READMEs em `infra/compose` assim que existirem. Este guia define o **contrato de DX** esperado.

## Serviços locais esperados

| Serviço | Porta típica | Uso |
|---------|--------------|-----|
| Web | 4200 | Angular dev server |
| API | 5080 / 7080 | HTTP + SignalR |
| Postgres | 5432 | SoT |
| Redis | 6379 | Presence/backplane |
| Keycloak | 8080 | OIDC |
| MinIO | 9000 / 9001 | S3 + console |
| Grafana | 3000 | Dashboards |
| Prometheus | 9090 | Métricas |

Credenciais de **dev** devem viver em `.env.example` (nunca secrets de produção).

## Seed mínimo

1. Realm Keycloak `vibechat-dev` com 2 usuários (`alice`, `bob`)
2. Um tenant `acme`
3. Workspace `Acme HQ`
4. Channel `#geral`
5. Memberships para alice e bob

Script alvo: `infra/scripts/seed-dev.sh`

## Fluxo de trabalho do desenvolvedor

1. Criar branch a partir de `cursor/vibechat-foundation-*` / main conforme processo
2. Implementar no módulo correto + contratos se necessário
3. Testes unit/integration locais
4. Verificar lint/arch tests
5. Atualizar docs se decisão mudar (preferir ADR)

## Estrutura mental do código

```text
apps/api          → composition root HTTP/SignalR
apps/worker       → outbox/jobs
apps/web          → Angular
modules/*         → domínio
docs/*            → verdade de produto/arquitetura
```

## Debugging útil

- **Mensagem não chega no outro cliente:** ver outbox `processed_at`, traces Tempo, grupos SignalR
- **401 loop:** clock skew, audience, redirect URIs no Keycloak
- **RLS vazio:** `app.tenant_id` não setado na conexão
- **Duplicata:** Idempotency-Key ausente/diferente

## Convenções

- PT-BR para docs de usuário/ops; código em inglês (ids, classes)
- Sem microserviços novos sem ADR
- Não commitar binários, `node_modules`, secrets

## Testes rápidos

```bash
# Exemplo — ajustar quando soluções existirem
dotnet test tests/unit
dotnet test tests/integration
dotnet test tests/architecture
dotnet test tests/security
```

E2E: ver `tests/e2e` (Playwright).

## Leitura obrigatória antes de codar

- `docs/product/glossario.md`
- `docs/architecture/contratos.md`
- `docs/adrs/ADR-001` … `ADR-010`
- `docs/security/multi-tenant.md`
- `docs/agents/orientacoes.md`
