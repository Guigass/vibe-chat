# ADR-041 — Registry de eventos públicos no repositório

- Status: Accepted
- Data: 2026-10-19
- Relacionado: B-139, B-108, B-109, D-18, ADR-010, ADR-015, ADR-026

## Contexto

Plugins, automações e bridges precisam de um contrato estável. O payload que
sai hoje no webhook é o JSON da outbox, sem versão. Um schema service externo
reabriria ADR-015 sem volume que o justifique.

## Decisão

1. A fonte canônica é `PublicEventCatalog` no módulo Integrations. Cada versão
   pública tem `eventType`, `schemaVersion`, nome de wire, status, janela de
   depreciação, changelog, JSON Schema (subconjunto) e exemplo sintético.
   Eventos só internos, sem consumidor externo, ficam de fora.
2. Flag `Integrations:EventSchemas:Enabled` nasce **false**. Desligada, o
   webhook continua com o body atual, sem header novo e sem checagem. A API
   responde 404 `EventSchemasDisabled`.
3. Ligada, a entrega valida o body. Sem `schemaVersion`, vale a v1. Versão ou
   tipo desconhecido não chama a URL, grava `LastError` com o código e soma
   `vibechat.events.schema.rejected` (`reason`, `event_type`). A outbox da
   mensagem segue. O body não entra em log nem em métrica.
4. `messaging.message.created` v1 permanece o body atual e fica deprecated até
   2027-04-19. A v2 é o envelope (`eventId`, `eventType`, `occurredAt`,
   `tenantId`, `aggregateId`, `correlationId`, `schemaVersion`, `payload`).
   As duas coexistem na janela. O wire default continua v1.
5. O subconjunto de JSON Schema cobre object, properties, required,
   additionalProperties, items, enum, format `uuid`/`date-time` e limites
   numéricos. Não há biblioteca nova nem serviço no Compose.
6. `EventSchemaCompatibility` compara o catálogo com
   `public-event-schema-baseline.json`. Remover versão dentro da janela,
   encurtar a janela, tirar propriedade, fechar `additionalProperties` ou
   criar campo obrigatório quebra o teste. `eventos-publicos.md` é a saída de
   `RenderMarkdown()`.
7. A API é `workspace.admin`. O catálogo não tem dado de tenant. O check
   recusa documento acima de 16 KiB e não persiste o body.

## Alternativas

- Schema Registry / Kafka: serviço novo e ADR-015. Fica para quando houver
  volume medido.
- Trocar o body do webhook para o envelope v2 agora: quebra consumidor que já
  assina o HMAC do JSON atual.
- JSON Schema completo via pacote: dependência a mais para um catálogo pequeno
  e fechado.

## Consequências

- Rollback operacional: `Integrations:EventSchemas:Enabled=false`. Entrega e
  chat voltam ao comportamento anterior. Não há migration.
- R3: flag off, sem bus, sem dado de tenant no schema, falha de versão não
  derruba a outbox.
- O portal de developer (B-135) e o kit empacotado (B-136) consomem este
  catálogo depois. Não entram neste PR.
