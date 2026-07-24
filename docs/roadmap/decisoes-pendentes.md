# Decisões Pendentes (Owner Humano) — VibeChat

Estas decisões **não** devem ser tomadas unilateralmente por agentes de código. Bloqueiam aspectos legais, de marca e de produção.

## Lista

| ID | Decisão | Por que importa | Owner sugerido | Status |
|----|---------|-----------------|----------------|--------|
| D-01 | **Licença open-source** (MIT, Apache-2.0, AGPL-3.0, etc.) | Define adoção, SaaS wrappers, obrigações de copyleft | Founder / Legal | **Pendente** — provisório no repo: **Apache-2.0** (`LICENSE`); aguarda decisão final do owner |
| D-02 | **Marca e naming** (VibeChat trademark, logo final, domínios) | Evita conflito de marca; identidade pública | Founder / Brand | Pendente |
| D-03 | **Política de retenção e exclusão de dados** | Delete, export, LGPD/GDPR, backups | Legal / DPO | Pendente |
| D-04 | **Credenciais e secrets de produção** (OIDC clients, DB, Redis, MinIO, AI keys) | Segurança operacional; nunca em git | Ops / Security | Pendente |
| D-05 | **Modo de deploy alvo oficial além de Compose** (VM hardening, K8s “suportado”) | Expectativa de suporte | Platform owner | Pendente (Compose = fase 1) |
| D-06 | **Uso de IA com provedores externos** — permitido? cláusulas? | PII sai do perímetro | Legal + Security | Pendente |
| D-07 | **Política de guests / dados de terceiros** | Compliance e authZ | Produto + Legal | Pendente |
| D-08 | **SLA/RPO/RTO** prometidos a usuários internos | Dimensiona backup e HA | Ops | Pendente |
| D-09 | **Código de conduta e governança de contribuição** | OSS saudável | Founder | **Pendente** — rascunho em `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1) |
| D-10 | **Provedor de domínio de e-mail / SMTP** para notificações | Features P2 | Ops | Pendente |

## Orientações para agentes

- Implementar features com **feature flags** e defaults seguros quando D-03/D-06 estiverem abertos
- Usar placeholders `CHANGE_ME` em `.env.example`, nunca valores reais de produção
- Não escolher licença no lugar do owner — pode documentar *prós/contras* se pedido
- Não publicar marca/logo finais inventados como oficiais além do design system técnico

## Como fechar uma decisão

1. Owner registra a escolha (issue/ADR curto se técnico)
2. Atualizar esta tabela: Status = Decidido + data + link
3. Propagar para README, LICENSE, ops docs

## Template de registro

```text
Decisão: D-0X
Escolha: …
Data: YYYY-MM-DD
Owner: …
Impacto em código/docs: …
```
