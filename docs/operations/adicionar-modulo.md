# Como Adicionar um Módulo — VibeChat

## Quando criar um módulo

Crie um módulo novo quando houver um **bounded context** claro (ex.: `Billing`, `ComplianceExport`) que não caiba sem inflar Messaging/Directory.

Não crie módulo para utilitários genéricos — isso vai em `modules/BuildingBlocks` (contratos técnicos) ou em `src/VibeChat.Infrastructure` / `src/VibeChat.SharedKernel` conforme a natureza.

## Passos

### 1. Definir fronteira

Documentar em PR:

- Responsabilidade
- Aggregates
- Dependências (apenas BuildingBlocks + SharedKernel; sem peers de domínio)
- Eventos publicados/consumidos

### 2. Criar projeto

Padrão atual do repo (pasta curta + csproj `VibeChat.<Nome>`):

```text
modules/<Nome>/
  VibeChat.<Nome>.csproj
  <Nome>Domain.cs   # ou fatias verticais equivalentes
```

Referenciar:

- `VibeChat.BuildingBlocks` (contratos técnicos: tenancy, outbox, permissions, etc.)
- `VibeChat.SharedKernel` quando necessário

**Não** referenciar outros `VibeChat.<Módulo>` de domínio.
**Não** criar assemblies fictícios `VibeChat.Contracts` ou `VibeChat.Platform` — a infra compartilhada vive em `src/VibeChat.Infrastructure`; não há módulo de negócio “Platform” (ver `docs/architecture/visao-geral.md`).

### 3. Contratos

Se outros módulos precisam falar com você:

1. Colocar interfaces/DTOs/eventos no **módulo dono** (ou em BuildingBlocks se for porta técnica transversal)
2. Implementar adapters em `src/VibeChat.Infrastructure` quando a implementação for persistência/infra
3. Registrar no composition root (`apps/api`, `apps/worker`)

Fonte canônica: `docs/architecture/contratos.md`.

### 4. Persistência

- Tabelas com `tenant_id`
- Migrações no fluxo padrão do repo
- Políticas RLS (`FORCE ROW LEVEL SECURITY` em tabelas tenant-aware)
- Índices para queries de ACL

### 5. Outbox

Mutações que disparam efeitos colaterais:

- Escrever outbox na mesma TX
- Handlers no worker idempotentes
- EventTypes namespaced: `<modulo>.<entidade>.<acao>`

### 6. API / Realtime

- Endpoints mínimos no host API (extension `MapXEndpoints` ou wiring em `Program.cs`)
- AuthZ via `IPermissionChecker` + leitores de membership (`IWorkspaceMembershipReader`, `IChannelMembershipReader`)
- Se realtime: publicar via porta Realtime/outbox, não acoplar ao Hub concreto

### 7. Testes

- Unit do domínio
- Integration do API
- Architecture test: novo módulo não referencia peers
- Security: casos cross-tenant
- Host compartilhado: `tests/VibeChat.TestHost` quando aplicável

### 8. Observabilidade

- Spans `módulo.ação`
- Métricas de negócio relevantes (cardinalidade baixa)
- Logs com tenant_id / correlation_id

### 9. Docs

- Atualizar `diagrama-modulos.md` e glossário se termos novos
- ADR se a decisão for estrutural

## Checklist de PR

- [ ] Fronteira clara / sem dependência circular
- [ ] Contratos estáveis (módulo dono ou BuildingBlocks)
- [ ] RLS + TenantContext
- [ ] Outbox se necessário
- [ ] Testes arch + security
- [ ] Feature flag se experimental
- [ ] Sem código de exemplo solto na pasta docs

## Anti-padrões

- “SharedKernel” / BuildingBlocks inchados com lógica de todos os módulos
- DbContext único god sem ownership
- Chamadas HTTP internas entre módulos no mesmo monólito
- Módulo AI importando internals de Messaging
- Inventar pacote `Contracts`/`Platform` paralelo ao layout real
