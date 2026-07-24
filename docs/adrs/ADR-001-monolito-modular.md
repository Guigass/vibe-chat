# ADR-001: Monólito modular

## Status: Accepted

## Contexto

VibeChat precisa entregar uma fatia vertical rapidamente, com fronteiras de domínio claras, operação simples (Docker Compose) e caminho de evolução futuro. Microserviços prematuros aumentariam custo operacional, latência e complexidade de consistência (mensagens, outbox, presença) sem benefício na fase 1.

## Decisão

Adotar um **monólito modular**:

- Um deploy de API (`apps/api`) e um Worker (`apps/worker`)
- Módulos de domínio isolados por assemblies/namespaces
- Comunicação via **contratos compartilhados** e eventos de outbox
- Proibição de referências internas cruzadas entre módulos (enforce via testes de arquitetura)

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Microserviços desde o dia 1 | Overhead de rede, deploy, tracing e dual-write; time pequeno |
| Modularidade apenas por pastas sem contratos | Fronteiras erodem; acoplamento implícito |
| Modular monolith + message bus externo obrigatório | Complexidade desnecessária; outbox + worker basta na fase 1 |

## Consequências

- **+** Deploy e debug simples; transações locais para message+outbox
- **+** Extração futura de módulo para serviço é possível se contratos forem respeitados
- **−** Disciplina necessária para não criar “big ball of mud”
- **−** Escala independente por módulo só virá se/quando extrair serviços
