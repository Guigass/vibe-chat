# Visão do Produto — VibeChat

## O que é

VibeChat é uma plataforma de chat corporativo **open-source** e **self-hosted**. Organizações instalam, operam e controlam a própria infraestrutura — sem depender de SaaS de terceiros para conversas internas, arquivos e integrações.

## Problema

Empresas que precisam de comunicação em tempo real frequentemente enfrentam:

- Dependência de produtos fechados (Slack, Teams, Discord) com lock-in e custos crescentes
- Dados sensíveis fora do perímetro da organização
- Pouca customização de identidade, fluxos e integrações internas
- Complexidade excessiva de soluções “enterprise” quando o time só precisa de canais, threads e busca confiável

## Proposta de valor

1. **Controle total** — dados, identidade e retenção sob governança da organização
2. **Simplicidade operacional** — monólito modular + Docker Compose na fase 1
3. **Experiência moderna** — UI própria (não clone de Slack/Discord/WhatsApp), PWA, tempo real
4. **Extensível** — módulos claros, contratos compartilhados, IA opcional atrás de interface
5. **Pronto para crescer** — outbox, multi-tenancy com RLS, observabilidade desde o início

Visão além da fase 2:
[`visao-longo-prazo.md`](visao-longo-prazo.md). O mapa de capacidades e o
horizonte ambicioso são propostas de produto, não autorização automática de
implementação.

## Público-alvo

| Persona | Necessidade |
|--------|-------------|
| Time de engenharia / plataforma | Self-host, Compose, observabilidade, ADRs claros |
| Administradores de TI | SSO (Keycloak/OIDC), tenants, backup, retenção |
| Colaboradores | Canais, DMs, threads, arquivos, presença, busca |
| Security / Compliance | Isolamento multi-tenant, auditoria, modelo de ameaças |

## Princípios de produto

- **Self-hosted first** — o caminho feliz é rodar na infra do cliente
- **Uma fatia vertical antes de tudo** — login → workspace → canal → enviar/receber mensagem em tempo real
- **Confiabilidade > features** — idempotência, sequência de conversa, outbox
- **IA opcional** — desligada por padrão; nunca acoplada ao núcleo
- **Identidade visual própria** — teal/ocean + charcoal; tipografia distintiva (ver design system)

## Escopo da fase 1 (MVP operacional)

Inclui:

- Autenticação OIDC via Keycloak
- Workspaces, Spaces, Channels, Threads
- Mensagens em tempo real (SignalR)
- Presença e typing (Redis)
- Anexos via MinIO (S3-compatible)
- Busca inicial em PostgreSQL
- Multi-tenancy com RLS
- Observabilidade (OpenTelemetry, Prometheus, Grafana, Loki, Tempo)
- Docker Compose (sem Kubernetes)

Fora do escopo da fase 1:

- Microserviços
- Kubernetes
- Elasticsearch/OpenSearch, NATS/Kafka/RabbitMQ
- Clientes nativos mobile (PWA cobre o essencial)
- Marketplace de apps / bots avançados → reescrito como **plugins locais**
  em W10; registry assinado/governado só em W15 (D-18/B-137), sem billing

## Métricas de sucesso (produto)

- Fatia vertical demonstrável em Compose local em &lt; 30 minutos
- Envio de mensagem com latência percebida &lt; 300 ms em rede local
- Isolamento comprovado entre tenants (testes de segurança)
- Zero acoplamento direto entre módulos de domínio (contratos via interfaces compartilhadas)

## Nome e marca

**VibeChat** — vibe de colaboração leve, sem parecer “mais um clone de Slack”. Assets visuais (logo, ícones, fundos, sons) em `apps/web/public/` — inventário em `docs/architecture/design-system.md` § Assets de marca. Decisões de marca/licença/identidade legal: `docs/roadmap/decisoes-pendentes.md`.
