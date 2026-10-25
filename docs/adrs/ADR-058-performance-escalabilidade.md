# ADR-058 — Performance permanece no Compose

- Status: Accepted
- Data: 2026-10-25
- Relacionado: B-170, B-146, ADR-001, ADR-010, ADR-011, ADR-015, ADR-016, ADR-017, ADR-048

## Contexto

B-146 publicou envelopes, SLIs e os números que reabriam bus, OpenSearch e
Kubernetes. Faltava olhar os hot paths da topologia atual antes de HA
(B-144) ou de otimizar no escuro.

## Decisão

1. Não há flag, endpoint nem serviço novo. O envio de mensagem não consulta
   este catálogo.
2. Os hot paths canônicos são `send-message`, `history-sync`,
   `signalr-fanout`, `outbox-worker`, `fts-search`, `upload-presign` e
   `admin-export`, com o baseline de SLI do ADR-048.
3. Os gatilhos numéricos do ADR-048 ficam confirmados: outbox p95 > 30 s
   por 15 minutos com ≥ 2 workers (ADR-015); busca p95 > 500 ms com ≥ 5
   milhões de mensagens, ou CPU de busca > 40% (ADR-016); réplicas de API
   > 3, ou > 2000 conexões/nó quando o restart rolante não cabe no Compose
   (ADR-017). Amostra mais curta que 15 minutos não fecha gatilho.
4. API escala na horizontal, sem estado, com backplane Redis. Worker
   permanece uma réplica e lote de 20 até haver lag medido; a segunda
   réplica exige `SKIP LOCKED`. Postgres continua um primário com
   `FORCE RLS`. Redis e MinIO não viram source of truth.
5. Gargalo sem breach sustentado fica adiado, com motivo no catálogo.
   Esta revisão não muda índice, lote, claim do outbox nem contrato.
6. Carga de lab usa tenant sintético. Cache e chave Redis continuam
   prefixados por tenant. Atalho de performance não fura RLS.

## Alternativas

- Aumentar o lote do outbox ou trocar o claim por `SKIP LOCKED` agora: não
  há lag sustentado acima do SLI, e um worker só não exercita o segundo
  processo.
- Painel novo de performance: o burn rate já cabe no painel de B-146
  quando a flag daquele item está ligada.
- Tratar a amostra de lab como porte Standard de produção: a margem e o
  hardware vão no JSON justamente para impedir isso.

## Consequências

- Rollback: reverter catálogo, script e documento. Não há migration.
- B-144 continua dependente de evidência de HA, não deste texto.
- O JSON de lab envelhece. Commit, data, hardware e margem são parte do
  resultado.
