# Troubleshooting — VibeChat

## Como usar este guia

1. Identifique o sintoma
2. Colete `correlation_id` / trace no Grafana Tempo
3. Siga o runbook de resposta: [`runbooks/incidentes.md`](./runbooks/incidentes.md)
4. Use as seções abaixo para diagnóstico
5. Se for bug de produto, abra issue com evidências

Índice de runbooks: [`runbooks/README.md`](./runbooks/README.md).

---

## Auth / Login

### Sintoma: redirect loop no login

- Conferir `redirect_uri` do client Angular no Keycloak
- Conferir clock skew (NTP) entre API e Keycloak
- `audience` / `ValidAudiences` na API

### Sintoma: 401 na API após login

- Access token expirado; refresh flow
- Issuer URL interno vs externo (container DNS vs browser URL)
- HTTPS vs HTTP mismatch em cookies

---

## Mensagens

### Sintoma: mensagem salva mas outro cliente não recebe

1. Verificar linha na tabela `outbox` — `processed_at` nulo?
2. Worker está up? Logs de claim/erro
3. Cliente no grupo SignalR correto? (`t:{tenant}:c:{conversation}`)
4. Redis backplane configurado se multi-API?
5. Cliente precisa gap-fill via history (`seq`)

### Sintoma: só o typing atualiza ao vivo (mensagens não)

- Typing é efêmero (Redis/hub); mensagens passam por **outbox → hub** (dispatcher na API)
- Conferir `building_blocks.outbox_messages`: `ProcessedAt` nulo + `Error` (ex.: FTS `to_tsvector` / reindex) — fan-out realtime não deve depender do search
- Confirmar handler `MessageCreated` no cliente e ingest na store (`seq` / dedupe)
- Após reconnect: re-`JoinChannel` **e** gap-fill por history (B-070)
- Se edit/delete/reação falham mas send às vezes funciona: checar eventos de hub correspondentes

### Sintoma: vejo meu próprio “digitando…”

- Esperado até B-071: filtrar self no client ou usar `OthersInGroup` no hub

### Sintoma: a página inteira rola (composer some / scrollbar no document)

- Esperado após B-072: shell em `100dvh` + `overflow: hidden`; scroll só em `.timeline` (e listas laterais)
- Conferir hosts Angular (`vc-timeline` / `vc-composer`) participando do flex com `min-height: 0`
- Evitar `min-height: 100dvh` em colunas que precisam encolher dentro do viewport

### Sintoma: mensagens duplicadas

- Cliente gerando nova Idempotency-Key a cada retry
- Unique constraint ausente
- Double-click sem disable no composer

### Sintoma: ordem estranha na UI

- UI ordenando por `created_at` em vez de `seq`
- Gaps não reconciliados

---

## Multi-tenant

### Sintoma: usuário não vê dados que deveria

- Membership ausente
- `TenantContext` errado (claim mapping)
- RLS `app.tenant_id` não setado → zero rows

### Sintoma: suspeita de vazamento cross-tenant

1. **Incidente de segurança** — isolar rede se necessário
2. Coletar evidence (query, IDs)
3. Rodar suíte `tests/security`
4. Revogar sessões; rotacionar segredos se breach confirmado
5. Ver `docs/security/multi-tenant.md`

---

## Redis / Presence

### Sintoma: todos aparecem offline

- Redis down ou AUTH errado
- Heartbeat do cliente parado (aba em throttle)
- TTL agressivo demais

### Sintoma: typing “gruda”

- Evento stop não enviado; depender do TTL
- UI não limpa on timeout

---

## Arquivos / MinIO

### Sintoma: upload falha CORS

- CORS do bucket MinIO para origem do web
- Clock skew em presign

### Sintoma: 403 no download

- Key de outro tenant
- URL expirada
- Política de bucket

---

## Performance

### Sintoma: API lenta

- Traces: DB vs Redis vs Keycloak
- N+1 queries em history
- Vacuum/índices Postgres

### Sintoma: outbox lag alto

- Worker underscaled / erros repetidos
- Payload handler lento (AI síncrono? — não deveria)
- Dead-letter enchendo

---

## Observabilidade

### Sintoma: sem traces

- OTEL endpoint errado
- Sampling 0%
- Propagação quebrada no worker

### Sintoma: logs sem tenant_id

- Enricher não registrado — corrigir Platform logging

---

## Comandos úteis (orientativos)

```bash
docker compose -f infra/compose/docker-compose.yml ps
docker compose logs -f api worker
# SQL: SELECT * FROM outbox WHERE processed_at IS NULL ORDER BY occurred_at LIMIT 50;
```

Nunca rodar `FLUSHALL` em Redis prod. Nunca desabilitar RLS como “fix” permanente.
