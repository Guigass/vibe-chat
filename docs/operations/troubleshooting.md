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

### Sintoma: clicar em “Entrar com Keycloak” volta para `/login`

- No Coolify, publique o domínio no serviço `web`, porta interna `80`; o Nginx
  desse container encaminha `/auth/*` para o Keycloak.
- Confirme `KEYCLOAK_PUBLIC_URL` e `KEYCLOAK_HOSTNAME` com a URL pública
  `https://<dominio>/auth` e `KEYCLOAK_HTTP_RELATIVE_PATH=/auth`.
- Staging path `/auth`: defina **também** `KC_HTTP_RELATIVE_PATH=/auth` no
  container Keycloak (Coolify). Não passe string vazia — Keycloak 26 falha no
  build com `KC_HTTP_RELATIVE_PATH=""`.
- `KEYCLOAK_PROXY_HEADERS` deve ser `xforwarded` ou `forwarded` (nunca vazio).
- O Service Worker não pode tratar `/auth/**` (nem as demais rotas de proxy)
  como navegação Angular. Verifique as exclusões em `apps/web/ngsw-config.json`.
- Após publicar uma correção do Service Worker, recarregue a aplicação para o
  navegador ativar o novo manifesto antes de repetir o login.
- Se o console administrativo permanecer em “Loading the Administration
  Console”, confirme que `location ^~ /auth/` tem precedência sobre a regex de
  assets Angular e não herda a CSP do shell. O Keycloak deve servir seus
  próprios JS/CSS e emitir seus próprios headers para os consoles admin/account.

### Sintoma: botões Alice/Bob/Demo não aparecem no login (`task apps`)

- DevAuth na UI é **build-time**: `ENABLE_DEV_AUTH=true` no `.env` e rebuild da
  web (`docker compose ... --build web` ou `task apps`).
- Staging/prod/Coolify devem manter `ENABLE_DEV_AUTH=false` (default Compose);
  login oficial é só OIDC.
- API DevAuth (`X-Dev-User`) só funciona com `ASPNETCORE_ENVIRONMENT=Development`.

### Sintoma: Keycloak reinicia / unhealthy após atualizar `.env`

- Logs com `Invalid value for option 'KC_PROXY_HEADERS'` → valor vazio; use
  `xforwarded` (default do Compose) ou `forwarded`.
- Logs com `Failed to run 'build' command` / NPE → `KC_HTTP_RELATIVE_PATH=""`;
  no lab omita a var no container; no staging use `/auth`.

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
3. Cliente no grupo SignalR correto? (`t:{tenant}:c:{channel}`)
4. Redis backplane configurado se multi-API?
5. Cliente precisa gap-fill via history (`seq`)

### Sintoma: só o typing atualiza ao vivo (mensagens não)

- Typing é efêmero (Redis/hub); mensagens passam por **outbox → hub** (dispatcher na API)
- Conferir `building_blocks.outbox_messages`: `ProcessedAt` nulo + `Error` (ex.: FTS `to_tsvector` / reindex) — fan-out realtime não deve depender do search
- Confirmar handler `MessageCreated` no cliente e ingest na store (`seq` / dedupe)
- Após reconnect: re-`JoinChannel` **e** gap-fill por history (B-070)
- Se edit/delete/reação falham mas send às vezes funciona: checar eventos de hub correspondentes

### Sintoma: vejo meu próprio “digitando…”

- Hub deve usar `Clients.OthersInGroup` em `SendTyping` (B-071 / W6-2)
- Cliente também ignora `userId` == perfil local (defesa em profundidade)
- Se ainda aparece: conferir se o payload `userId` bate com `profile.id` (DevAuth/OIDC `sub`)

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

- CORS global do MinIO (`MINIO_API_CORS_ALLOW_ORIGIN`) inclui a origem do web
  (CORS por bucket / `mc cors set` é só AIStor)
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
