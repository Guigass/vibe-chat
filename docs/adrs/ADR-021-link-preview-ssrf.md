# ADR-021: Link preview com guarda SSRF

## Status: Accepted

## Contexto

W9-4 / B-091 exige cartão Open Graph para a primeira URL de uma mensagem. Isso
faz o **worker** buscar URL fornecida pelo usuário — superfície clássica de SSRF.
A spec e o pacote R3 exigem ADR, threat model, flag e rollback no mesmo PR.

## Decisão

1. **Assíncrono via outbox** de `MessageCreated` — nunca no hot path de
   `SendMessage` (mesmo padrão de B-090 / thumbnails).
2. **Cache por tenant** em `messaging.link_previews` (único `TenantId`+`UrlHash`)
   + junction `messaging.message_link_previews`; TTL 7 dias.
3. **Fetcher dedicado** (`LinkPreviewFetcher`) com:
   - allowlist `http`/`https`;
   - DNS resolve e recusa de IP privado/loopback/link-local/metadata
     (`169.254.169.254`), inclusive após cada redirect;
   - `AllowAutoRedirect=false` + follow manual (máx. 3);
   - `ConnectCallback` que só conecta a endereços públicos (mitiga DNS rebinding);
   - timeout configurável (`LinkPreview:TimeoutMs`, default 8000);
   - HTML ≤ 1,5 MB / imagem ≤ 512 KB; sem cookies/credenciais; UA `VibeChat-LinkPreview/1.0`.
4. **Imagem** armazenada no MinIO (`ImageKey`); a API só expõe URL assinada —
   sem hotlink.
5. **Flags:** `LinkPreview:Enabled` (processo, default `true`) e toggle por
   tenant em admin. B-187: processo + timeout no DB de instância quando
   overrides on; senão default de código.
6. **Remoção:** `DELETE .../messages/{id}/link-preview` — autor ou
   `workspace.admin` (soft-remove na junction).

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Fetch síncrono no `SendMessage` | Viola ADR-010 / hot path; timeout derruba UX de envio |
| Cache global cross-tenant | Vazamento entre tenants |
| Hotlink de `og:image` | Exfiltração/tracking e quebra de privacidade |
| Sem guarda de IP pós-redirect | SSRF clássico — item R3 não passa review |

## Rollback

1. `LinkPreview:Enabled=false` (ou toggle admin) — para novos fetches.
2. Reverter migration / dropar tabelas só em lab; em dados reais preferir flag off.
3. Evento SignalR `LinkPreviewReady` é best-effort; ausência não quebra o chat.

## Consequências

- **+** Preview útil sem travar envio; isolamento multi-tenant no cache
- **+** Controles SSRF auditáveis e cobertos por unit tests
- **−** Worker faz egress HTTP; exige timeout/concorrência cuidadosos
- **−** Sites sem OG não geram cartão (comportamento esperado)
