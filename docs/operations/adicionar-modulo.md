# Como Adicionar um Módulo — VibeChat

## Quando criar um módulo

Crie um módulo novo quando houver um **bounded context** claro (ex.: `Billing`, `ComplianceExport`) que não caiba sem inflar Messaging/Directory.

Não crie módulo para utilitários genéricos — isso vai em `Platform` ou `Contracts`.

## Passos

### 1. Definir fronteira

Documentar em PR:

- Responsabilidade
- Aggregates
- Dependências (apenas Contracts + Platform)
- Eventos publicados/consumidos

### 2. Criar projeto

```text
modules/VibeChat.Modules.<Nome>/
  Domain/
  Application/
  Infrastructure/
  # ou estrutura vertical slices — manter padrão do repo
```

Referenciar:

- `VibeChat.Contracts`
- `VibeChat.Platform` (se necessário)

**Não** referenciar outros `Modules.*`.

### 3. Contratos

Se outros módulos precisam falar com você:

1. Adicionar interfaces/DTOs/eventos em `VibeChat.Contracts`
2. Implementar no módulo novo
3. Registrar no composition root (`apps/api`, `apps/worker`)

### 4. Persistência

- Tabelas com `tenant_id`
- Migrações no fluxo padrão do repo
- Políticas RLS
- Índices para queries de ACL

### 5. Outbox

Mutações que disparam efeitos colaterais:

- Escrever outbox na mesma TX
- Handlers no worker idempotentes
- EventTypes namespaced: `<modulo>.<entidade>.<acao>`

### 6. API / Realtime

- Endpoints mínimos no host API (extension `MapXEndpoints`)
- AuthZ via membership queries existentes ou novas interfaces
- Se realtime: publicar via `IRealtimePublisher`, não acoplar ao Hub concreto

### 7. Testes

- Unit do domínio
- Integration do API
- Architecture test: novo módulo não referencia peers
- Security: casos cross-tenant

### 8. Observabilidade

- Spans `módulo.ação`
- Métricas de negócio relevantes (cardinalidade baixa)
- Logs com tenant_id / correlation_id

### 9. Docs

- Atualizar `diagrama-modulos.md` e glossário se termos novos
- ADR se a decisão for estrutural

## Checklist de PR

- [ ] Fronteira clara / sem dependência circular
- [ ] Contratos estáveis
- [ ] RLS + TenantContext
- [ ] Outbox se necessário
- [ ] Testes arch + security
- [ ] Feature flag se experimental
- [ ] Sem código de exemplo solto na pasta docs

## Anti-padrões

- “SharedKernel” inchado com lógica de todos os módulos
- DbContext único god sem ownership
- Chamadas HTTP internas entre módulos no mesmo monólito
- Módulo AI importando Messaging.Internal
