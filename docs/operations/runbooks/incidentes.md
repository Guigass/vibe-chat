# Runbook — Incidentes

Complementa [`../troubleshooting.md`](../troubleshooting.md) e [`../operacao.md`](../operacao.md).

## Severidade

| Nível | Critério | Exemplo |
|-------|----------|---------|
| P0 | Login ou envio/recebimento de mensagem indisponível para todos | API down, Postgres down, Keycloak down |
| P1 | Degradação grave (anexos, realtime multi-réplica, outbox lag alto) | Redis down, Worker parado, MinIO down |
| P2 | Observabilidade / DX / parcial | Grafana sem dados, um tenant lento |

## Checklist inicial (5 minutos)

1. Confirmar sintoma com horário UTC e escopo (todos os tenants? um workspace?)
2. Coletar evidência:
   - `GET /health` e `GET /ready` da API
   - `docker compose ps` (ou stack equivalente)
   - Grafana: 5xx, outbox lag, saturação Postgres/Redis
   - Um `correlation_id` / trace id de request falho
3. Classificar severidade (tabela acima)
4. Se suspeita de **vazamento cross-tenant** → tratar como incidente de segurança (abaixo); não “só reiniciar”

## Árvore rápida

```text
Login falha?
  → Keycloak healthy? issuer/audience? clock skew?
  → ver troubleshooting Auth / Login

Mensagem salva mas não chega?
  → outbox processed_at nulo? Worker up? Redis backplane?
  → ver troubleshooting Mensagens

API 5xx / /ready unhealthy?
  → Postgres / Redis reachability
  → logs api + worker

Anexo falha?
  → MinIO + CORS + presign clock
  → ver troubleshooting Arquivos

Suspeita cross-tenant?
  → runbook Segurança (abaixo)
```

## P0 — App ou dados críticos

1. **Estabilizar**
   - Não aplicar migrate nem restore em prod sem confirmação
   - Se saturação: reduzir carga (rate-limit já existe; pausar jobs não críticos)
2. **Isolar componente**
   - `docker compose ps` + `docker compose logs --tail=200 api worker postgres redis keycloak`
   - Reiniciar só o serviço falho após hipótese clara (`docker compose restart <svc>`)
3. **Dados**
   - Postgres down: priorizar volume + restore só se corrupção confirmada ([backup-restore](./backup-restore.md))
   - Nunca `FLUSHALL` em Redis prod
4. **Validar recuperação**
   - `/ready` Healthy
   - Login (ou DevAuth só em lab)
   - Enviar mensagem de teste em canal conhecido; confirmar entrega realtime + persistência
5. **Comunicar** usuários self-host (status page / canal ops interno)

## P1 — Degradação

| Sintoma | Ação |
|---------|------|
| Redis down | App degrada presence/typing/backplane — restaurar Redis; API deve continuar com Postgres |
| Worker parado | Outbox acumula — subir worker; inspecionar dead-letter |
| MinIO down | Chat texto ok; anexos falham — restaurar MinIO / credenciais |
| Outbox lag | Escalar worker; checar handler lento (IA **não** pode estar no hot path de send) |

## Segurança — suspeita cross-tenant

1. Isolar acesso de rede se breach ativo
2. Coletar evidence (request, IDs, query) **sem** colar secrets/PII em canais públicos
3. Rodar `task test:security` em staging com o mesmo commit
4. Revogar sessões Keycloak do tenant afetado; rotacionar segredos se confirmado
5. Seguir [`../../security/multi-tenant.md`](../../security/multi-tenant.md)

## Pós-incidente

- [ ] Timeline (detecção → mitigação → resolução)
- [ ] Causa raiz (1 parágrafo)
- [ ] Ação preventiva (alerta, healthcheck, doc, teste)
- [ ] Atualizar troubleshooting se sintoma novo
- [ ] Se superfície mudou: `docs/security/modelo-ameacas.md`

## Comandos úteis

```bash
docker compose ps
docker compose logs --tail=200 api worker
curl -sS http://localhost:5080/health
curl -sS http://localhost:5080/ready
# SQL (lab): SELECT id, type, occurred_at FROM outbox WHERE processed_at IS NULL ORDER BY occurred_at LIMIT 50;
```
