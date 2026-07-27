# Visão Geral da Arquitetura — VibeChat

## Estilo arquitetural

**Monólito modular** (ADR-001): um processo de API ASP.NET Core e um Worker compartilham o mesmo modelo de domínio e contratos, com fronteiras de módulo claras. Não há microserviços na fase 1.

```
┌─────────────────────────────────────────────────────────────┐
│                     Cliente Web (Angular 22)                │
│              Standalone + Signals + CDK + PWA               │
└───────────────────────────┬─────────────────────────────────┘
                            │ HTTPS / OIDC / SignalR (WSS)
┌───────────────────────────▼─────────────────────────────────┐
│                     apps/api  (.NET 10)                     │
│  Identity │ Directory │ Messaging │ Files │ Search │ AI …   │
└───────┬───────────┬──────────────┬──────────────┬───────────┘
        │           │              │              │
        ▼           ▼              ▼              ▼
   PostgreSQL     Redis         MinIO         Keycloak
   (SoT+RLS)   (cache/presença/  (S3)          (OIDC)
                backplane)
        │
        │ outbox
        ▼
┌───────────────────┐     ┌──────────────────────────────────┐
│   apps/worker     │────▶│ OpenTelemetry → Prom/Grafana/    │
│  (outbox, jobs)   │     │ Loki / Tempo                     │
└───────────────────┘     └──────────────────────────────────┘
```

## Componentes de runtime

| Componente | Papel |
|------------|--------|
| **apps/web** | SPA Angular 22; auth OIDC; UI de chat; hub SignalR |
| **apps/api** | HTTP + SignalR; orquestra módulos; valida authZ |
| **apps/worker** | Publica/consome outbox; jobs assíncronos |
| **PostgreSQL** | Source of truth; RLS; busca inicial (FTS) |
| **Redis** | Presence, typing, cache, rate-limit, SignalR backplane |
| **Keycloak** | IdP OIDC |
| **MinIO** | Object storage S3-compatible |
| **Stack OTel** | Traces, métricas, logs |

## Módulos de domínio e plataforma

Os nomes abaixo acompanham os assemblies em `modules/`. Algumas responsabilidades
de runtime permanecem na infraestrutura compartilhada, mas não devem atravessar
fronteiras de domínio silenciosamente.

| Módulo | Responsabilidade |
|--------|------------------|
| **Tenancy** | TenantContext e primitivas de isolamento |
| **Identity** | Usuários locais/espelho, sessão, claims, integração Keycloak |
| **Directory** | Workspaces, spaces, memberships e papéis |
| **Conversations** | Channels, DMs, threads e conversations |
| **Messaging** | Messages, reactions, seq, idempotência e outbox de mensagem |
| **Realtime** | Hubs SignalR, grupos, fan-out de eventos |
| **Files** | Metadados de anexo, URLs pré-assinadas, políticas de tipo/tamanho |
| **Search** | Indexação leve / FTS PostgreSQL |
| **Notifications** | Preferências e entregas assíncronas (fase inicial mínima) |
| **Audit** | Trilha de ações sensíveis |
| **AI** | Interface `IAiAssistant`; provedor opcional (OpenRouter) |
| **Administration** | Dashboard, settings, export e leitura administrativa |
| **Integrations** | Webhooks outbound e futuras integrações autorizadas |
| **Moderation** | Fronteira reservada; ainda sem domínio material no snapshot de 2026-07-27 |
| **BuildingBlocks** | Tipos e contratos técnicos compartilhados |

Presence/typing pertencem hoje à fronteira **Realtime** com estado efêmero no
Redis. Health, persistência, outbox processor e rate-limit ficam em
`src/VibeChat.Infrastructure`; não constituem um módulo de negócio chamado
“Platform”.

## Fluxo de dados (resumo)

1. Cliente autentica no Keycloak e obtém tokens.
2. API valida JWT, estabelece `TenantContext` (nunca confiar no body).
3. Comando de envio de mensagem: Messaging valida membership → grava message + outbox (mesma TX) → confirma ao cliente.
4. Worker processa outbox → Realtime/SignalR notifica assinantes (via backplane Redis se multi-instância).
5. Clientes reconciliam por `seq` se necessário.

Detalhes: `fluxo-envio-mensagem.md`, `modelo-dominio.md`, `contratos.md`.

## Princípios de design

1. **PostgreSQL é a verdade** — Redis e caches são descartáveis
2. **Contratos &gt; acoplamento** — módulos não referenciam internals uns dos outros
3. **Idempotência e sequência** — retries seguros; ordem por conversation
4. **Tenant no contexto** — RLS + filtros de aplicação
5. **IA atrás de porta** — núcleo funciona com AI desligada
6. **Observabilidade default** — todo caminho crítico é traçável
7. **Compose first** — sem K8s até haver justificativa (ADR-017)

## Fronteiras de deploy (fase 1)

- Um `api`, um `worker`, um `web` (nginx/static) — **todos como serviços Compose** (profile `apps` / `task apps`; B-074 — caminho oficial self-host/demo)
- Dependências (Postgres, Redis, Keycloak, MinIO) no mesmo Compose
- Escala horizontal da API possível via Redis backplane; schema e outbox preparados
- Dev local pode rodar API/Web no host (`task dev`); self-host/demo usa containers

## O que deliberadamente não temos (ainda)

- Bus de mensagens externo (NATS/Kafka/Rabbit) — ADR-015
- Elasticsearch/OpenSearch — ADR-016
- Kubernetes — ADR-017
- Banco por tenant físico (usamos RLS em DB compartilhado)
