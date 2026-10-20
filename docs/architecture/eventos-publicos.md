# Eventos públicos versionados

Fonte canônica: `PublicEventCatalog` (B-139 / ADR-041). Este arquivo é a
saída de `PublicEventCatalog.RenderMarkdown()`; o teste de unidade falha se divergir.

O registry é um artefato do repositório e uma API da própria aplicação.
Não há Kafka nem serviço externo de schema (ADR-015).

Flag `Integrations:EventSchemas:Enabled` nasce **false**. Desligada, o webhook
entrega o body atual sem checagem e `GET/POST /api/v1/admin/event-schemas`
responde 404 `EventSchemasDisabled`. Ligada, a entrega valida o body contra a
versão declarada (ou a v1, se o JSON não trouxer `schemaVersion`) e recusa
tipo ou versão desconhecidos sem chamar a URL e sem relançar para a outbox.

## Kit do consumidor

- `PublicEventCatalog.Validate(eventType, schemaVersion, json)`
- `PublicEventCatalog.Project` só cobre `messaging.message.created` de v1 para v2
- `POST /api/v1/admin/event-schemas/check` com `workspace.admin`
- documento acima de 16 KiB → `SchemaDocumentTooLarge`

## Catálogo

### messaging.message.created v1

- Wire: `MessageCreated`
- Status: deprecated
- Janela: até 2027-04-19 (inclusive o dia anterior; a partir dessa data a versão pode sair)
- Changelog: Wire payload already delivered to webhook consumers. Deprecated in favor of the v2 envelope; both stay valid through 2027-04-18.

Exemplo:

```json
{
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "channelId": "00000000-0000-0000-0000-000000000002",
  "messageId": "00000000-0000-0000-0000-000000000003",
  "sequence": 1,
  "authorId": "00000000-0000-0000-0000-000000000004",
  "createdAt": "2026-10-19T12:00:00Z"
}
```

Schema:

```json
{
  "type": "object",
  "additionalProperties": true,
  "required": [
    "tenantId",
    "channelId",
    "messageId",
    "sequence",
    "authorId",
    "createdAt"
  ],
  "properties": {
    "tenantId": {
      "type": "string",
      "format": "uuid"
    },
    "channelId": {
      "type": "string",
      "format": "uuid"
    },
    "messageId": {
      "type": "string",
      "format": "uuid"
    },
    "sequence": {
      "type": "integer"
    },
    "authorId": {
      "type": "string",
      "format": "uuid"
    },
    "createdAt": {
      "type": "string",
      "format": "date-time"
    }
  }
}
```

### messaging.message.created v2

- Wire: `MessageCreated`
- Status: active
- Changelog: Common envelope. payload is a v1 document. Coexists with v1 until the deprecation date.

Exemplo:

```json
{
  "eventId": "00000000-0000-0000-0000-000000000005",
  "eventType": "messaging.message.created",
  "occurredAt": "2026-10-19T12:00:00Z",
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "aggregateId": "00000000-0000-0000-0000-000000000003",
  "correlationId": "00000000-0000-0000-0000-000000000006",
  "schemaVersion": 2,
  "payload": {
    "tenantId": "00000000-0000-0000-0000-000000000001",
    "channelId": "00000000-0000-0000-0000-000000000002",
    "messageId": "00000000-0000-0000-0000-000000000003",
    "sequence": 1,
    "authorId": "00000000-0000-0000-0000-000000000004",
    "createdAt": "2026-10-19T12:00:00Z"
  }
}
```

Schema:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": [
    "eventId",
    "eventType",
    "occurredAt",
    "tenantId",
    "aggregateId",
    "correlationId",
    "schemaVersion",
    "payload"
  ],
  "properties": {
    "eventId": {
      "type": "string",
      "format": "uuid"
    },
    "eventType": {
      "type": "string",
      "enum": [
        "messaging.message.created"
      ]
    },
    "occurredAt": {
      "type": "string",
      "format": "date-time"
    },
    "tenantId": {
      "type": "string",
      "format": "uuid"
    },
    "aggregateId": {
      "type": "string",
      "format": "uuid"
    },
    "correlationId": {
      "type": "string",
      "format": "uuid"
    },
    "schemaVersion": {
      "type": "integer",
      "minimum": 2,
      "maximum": 2
    },
    "payload": {
      "type": "object",
      "additionalProperties": true,
      "required": [
        "tenantId",
        "channelId",
        "messageId",
        "sequence",
        "authorId",
        "createdAt"
      ],
      "properties": {
        "tenantId": {
          "type": "string",
          "format": "uuid"
        },
        "channelId": {
          "type": "string",
          "format": "uuid"
        },
        "messageId": {
          "type": "string",
          "format": "uuid"
        },
        "sequence": {
          "type": "integer"
        },
        "authorId": {
          "type": "string",
          "format": "uuid"
        },
        "createdAt": {
          "type": "string",
          "format": "date-time"
        }
      }
    }
  }
}
```

### messaging.message.edited v1

- Wire: `MessageEdited`
- Status: active
- Changelog: Initial public schema for the current webhook body. Additional properties remain allowed.

Exemplo:

```json
{
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "channelId": "00000000-0000-0000-0000-000000000002",
  "messageId": "00000000-0000-0000-0000-000000000003",
  "sequence": 2,
  "editedAt": "2026-10-19T12:00:00Z"
}
```

Schema:

```json
{
  "type": "object",
  "additionalProperties": true,
  "required": [
    "tenantId",
    "channelId",
    "messageId",
    "sequence",
    "editedAt"
  ],
  "properties": {
    "tenantId": {
      "type": "string",
      "format": "uuid"
    },
    "channelId": {
      "type": "string",
      "format": "uuid"
    },
    "messageId": {
      "type": "string",
      "format": "uuid"
    },
    "sequence": {
      "type": "integer"
    },
    "editedAt": {
      "type": "string",
      "format": "date-time"
    }
  }
}
```

### messaging.message.deleted v1

- Wire: `MessageDeleted`
- Status: active
- Changelog: Initial public schema for the current webhook body. Additional properties remain allowed.

Exemplo:

```json
{
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "channelId": "00000000-0000-0000-0000-000000000002",
  "messageId": "00000000-0000-0000-0000-000000000003",
  "sequence": 3,
  "deletedAt": "2026-10-19T12:00:00Z"
}
```

Schema:

```json
{
  "type": "object",
  "additionalProperties": true,
  "required": [
    "tenantId",
    "channelId",
    "messageId",
    "sequence",
    "deletedAt"
  ],
  "properties": {
    "tenantId": {
      "type": "string",
      "format": "uuid"
    },
    "channelId": {
      "type": "string",
      "format": "uuid"
    },
    "messageId": {
      "type": "string",
      "format": "uuid"
    },
    "sequence": {
      "type": "integer"
    },
    "deletedAt": {
      "type": "string",
      "format": "date-time"
    }
  }
}
```

### messaging.message.reaction_changed v1

- Wire: `ReactionChanged`
- Status: active
- Changelog: Initial public schema for the current webhook body. Additional properties remain allowed.

Exemplo:

```json
{
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "channelId": "00000000-0000-0000-0000-000000000002",
  "messageId": "00000000-0000-0000-0000-000000000003",
  "userId": "00000000-0000-0000-0000-000000000004",
  "emoji": "thumbsup",
  "added": true
}
```

Schema:

```json
{
  "type": "object",
  "additionalProperties": true,
  "required": [
    "tenantId",
    "channelId",
    "messageId",
    "userId",
    "emoji",
    "added"
  ],
  "properties": {
    "tenantId": {
      "type": "string",
      "format": "uuid"
    },
    "channelId": {
      "type": "string",
      "format": "uuid"
    },
    "messageId": {
      "type": "string",
      "format": "uuid"
    },
    "userId": {
      "type": "string",
      "format": "uuid"
    },
    "emoji": {
      "type": "string"
    },
    "added": {
      "type": "boolean"
    }
  }
}
```

### integrations.webhook.test v1

- Wire: `WebhookTest`
- Status: active
- Changelog: Initial public schema for the current webhook body. Additional properties remain allowed.

Exemplo:

```json
{
  "type": "WebhookTest",
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "endpointId": "00000000-0000-0000-0000-000000000007",
  "sentAt": "2026-10-19T12:00:00Z"
}
```

Schema:

```json
{
  "type": "object",
  "additionalProperties": true,
  "required": [
    "type",
    "tenantId",
    "endpointId",
    "sentAt"
  ],
  "properties": {
    "type": {
      "type": "string",
      "enum": [
        "WebhookTest"
      ]
    },
    "tenantId": {
      "type": "string",
      "format": "uuid"
    },
    "endpointId": {
      "type": "string",
      "format": "uuid"
    },
    "sentAt": {
      "type": "string",
      "format": "date-time"
    }
  }
}
```

### audit.event.recorded v1

- Wire: `audit.event.recorded`
- Status: active
- Changelog: Minimized audit envelope for SIEM. Message body is not a property.

Exemplo:

```json
{
  "eventType": "audit.event.recorded",
  "schemaVersion": 1,
  "eventId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "occurredAt": "2026-10-20T12:00:00.0000000\u002B00:00",
  "category": "siem",
  "severity": "info",
  "action": "siem.test",
  "synthetic": true
}
```

Schema:

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": [
    "eventType",
    "schemaVersion",
    "eventId",
    "occurredAt",
    "category",
    "severity",
    "action",
    "synthetic"
  ],
  "properties": {
    "eventType": {
      "type": "string",
      "enum": [
        "audit.event.recorded"
      ]
    },
    "schemaVersion": {
      "type": "integer",
      "minimum": 1
    },
    "eventId": {
      "type": "string",
      "format": "uuid"
    },
    "occurredAt": {
      "type": "string",
      "format": "date-time"
    },
    "category": {
      "type": "string"
    },
    "severity": {
      "type": "string",
      "enum": [
        "info",
        "low",
        "medium",
        "high",
        "critical"
      ]
    },
    "action": {
      "type": "string"
    },
    "synthetic": {
      "type": "boolean"
    },
    "entityType": {
      "type": "string"
    },
    "entityId": {
      "type": "string"
    },
    "actorId": {
      "type": "string",
      "format": "uuid"
    },
    "metadata": {
      "type": "object"
    }
  }
}
```

