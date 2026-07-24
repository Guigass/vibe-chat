# ADR-017: Quando justificar Kubernetes

## Status: Accepted

## Contexto

A fase 1 opera com **Docker Compose** (ou equivalente systemd+containers). Kubernetes oferece orquestração poderosa, mas com custo alto de plataforma. Muitos self-hosters (PMEs, times internos) preferem Compose/VM simples.

## Decisão

**Não** exigir Kubernetes na fase 1. Entregar manifests/Helm só quando gatilhos forem atendidos; até lá, Compose é o caminho oficial.

### Critérios de gatilho

1. Necessidade de **múltiplos ambientes** com escala horizontal frequente (várias réplicas API/Worker) e rolling updates sem downtime como requisito duro
2. Organização **já opera** uma plataforma K8s (e rejeita Compose em produção)
3. Requisitos de **multi-AZ / self-healing** além do que um Compose + proxy + systemd cobre
4. Densidade multi-tenant grande com **autoscaling** (HPA/KEDA) baseado em outbox lag / conexões SignalR
5. Compliance/processo interno exige deploy via cluster com policies (PSS, NetworkPolicy) já padronizadas

### O que preparar mesmo sem K8s

- Containers stateless (API/Worker/Web)
- Config por env; secrets externos
- Health/ready probes
- Identidade de workload compatível com futura migração
- Documentação de escala vertical/horizontal em VMs

### O que não fazer cedo

- Operadores custom complexos
- Service mesh obrigatório
- Fragmentar o monólito só para “ficar cloud-native”

## Alternativas consideradas

| Alternativa | Motivo |
|-------------|--------|
| K8s obrigatório no MVP | Barreira a self-hosters e agentes |
| Nomad/ECS apenas | Podem ser alvos futuros; não oficiais na fase 1 |
| Bare metal sem containers | Possível para experts; Compose permanece referência |

## Consequências

- **+** Onboarding local e demos em minutos
- **+** Menos YAML e menos superfície de ataque na fase 1
- **−** HA avançada fica sob responsabilidade do operador da VM/Compose
- **−** Quando migrar, será necessário chart/manifests e revisão de sticky sessions / SignalR
