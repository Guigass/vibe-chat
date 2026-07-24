# ADR-015: Quando justificar NATS / Kafka / RabbitMQ

## Status: Accepted

## Contexto

Na fase 1, o **Transactional Outbox + Worker** cobre fan-out interno (SignalR, indexação, notificações) sem broker externo. Equipes frequentemente introduzem Kafka/Rabbit/NATS cedo por hábito, aumentando ops sem ganho. Este ADR define **sinais objetivos** para reavaliar.

## Decisão

**Não** adotar NATS, Kafka ou RabbitMQ na fase 1.

Reavaliar introdução de um bus externo **somente** se vários critérios abaixo forem verdadeiros de forma sustentada:

### Critérios de gatilho (precisam de evidência)

1. **Throughput de eventos** excede o que outbox polling + SKIP LOCKED entrega com lag aceitável (SLO de lag violado após tuning)
2. **Múltiplos consumidores externos** (outros sistemas da empresa) precisam assinar eventos VibeChat com retenção/replay
3. **Extração de microserviço** real (módulo vira processo separado com ciclo de deploy independente)
4. **Fan-out cross-região** ou integração com plataforma de eventos corporativa já existente
5. Necessidade de **stream processing** (janelas, agregações contínuas) além de jobs pontuais

### Se justificado, escolha sugerida (orientação)

| Cenário | Tendência |
|---------|-----------|
| Fan-out leve, low-ops, edge | **NATS** JetStream |
| Integração enterprise, retenção longa, replay, muitos consumidores | **Kafka** (ou managed) |
| Filas de trabalho tradicionais, roteamento simples | **RabbitMQ** |

A introdução deve preservar o outbox (DB continua sendo a origem atômica); o bus torna-se transporte **após** o commit, não substituto da transação.

## Alternativas consideradas

| Alternativa | Motivo |
|-------------|--------|
| Kafka “já no Compose” | Custo cognitivo e de disco sem requisito |
| RabbitMQ para typing/presence | Redis já cobre efêmero |
| Substituir outbox por publish direto no broker | Reintroduz dual-write |

## Consequências

- **+** Fase 1 simples; menos partes móveis
- **+** Critérios claros evitam debate cíclico
- **−** Até lá, integrações externas podem usar webhooks/API pull
- **−** Quando adotar bus, haverá projeto de migração de consumidores do worker
