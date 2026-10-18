# ADR-037 — Conectores bidirecionais

- Status: Accepted
- Data: 2026-10-17
- Relacionado: B-127, B-108, B-125, ADR-009, ADR-010, ADR-020, ADR-026, ADR-035

## Contexto

Webhooks genéricos não sincronizam issue, calendário ou evento externo com
canal, tarefa ou incidente. O pacote R3 de automação proíbe script na API e bus
novo. B-125 já entrega `call_connector` para um webhook HTTPS cadastrado. B-127
é outro contrato: instalação por tenant, segredo cifrado e egress com allowlist.

## Decisão

1. Quatro tabelas no schema `connectors`: `installations`, `effects`,
   `deliveries` e `checkpoints`. `TenantId` com FORCE RLS. Postgres continua
   source of truth. Não há worker nem `app.job_role` novo além da leitura
   pontual `connector`, só no lookup do endpoint opaco, com `WITH CHECK` ainda
   exigindo o tenant.
2. O catálogo `connector.manifest.v1` é código: `github`, `gitlab` e `calendar`.
   Escopos mínimos ficam no catálogo e na resposta. Hosts de GitHub e GitLab
   são fixos. Calendário (CalDAV reduzido ao mesmo evento `{id,title}`) fixa um
   único host HTTPS na instalação.
3. Segredo de acesso, refresh e verificador PKCE usam o envelope AES-GCM já
   existente (ADR-020). A resposta só traz máscara. OAuth é cliente público com
   PKCE. Não há client secret no repositório nem no banco.
4. Inbound é `POST /api/v1/connectors/inbound/{opaqueKey}`. O tenant sai da
   instalação, nunca do JSON. Assinatura HMAC ou token GitLab. O mesmo
   `externalEventId` devolve o mesmo efeito. O efeito é mensagem no canal,
   item de checklist na tarefa, ou referência na timeline do incidente. O body
   externo não é copiado.
5. Outbound usa transporte com redirect desligado, timeout de 5s, duas
   resoluções de DNS e bloqueio de IP privado, loopback, link-local, metadata
   e rebinding. Falha incrementa backoff e abre o circuito na terceira. A falha
   grava `connectors.deliveries` e não escreve na outbox de mensagens.
6. Revogar limpa os envelopes e recusa inbound (410) e outbound sem chamada de
   rede. Rotacionar troca o envelope e zera o circuito.
7. `call_connector` de B-125 continua no webhook allowlisted. Não reentra neste
   runner.
8. Flag `Connectors:Enabled`, **off** em `appsettings.json`. Desligada, a API
   responde `ConnectorDisabled` (404) e não grava. O chat segue.

## Alternativas

- Reusar só o webhook de B-108: não há dedupe de evento externo nem mapeamento
  para tarefa ou incidente, e a URL é escolhida pelo admin sem pin de provedor.
- Buscar qualquer URL informada no payload: abre SSRF.
- Chamar a rede de dentro da outbox geral: uma falha externa segura a fila de
  `MessageCreated`.

## Consequências

- Rollback operacional: `Connectors:Enabled=false`. Instalações e mensagens já
  gravadas permanecem. O chat não muda. Runbook:
  `docs/operations/runbooks/conectores.md`.
- Rollback de schema: `Down` remove `connectors.installations`, `effects`,
  `deliveries` e `checkpoints`. A mensagem já publicada no canal permanece.
- Capacidade: 20 instalações por workspace, corpo inbound de 16 KiB, circuito
  aberto por 60s após 3 falhas. Métricas
  `vibechat.connectors.inbound|outbound_failed|revoked` não levam conteúdo.
- R3: sem serviço novo, sem bus, sem script, flag off por default. Sem
  credencial real de GitHub, GitLab ou calendário.

## Segurança

Ver `docs/security/modelo-ameacas.md` (B-127). Tenant vem da instalação no
inbound e da sessão no admin. Member recebe 403. Outro tenant recebe 403.
URL privada ou metadata não sai da API.
