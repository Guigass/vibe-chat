# Decisões Pendentes (Owner Humano) — VibeChat

Estas decisões **não** devem ser tomadas unilateralmente por agentes de código. Bloqueiam aspectos legais, de marca e de produção. D-01…D-10 foram **fechadas pelo owner** em 2026-07-24 (Wave 4 / MVP P1); D-11…D-15 e a revisão de D-07 foram fechadas em **2026-07-25** (paridade de mensageria + saída do PrimeNG).

## Lista

| ID | Decisão | Por que importa | Owner sugerido | Status |
|----|---------|-----------------|----------------|--------|
| D-01 | **Licença open-source** | Define adoção, SaaS wrappers, obrigações de copyleft | Founder / Legal | **Decidido (2026-07-24)** — Apache-2.0 definitiva (`LICENSE`) |
| D-02 | **Marca e naming** | Evita conflito de marca; identidade pública | Founder / Brand | **Decidido (2026-07-24)** — produto **VibeChat**; assets visuais em `apps/web/public/` (ver design-system § Assets de marca); domínios oficiais ainda fora do escopo de agentes |
| D-03 | **Política de retenção e exclusão** | Delete, export, LGPD/GDPR, backups | Legal / DPO | **Decidido (2026-07-24)** — soft-delete de mensagens; hard-delete/purge configurável depois (90 dias sugerido, feature flag); export workspace em P2 — ver ADR-018 |
| D-04 | **Credenciais e secrets de produção** | Segurança operacional; nunca em git | Ops / Security | **Decidido (2026-07-24)** — secrets só via `.env` / secrets manager; placeholders `CHANGE_ME` / `*_change_me` em `.env.example` |
| D-05 | **Modo de deploy alvo oficial** | Expectativa de suporte | Platform owner | **Decidido (2026-07-24)** — fase 1 = **Docker Compose**; K8s só quando ADR-017 justificar |
| D-06 | **Uso de IA com provedores externos** | PII sai do perímetro | Legal + Security | **Decidido (2026-07-24)** — IA externa **off por default**; só com flag + key; mock local default; nunca no hot path de `SendMessage` (ADR-012) |
| D-07 | **Política de guests** | Compliance e authZ | Produto + Legal | **Decidido (2026-07-24)**, **revisado (2026-07-25)** — guests entram na Wave 10 por convite com escopo de canal e validade; ver registro D-07 |
| D-08 | **SLA/RPO/RTO** | Dimensiona backup e HA | Ops | **Decidido (2026-07-24)** — dev/self-host sem SLA comercial; RPO/RTO “best effort” + backup diário Postgres (`docs/operations/backup-restore.md`) |
| D-09 | **Código de conduta e governança** | OSS saudável | Founder | **Decidido (2026-07-24)** — `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1) + `CONTRIBUTING.md` |
| D-10 | **Provedor SMTP / e-mail** | Features P2 | Ops | **Decidido (2026-07-24)** — Mailpit em dev; produção = SMTP genérico configurável (sem vendor locked) |
| D-11 | **Escopo de paridade da fase 2** | Define o que o agente pode implementar sem perguntar | Founder / Produto | **Decidido (2026-07-25)** — mensageria rica assíncrona entra; voz/vídeo ao vivo, canvas colaborativo, bots e E2EE ficam fora |
| D-12 | **Mensagem de áudio** | Formato, limites, privacidade da transcrição | Produto + Security | **Decidido (2026-07-25)** — anexo de áudio com MIME negociado no cliente; 5 min / 10 MB; transcrição opt-in atrás da flag de IA |
| D-13 | **Notificações push** | Sai do perímetro self-host se usar serviço fechado | Ops + Security | **Decidido (2026-07-25)** — Web Push (VAPID) do próprio servidor; opt-in por usuário; sem FCM/APNs proprietário |
| D-14 | **Idiomas suportados** | Custo de manutenção de catálogo | Produto | **Decidido (2026-07-25)** — `pt-BR` (default) e `en`; `@angular/localize`; sem terceiro idioma na fase 2 |
| D-15 | **Licença do PrimeNG / PrimeUI** | `primeng@22` é comercial e, sem chave, injeta banner que cobre o composer | Founder / Legal | **Decidido (2026-07-25)** — **sair do PrimeNG** (opção c); ver B-104 + emenda ADR-002 |

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
Escolha: Nome de produto VibeChat; identidade visual técnica versionada em apps/web/public/; domínios oficiais fora do escopo de agentes
Data: 2026-07-24 (assets de marca adicionados ao repo em 2026-07-25)
Owner: Founder / Brand
Impacto em código/docs: UI e docs usam “VibeChat”; reutilizar assets catalogados em design-system.md; não inventar domínio oficial
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

```text
Decisão: D-07 (revisão)
Escolha: Guest passa a existir na Wave 10, com estas regras não negociáveis:
  - acesso só por convite de admin a UM canal por convite (nunca ao workspace)
  - convite é um link com token de uso único, validade default 7 dias (máx. 30)
  - guest não lista outros canais, não usa busca global, não abre DM com quem não
    esteja no mesmo canal, não vê o diretório de membros do workspace
  - guest pode enviar mensagem, anexo e reação no canal do convite; não pode
    convidar, criar canal, nem ler configuração
  - revogação imediata pelo admin encerra sessão e membership
  - todo convite e todo aceite geram evento de audit
Data: 2026-07-25
Owner: Founder / Produto
Impacto em código/docs: B-040 sai de Blocked e vira W10-8; spec em
  docs/product/specs/B-040-guests-por-convite.md; glossário atualiza "Guest"
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

### D-11

```text
Decisão: D-11
Escolha: Paridade da fase 2 = mensageria rica assíncrona. Entram composição
  (formatação, menções, emoji, anexos múltiplos, áudio, citar, encaminhar),
  leitura (agrupamento, histórico paginado, previews, fixar, salvos, não lidas),
  notificações (web push, preferências, DND), busca com filtros, atalhos,
  acessibilidade e i18n.
  Ficam FORA, sem ADR novo: chamada de voz/vídeo ao vivo e screen share
  (exigem SFU/TURN e plano de capacidade), superfície de documento colaborativo
  tipo Canvas/Loop (CRDT + modelo de permissão próprio), marketplace de bots e
  E2EE (incompatível com B-046/B-067).
Data: 2026-07-25
Owner: Founder / Produto
Impacto em código/docs: waves 8-10 do roadmap; benchmark-mensageria.md;
  itens fora de escopo permanecem em P3 quando aplicável
```

### D-12

```text
Decisão: D-12
Escolha: Mensagem de áudio é um Attachment como outro qualquer, com metadados
  de duração e waveform. O cliente negocia o MIME em runtime
  (MediaRecorder.isTypeSupported): webm/opus onde houver, mp4/aac no Safari.
  Sem transcodificação no servidor na fase 2 — o player usa o formato original.
  Limites: 5 minutos e 10 MB por áudio.
  Transcrição é opcional, roda no servidor pelo caminho de IA já existente,
  fica atrás de Ai:Enabled + permissão, off por default (D-06 se aplica) e
  nunca no hot path de SendMessage.
Data: 2026-07-25
Owner: Produto + Security
Impacto em código/docs: spec B-080; Files/AI; contratos.md; glossário
```

### D-13

```text
Decisão: D-13
Escolha: Notificação push via Web Push padrão (VAPID) servida pela própria
  instância; nada de FCM/APNs ou serviço proprietário, para não furar o
  perímetro self-host. Opt-in explícito por usuário e por dispositivo; a
  permissão só é pedida depois de uma ação do usuário, nunca no load.
  Payload da notificação é mínimo — remetente, canal e prévia curta; conteúdo
  sensível fica fora. Chaves VAPID em env, como qualquer secret (D-04).
Data: 2026-07-25
Owner: Ops + Security
Impacto em código/docs: specs B-095/B-097; Notifications; .env.example;
  ngsw + service worker; modelo-ameacas.md
```

### D-14

```text
Decisão: D-14
Escolha: pt-BR é o idioma default e en o segundo. Mecanismo: @angular/localize
  com catálogo por idioma no repo. Sem terceiro idioma na fase 2. Docs humanas
  continuam só em PT-BR (AGENTS.md); a regra vale para a UI.
Data: 2026-07-25
Owner: Produto
Impacto em código/docs: spec B-100; apps/web; orientacoes.md
```

### D-15

```text
Decisão: D-15 — Decidido
Contexto: primeng@22 deixou de ser OSS. O LICENSE.md do pacote instalado diz
  "This package is part of PrimeUI, a family of commercial UI libraries by
  PrimeTek Informatics" e "A valid license key is required to use this
  software... A missing, invalid, or expired key may cause the software to
  display a license notice".
  Sem chave, primeng/fesm2022/primeng-license.mjs injeta um div position:fixed
  no canto inferior direito, z-index 2147483647, dentro de shadow root
  mode:'closed' — deliberadamente resistente a CSS. Na tela do chat ele cobre
  os botões Anexar e Enviar (UX-002).
  Isso conflita com duas coisas já escritas: "sem dependências proprietárias"
  em AGENTS.md, e a emenda do ADR-002 que adotou PrimeNG no B-073 sem análise
  de licença.

Opções:
  a) Community License (grátis) — exige elegibilidade anual declarada:
     < US$ 1M de receita, < 5 devs, < 10 funcionários, < US$ 3M de captação.
     Projeto OSS não comercial também qualifica. Precisa gerar e configurar a
     chave; renovação anual.
  b) Commercial License (paga) — por desenvolvedor, perpétua, 1 ano de updates.
  c) Sair do PrimeNG — hoje o uso está confinado ao /admin (Table/Select/Tag);
     o shell de chat já é composição própria. Substituir por componentes
     próprios + CDK, e emendar o ADR-002 de novo.

Escolha: (c) Sair do PrimeNG
Data: 2026-07-25
Owner: Founder / Legal
Impacto em código/docs:
  - Emenda ADR-002 supersede a adoção B-073; stack UI = Angular + CDK + composição
    própria com tokens VibeChat
  - Backlog B-104 (Wave 7) + spec docs/product/specs/B-104-remover-primeng.md
  - Remover primeng do package.json, providePrimeNG, preset, styles/_primeng.scss
  - Reescrever /admin (Table/Select/Tag) sem PrimeNG
  - UX-002 deixa de ser Blocked; fecha quando B-104 mergear
  - Agente NÃO compra/gera chave nem esconde o banner por CSS
```

## Orientações para agentes

- Features sensíveis (retenção/IA) com **feature flags** e defaults seguros
- Usar placeholders em `.env.example`, nunca valores reais de produção
- Não inventar marca/logo/domínio — usar assets em `apps/web/public/` e o inventário do design-system
- Novas decisões humanas: abrir linha nesta tabela como Pendente e parar se bloquearem o escopo

## Como fechar uma decisão

1. Owner registra a escolha (issue/ADR curto se técnico)
2. Atualizar esta tabela: Status = Decidido + data + link
3. Propagar para README, LICENSE, ops docs
