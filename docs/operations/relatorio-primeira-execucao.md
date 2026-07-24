# Relatório — Primeira execução (fundação VibeChat)

## Stack e versões

| Componente | Versão |
|---|---|
| .NET / ASP.NET Core | 10.0 LTS |
| Angular | 22 (standalone + Signals) |
| PostgreSQL | 16.6 |
| Redis | 7.4 |
| Keycloak | 26.0.2 |
| MinIO | RELEASE.2024-12-18 |

## Comandos executados

```bash
cp .env.example .env
docker compose up -d postgres redis minio createbucket keycloak
# API
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run --project apps/api
# Web
cd apps/web && ng serve --host 0.0.0.0 --port 4200
dotnet test tests/unit
dotnet test tests/architecture
dotnet test tests/integration
dotnet test tests/security
cd tests/e2e && E2E_AUTH_MODE=devauth npm test
```

## Resultados de testes

| Suíte | Resultado |
|---|---|
| Unit | 5 passed |
| Architecture | 2 passed |
| Integration | 5 passed |
| Security | 2 passed |
| E2E (duas sessões, DevAuth) | 1 passed |

## Evidências manuais da fatia vertical

- Health API: `Healthy` (postgres, redis, minio up)
- Workspace demo + canal `#geral` via seed
- Envio de mensagem persistido com sequência
- Idempotência no reenvio
- Resumo de IA (Mock) operacional
- Cursor de leitura / não lidas
- Admin dashboard com usuários, workspaces, canais, mensagens, outbox, saúde e versão
- Realtime: Alice envia → Bob recebe (E2E Playwright)

## Decisões relevantes

1. Monólito modular .NET 10 + Angular 22 (ADRs 001–003)
2. Outbox + sequência por conversa (ADR-010)
3. DevAuth (`X-Dev-User`) para desenvolvimento/E2E sem depender de OIDC
4. `compose.override.yaml` usa `network_mode: host` em ambientes com bridge Docker quebrada
5. Keycloak management port `9002` para não colidir com MinIO `:9000`
6. Licença provisória Apache-2.0 (decisão humana pendente)

## Limitações / pendências

- Observabilidade completa (Grafana/Prometheus/Loki/Tempo) sob profile `observability` — não exercitada nesta corrida
- RLS SQL provisionado; validação contínua via testes de isolamento
- Login OIDC Keycloak disponível, mas E2E oficial desta fatia usou DevAuth
- Decisões humanas: licença final, marca, retenção legal, credenciais de produção

## Próximos passos (paralelizáveis)

Ver `docs/roadmap/roadmap.md` e `docs/roadmap/backlog.md`.
