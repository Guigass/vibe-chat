# Decisões Pendentes (Owner Humano) — VibeChat

Estas decisões **não** devem ser tomadas unilateralmente por agentes de código. Bloqueiam aspectos legais, de marca e de produção. Itens abaixo foram **fechados pelo owner** em 2026-07-24 (Wave 4 / MVP P1).

## Lista

| ID | Decisão | Por que importa | Owner sugerido | Status |
|----|---------|-----------------|----------------|--------|
| D-01 | **Licença open-source** | Define adoção, SaaS wrappers, obrigações de copyleft | Founder / Legal | **Decidido (2026-07-24)** — Apache-2.0 definitiva (`LICENSE`) |
| D-02 | **Marca e naming** | Evita conflito de marca; identidade pública | Founder / Brand | **Decidido (2026-07-24)** — produto **VibeChat**; logo/domínios oficiais ficam placeholders do design system |
| D-03 | **Política de retenção e exclusão** | Delete, export, LGPD/GDPR, backups | Legal / DPO | **Decidido (2026-07-24)** — soft-delete de mensagens; hard-delete/purge configurável depois (90 dias sugerido, feature flag); export workspace em P2 — ver ADR-018 |
| D-04 | **Credenciais e secrets de produção** | Segurança operacional; nunca em git | Ops / Security | **Decidido (2026-07-24)** — secrets só via `.env` / secrets manager; placeholders `CHANGE_ME` / `*_change_me` em `.env.example` |
| D-05 | **Modo de deploy alvo oficial** | Expectativa de suporte | Platform owner | **Decidido (2026-07-24)** — fase 1 = **Docker Compose**; K8s só quando ADR-017 justificar |
| D-06 | **Uso de IA com provedores externos** | PII sai do perímetro | Legal + Security | **Decidido (2026-07-24)** — IA externa **off por default**; só com flag + key; mock local default; nunca no hot path de `SendMessage` (ADR-012) |
| D-07 | **Política de guests** | Compliance e authZ | Produto + Legal | **Decidido (2026-07-24)** — guests **fora do MVP P1** (P2); membership obrigatória |
| D-08 | **SLA/RPO/RTO** | Dimensiona backup e HA | Ops | **Decidido (2026-07-24)** — dev/self-host sem SLA comercial; RPO/RTO “best effort” + backup diário Postgres (`docs/operations/backup-restore.md`) |
| D-09 | **Código de conduta e governança** | OSS saudável | Founder | **Decidido (2026-07-24)** — `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1) + `CONTRIBUTING.md` |
| D-10 | **Provedor SMTP / e-mail** | Features P2 | Ops | **Decidido (2026-07-24)** — Mailpit em dev; produção = SMTP genérico configurável (sem vendor locked) |

## Registros

### D-01

```text
Decisão: D-01
Escolha: Apache License 2.0
Data: 2026-07-24
Owner: Founder (autorizado Wave 4)
Impacto em código/docs: LICENSE já presente; README deixa de marcar licença como provisória
```

### D-02

```text
Decisão: D-02
Escolha: Nome de produto VibeChat; marca visual oficial / domínios ficam placeholders do design system
Data: 2026-07-24
Owner: Founder / Brand
Impacto em código/docs: UI e docs usam “VibeChat”; sem inventar logo/domínio oficiais
```

### D-03

```text
Decisão: D-03
Escolha: Soft-delete de mensagens no MVP; purge/hard-delete configurável (sugestão 90 dias + feature flag); export de workspace em P2
Data: 2026-07-24
Owner: Legal / DPO
Impacto em código/docs: ADR-018; EditedAt/DeletedAt no Messaging; B-047 Done (purge configurável)
```

### D-04

```text
Decisão: D-04
Escolha: Secrets apenas via .env / secrets manager; nunca em git
Data: 2026-07-24
Owner: Ops / Security
Impacto em código/docs: .env.example com placeholders; CI/compose sem credenciais reais
```

### D-05

```text
Decisão: D-05
Escolha: Deploy oficial fase 1 = Docker Compose; K8s sob ADR-017
Data: 2026-07-24
Owner: Platform owner
Impacto em código/docs: docs/operations e ADR-017; sem Helm obrigatório
Nota (Wave 6 / B-074): o caminho oficial inclui containers de **api** e **web** (e worker) no Compose — não apenas o data plane; profile `apps` é o veículo atual.
```

### D-06

```text
Decisão: D-06
Escolha: IA externa off por default; mock local; flag + key para providers; fora do hot path SendMessage
Data: 2026-07-24
Owner: Legal + Security
Impacto em código/docs: ADR-012; AiSettings/Mock; OPENROUTER_API_KEY placeholder
```

### D-07

```text
Decisão: D-07
Escolha: Guests fora do MVP P1 (P2); membership obrigatória para acesso a channels/DMs
Data: 2026-07-24
Owner: Produto + Legal
Impacto em código/docs: Role.Guest sem send; B-040 permanece P2
```

### D-08

```text
Decisão: D-08
Escolha: Sem SLA comercial em self-host; RPO/RTO best effort; backup diário Postgres
Data: 2026-07-24
Owner: Ops
Impacto em código/docs: docs/operations/backup-restore.md e operacao.md
```

### D-09

```text
Decisão: D-09
Escolha: Adotar CODE_OF_CONDUCT.md (Contributor Covenant 2.1) + CONTRIBUTING.md
Data: 2026-07-24
Owner: Founder
Impacto em código/docs: arquivos na raiz deixam de ser “provisórios”
```

### D-10

```text
Decisão: D-10
Escolha: SMTP via Mailpit em dev; produção = SMTP genérico configurável
Data: 2026-07-24
Owner: Ops
Impacto em código/docs: .env.example (Mailpit + SMTP_*); notificações email em P2
```

## Orientações para agentes

- Features sensíveis (retenção/IA) com **feature flags** e defaults seguros
- Usar placeholders em `.env.example`, nunca valores reais de produção
- Não inventar marca/logo oficiais além do design system técnico
- Novas decisões humanas: abrir linha nesta tabela como Pendente e parar se bloquearem o escopo

## Como fechar uma decisão

1. Owner registra a escolha (issue/ADR curto se técnico)
2. Atualizar esta tabela: Status = Decidido + data + link
3. Propagar para README, LICENSE, ops docs
