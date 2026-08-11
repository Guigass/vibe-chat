# Métricas de Produto, Operação e Evidências

Catálogo para decidir por dados sem criar telemetria externa obrigatória.

## Princípios

- Métrica serve decisão ou SLO.
- Instância self-host mantém telemetria no próprio perímetro.
- Envio de analytics para fora é off por default e exige opt-in.
- Não registrar body, secret, token ou identificador desnecessário.
- Tenant pode aparecer apenas em forma segura para segregação local.
- Métrica não substitui audit.

## North star

**Tempo entre uma necessidade comunicada e sua resolução rastreável**, sem perda
de contexto nem violação de governança.

Proxies:

- tempo até primeira conversa útil;
- tempo para encontrar uma mensagem/decisão;
- percentual de action items com origem;
- automações concluídas sem intervenção;
- incidentes cross-tenant: zero.

## Funil de adoção local

| Etapa | Evento agregado |
|-------|-----------------|
| Instância pronta | install/health concluído |
| Workspace ativo | primeiro membership/canal |
| Conversa útil | primeira mensagem e leitura por outro usuário |
| Retenção semanal | usuários ativos locais |
| Valor avançado | decisão/tarefa/automação concluída |

Não existe phone-home obrigatório.

## Métricas por domínio

### Mensageria

- send latency p50/p95/p99;
- erro e rate-limit;
- duplicate prevented;
- seq gap;
- outbox lag;
- reconnect/gap-fill;
- attachment success;
- search latency.

### Conhecimento e IA

- index lag;
- retrieval denied por ACL;
- delete propagation lag;
- respostas com citação;
- provider timeout/fallback;
- custo/budget;
- feedback sem conteúdo sensível.

### Automação e integrações

- executions success/fail;
- retry/dead-letter;
- loop prevented;
- callback latency;
- quota/throttle;
- token/grant revoked;
- delivery deduplicated.

### Segurança e compliance

- cross-tenant denied;
- authZ denied;
- secret exposure: zero;
- hold/export audit coverage;
- malware states;
- DLP findings por classificação;
- restore/security drills.

### Live

- join success/time;
- participant capacity;
- packet loss/jitter;
- SFU/TURN saturation;
- recording consent;
- chat availability durante falha.

## Evidência por wave

| Wave | Evidência de saída |
|------|--------------------|
| W7 | CI/supply chain/CSP/body/config/admin UX |
| W8 | composição E2E, anexos/áudio, a11y |
| W9 | histórico/reconnect/read cursor/previews |
| W10 | push/DND/guests/plugins/security |
| W11 | inbox/anúncios/tópicos, import dry-run/checkpoint e support bundle sanitizado |
| W12 | origem de decisão/tarefa, ACL e delete propagation |
| W13 | automação idempotente, connector security, SCIM |
| W14 | cadeia de custódia, SIEM, quotas e capacity |
| W15 | SDK/registry provenance, bridges e support matrix |
| W16 | performance/escalabilidade, failover/offline/federação/E2EE |
| W17 | capacity/consentimento/live/canvas |

## Evidence bundle de PR

Cada PR informa:

- Work-Item/Wave/Risk/Lease;
- testes executados;
- logs sintéticos relevantes;
- screenshots quando UI;
- migrations/rollback;
- métricas antes/depois quando performance;
- trace/correlation sem PII;
- threat model/ADR quando R3;
- limitações conhecidas.

## SLOs

Metas Basic/Standard/HA vêm de D-25/D-28 e B-146. Antes de B-146, números são
objetivos de referência, não garantias. Persistência, realtime, reconnect, sync,
revogação, anexos e restore possuem SLIs próprios.

Error budget não autoriza ignorar incidente de segurança ou integridade.

## Métrica de conclusão documental

- 100% dos itens Planned com spec;
- 100% com risco;
- 100% das specs com seções canônicas;
- zero link local quebrado;
- zero decisão aberta sem R4 real;
- zero finding Alta sem rota;
- zero `Done` sem evidência.
