# Contrato SignalR em Alta Disponibilidade

Detalha realtime multi-instância para B-144. Redis distribui fan-out efêmero; não
armazena a verdade das mensagens.

## Invariantes

- Persistência e outbox confirmam a mensagem antes do fan-out.
- Falha de SignalR não perde mensagem durável.
- Reconnect termina com sync/gap-fill.
- Grupo é tenant-scoped e revalida authZ.
- API nodes são descartáveis.
- Cliente lento não bloqueia os demais.

## Topologia

```text
cliente
  → load balancer/proxy com WebSocket
  → API node
  ↔ Redis backplane
  ← worker/outbox publisher
```

PostgreSQL continua source of truth. Redis HA precisa ser proporcional ao perfil;
queda do backplane degrada realtime, não escrita/histórico.

## Afinidade

O ADR de B-144 registra:

- transportes habilitados;
- necessidade de sticky sessions para a combinação escolhida;
- cookie/afinidade sem dado sensível;
- comportamento em reconnect para outro nó;
- compatibilidade com proxy e backplane.

Não assumir que Redis elimina afinidade em toda configuração. WebSocket puro pode
reduzir a necessidade; fallback transports e negociação podem alterá-la.

## Autenticação

- token é validado no connect;
- expiração/renovação usa callback seguro do cliente;
- reconexão revalida token e memberships;
- logout/revoke encerra conexões e remove grupos;
- query string com token nunca aparece em access log;
- relógio/skew e falha de refresh possuem reason codes.

## Grupos

- `t:{tenantId}` para eventos tenant-scoped permitidos;
- `t:{tenantId}:c:{channelId}` para conversa;
- join exige membership atual;
- reconnect não restaura grupo sem nova autorização;
- mudança de membership invalida conexão/grupo;
- nome recebido do cliente nunca é usado diretamente.

## Backpressure

Cada conexão possui:

- buffer limitado;
- máximo de tamanho/frequência por evento;
- timeout de envio;
- política de desconexão/resync;
- métricas de drop/slow consumer.

Evento durável pode ser reduzido a notificação de mudança. Ao exceder buffer,
cliente recebe/faz `resync required`; servidor não mantém backlog infinito.
Typing/presence podem ser descartados.

## Capacidade

B-146 mede por perfil:

- conexões simultâneas por nó;
- joins/grupos por segundo;
- fan-out por canal;
- maior canal;
- payload/event rate;
- memória por conexão;
- CPU e network;
- Redis ops/bandwidth;
- tempo de reconnect storm.

Limite rejeita/degrada com reason code e retry hint, sem derrubar o chat HTTP.

## Drain e rolling upgrade

1. readiness impede novas conexões no nó;
2. nó anuncia reconnect/drain quando suportado;
3. encerra aceite de novos joins;
4. aguarda envios in-flight por tempo limitado;
5. fecha conexões com motivo retryable;
6. clientes reconectam em versão compatível;
7. sync/gap-fill reconcilia;
8. nó sai após deadline.

Versões adjacentes concordam em eventos, grupos e protocolo de sync durante a
janela. Breaking event usa dual-publish/versionamento.

## Falhas

| Falha | Comportamento |
|-------|---------------|
| API node | Cliente reconecta em outro nó e sincroniza |
| Redis/backplane | Escrita continua; realtime degrada; clientes fazem polling/sync |
| Worker/outbox lag | Ack continua durável; UI mostra atraso quando limiar excede |
| Proxy WebSocket | Fallback apenas se suportado/testado; caso contrário erro claro |
| Auth provider | Sessões válidas seguem policy; novo connect falha fechado |
| Reconnect storm | Jitter, limites e admission control |

Nenhum failover promove Redis ou memória do hub a registro durável.

## Observabilidade

- conexões por nó/tenant;
- connect/reconnect success e latency;
- group join denied;
- token refresh failure;
- slow consumers/buffer drops;
- fan-out latency/error;
- Redis backplane health;
- drain duration;
- resync/gap-fill após reconnect;
- outbox-to-client latency.

Labels evitam user/channel IDs de alta cardinalidade.

## Segurança

- teste cross-tenant em join, typing, presence e fan-out;
- rate limit por tenant/principal/IP conforme policy;
- limite de payload;
- origin/CORS explícitos;
- logs sem token/body;
- proteção contra connection/group churn;
- revogação medida ponta a ponta.

## Testes de B-144

- dois ou mais API nodes com backplane;
- cliente conecta em nós diferentes;
- matar nó durante send;
- Redis indisponível e recuperado;
- drain/rolling upgrade mixed-version;
- token expira durante conexão;
- membership revogada;
- slow client e canal grande;
- reconnect storm;
- nenhuma duplicação/perda após gap-fill.
