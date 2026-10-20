# ADR-045 — Export de audit para SIEM

- Status: Accepted
- Data: 2026-10-20
- Relacionado: B-132, B-042, B-108, B-139, D-23, ADR-009, ADR-010, ADR-020, ADR-026

## Contexto

O audit local (B-042) não chega ao monitoramento corporativo. A spec pede
entrega near-real-time, assinada, com checkpoint, retry idempotente, filtro,
minimização, backfill limitado e circuit breaker. D-23 pede o controle sem
certificação. O pacote R3 de compliance separa essa exportação de
`workspace.admin`.

## Decisão

1. Flag `Features:Siem:Enabled` nasce **false**. Desligada, a API responde
   404 `SiemDisabled` e o worker não envia nada.
2. Tabelas `compliance.siem_destinations` e `compliance.siem_deliveries`,
   com `TenantId`, RLS FORCE e WITH CHECK. O claim do worker lê destinos
   só com `app.job_role = siem`. A escrita continua exigindo o tenant.
3. No máximo 3 destinos ativos por workspace. Transporte `webhook-json` ou
   `syslog-cef`. URL só HTTPS, sem redirect, com a mesma guarda de egress
   dos conectores (DNS duas vezes, rede privada bloqueada).
4. `security.audit.export` não entra no catálogo de papéis. O
   `workspace.admin` nomeia outra pessoa com capability `audit.export`.
5. O segredo de HMAC-SHA256 fica no envelope AES-GCM (ADR-020). A resposta
   mostra o valor uma vez, na criação e na rotação. Lista e health devolvem
   só a máscara, o circuito, o lag e o último sucesso.
6. O corpo é o envelope `audit.event.recorded` v1 (B-139). Sem body de
   mensagem. Minimização ligada por default; desligada, o metadata ainda
   perde chaves de body, segredo, token e senha. CEF é sempre minimizado.
7. Checkpoint por destino. Retry reutiliza o mesmo `eventId` (índice único
   em destino + evento). 4xx, falha de rede ou egress abrem o circuito na
   hora; 5xx abre na terceira falha, por 60 segundos.
8. Backfill não passa de 24 horas nem de 100 eventos, só do tenant do
   destino. O cursor ao vivo começa na criação, sem despejar o histórico.
9. O evento de teste é sintético. Não copia audit real.

## Alternativas

- Reusar o webhook de mensagem (B-108): ele carrega body e não tem
  checkpoint de audit.
- Um destino para vários tenants: a spec proíbe.
- Syslog UDP: não há como assinar nem aplicar a guarda HTTPS.

## Consequências

- Rollback da API e do worker: `Features:Siem:Enabled=false`. Linhas já
  gravadas permanecem. Nenhum POST sai.
- A migration `AddSiemExport` cria as tabelas. Não há dado de produção
  para voltar atrás; dropar as tabelas só com o schema vazio.
- UI em `/compliance`: destino, teste, lag e rotação, sem reexibir o segredo.
- Não há garantia de ingestão no SIEM externo nem certificação.
