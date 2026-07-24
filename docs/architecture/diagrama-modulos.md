# Diagrama de Módulos — VibeChat

## Mapa de módulos e dependências permitidas

```mermaid
flowchart TB
  subgraph Apps
    WEB[apps/web<br/>Angular 22]
    API[apps/api<br/>ASP.NET Core]
    WRK[apps/worker]
  end

  subgraph Modules
    PLAT[Platform<br/>tenancy, outbox, health]
    ID[Identity]
    DIR[Directory]
    MSG[Messaging]
    RT[Realtime]
    FILES[Files]
    SEARCH[Search]
    PRES[Presence]
    NOTIF[Notifications]
    AUDIT[Audit]
    AI[AI]
  end

  subgraph Shared
    CTR[Contracts<br/>interfaces + DTOs + events]
  end

  subgraph Infra
    PG[(PostgreSQL)]
    RD[(Redis)]
    S3[(MinIO)]
    KC[Keycloak]
    OTEL[OTel Stack]
  end

  WEB -->|HTTP + SignalR| API
  API --> PLAT
  API --> ID
  API --> DIR
  API --> MSG
  API --> RT
  API --> FILES
  API --> SEARCH
  API --> PRES
  API --> NOTIF
  API --> AUDIT
  API --> AI

  WRK --> PLAT
  WRK --> MSG
  WRK --> RT
  WRK --> SEARCH
  WRK --> FILES
  WRK --> NOTIF
  WRK --> AI
  WRK --> AUDIT

  ID --> CTR
  DIR --> CTR
  MSG --> CTR
  RT --> CTR
  FILES --> CTR
  SEARCH --> CTR
  PRES --> CTR
  NOTIF --> CTR
  AUDIT --> CTR
  AI --> CTR
  PLAT --> CTR

  ID --> KC
  PLAT --> PG
  MSG --> PG
  DIR --> PG
  FILES --> S3
  FILES --> PG
  PRES --> RD
  RT --> RD
  PLAT --> RD
  API --> OTEL
  WRK --> OTEL
```

## Regras de dependência

1. Módulos de domínio dependem apenas de **Contracts** + **Platform** (infraestrutura compartilhada).
2. Um módulo **não** referencia o namespace interno de outro módulo.
3. Comunicação entre módulos:
   - **Síncrona:** interfaces em Contracts (ex.: `IMembershipQuery`)
   - **Assíncrona:** eventos de outbox (`MessageCreated`, `MembershipChanged`)
4. `apps/api` e `apps/worker` são composition roots — registram implementações.
5. `AI` é opcional: se desabilitado, binding é `NoOpAiAssistant`.

## Pacotes sugeridos (solução .NET)

```
src/
  VibeChat.Contracts/
  VibeChat.Platform/
  VibeChat.Modules.Identity/
  VibeChat.Modules.Directory/
  VibeChat.Modules.Messaging/
  VibeChat.Modules.Realtime/
  VibeChat.Modules.Files/
  VibeChat.Modules.Search/
  VibeChat.Modules.Presence/
  VibeChat.Modules.Notifications/
  VibeChat.Modules.Audit/
  VibeChat.Modules.AI/
apps/
  VibeChat.Api/
  VibeChat.Worker/
  VibeChat.Web/          # Angular (repo apps/web)
```

## Testes de arquitetura

Usar testes NetArchTest / similares em `tests/architecture` para garantir:

- Modules.* não referenciam outros Modules.* diretamente
- Apenas Api/Worker referenciam todos os módulos
- Contracts não depende de Modules nem de Infra concreta
