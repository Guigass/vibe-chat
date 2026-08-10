# Registro de Decisões de Produto — VibeChat

O nome do arquivo é preservado por compatibilidade, mas **não há decisão aberta**.
D-01…D-10 foram fechadas em 2026-07-24; D-11…D-15 e a revisão de D-07 em
2026-07-25; D-16…D-28 em 2026-07-27. O conjunto define defaults suficientes
para execução autônoma do roadmap. Nova decisão de produto só é aberta quando
um caso realmente fora desses defaults também for R4 em `agents/autonomia.md`.

## Lista

| ID | Decisão | Por que importa | Autoridade original | Status |
|----|---------|-----------------|----------------|--------|
| D-01 | **Licença open-source** | Define adoção, SaaS wrappers, obrigações de copyleft | Founder / Legal | **Decidido (2026-07-24)** — Apache-2.0 definitiva (`LICENSE`) |
| D-02 | **Marca e naming** | Evita conflito de marca; identidade pública | Founder / Brand | **Decidido (2026-07-24)** — produto **VibeChat**; assets visuais em `apps/web/public/` (ver design-system § Assets de marca); domínios oficiais ainda fora do escopo de agentes |
| D-03 | **Política de retenção e exclusão** | Delete, export, LGPD/GDPR, backups | Legal / DPO | **Decidido (2026-07-24)** — soft-delete de mensagens; hard-delete/purge configurável depois (90 dias sugerido, feature flag); export workspace em P2 — ver ADR-018 |
| D-04 | **Credenciais e secrets de produção** | Segurança operacional; nunca em git | Ops / Security | **Decidido (2026-07-24)**, **emendado (2026-08-10)** — infra só `.env`/secret manager; integrações externas podem ir ao DB com AES-GCM (ADR-020); placeholders `CHANGE_ME` |
| D-05 | **Modo de deploy alvo oficial** | Expectativa de suporte | Platform owner | **Decidido (2026-07-24)** — fase 1 = **Docker Compose**; K8s só quando ADR-017 justificar |
| D-06 | **Uso de IA com provedores externos** | PII sai do perímetro | Legal + Security | **Decidido (2026-07-24)** — IA externa **off por default**; só com flag + key; mock local default; nunca no hot path de `SendMessage` (ADR-012) |
| D-07 | **Política de guests** | Compliance e authZ | Produto + Legal | **Decidido (2026-07-24)**, **revisado (2026-07-25)** — guests entram na Wave 10 por convite com escopo de canal e validade; ver registro D-07 |
| D-08 | **SLA/RPO/RTO** | Dimensiona backup e HA | Ops | **Decidido (2026-07-24)** — dev/self-host sem SLA comercial; RPO/RTO “best effort” + backup diário Postgres (`docs/operations/backup-restore.md`) |
| D-09 | **Código de conduta e governança** | OSS saudável | Founder | **Decidido (2026-07-24)** — `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1) + `CONTRIBUTING.md` |
| D-10 | **Provedor SMTP / e-mail** | Features P2 | Ops | **Decidido (2026-07-24)** — Mailpit em dev; produção = SMTP genérico configurável (sem vendor locked) |
| D-11 | **Escopo de paridade da fase 2** | Define o que o agente pode implementar sem perguntar | Founder / Produto | **Decidido (2026-07-25)**, **esclarecido (2026-07-27)** — mensageria rica entra; voz/vídeo, canvas, registry e E2EE ficam fora **da fase 2** e foram ordenados depois por D-16…D-26 |
| D-12 | **Mensagem de áudio** | Formato, limites, privacidade da transcrição | Produto + Security | **Decidido (2026-07-25)** — anexo de áudio com MIME negociado no cliente; 5 min / 10 MB; transcrição opt-in atrás da flag de IA |
| D-13 | **Notificações push** | Sai do perímetro self-host se usar serviço fechado | Ops + Security | **Decidido (2026-07-25)** — Web Push (VAPID) do próprio servidor; opt-in por usuário; sem FCM/APNs proprietário |
| D-14 | **Idiomas suportados** | Custo de manutenção de catálogo | Produto | **Decidido (2026-07-25)** — `pt-BR` (default) e `en`; `@angular/localize`; sem terceiro idioma na fase 2 |
| D-15 | **Licença do PrimeNG / PrimeUI** | `primeng@22` é comercial e, sem chave, injeta banner que cobre o composer | Founder / Legal | **Decidido (2026-07-25)** — **sair do PrimeNG** (opção c); ver B-104 + emenda ADR-002 |
| D-16 | **Posicionamento de longo prazo** | Define se VibeChat permanece chat excelente ou vira plataforma de comunicação/conhecimento/automação | Founder / Produto | **Decidido (2026-07-27)** — plataforma aberta de comunicação, conhecimento e automação, self-hosted first |
| D-17 | **Superfície de conhecimento** | Página leve, canvas colaborativo ou integração externa têm custos e modelos de permissão diferentes | Produto + Arquitetura | **Decidido (2026-07-27)** — páginas server-authoritative primeiro; colaboração CRDT só em B-152 |
| D-18 | **Distribuição de plugins** | Registry/marketplace exige assinatura, revisão, suporte, revogação e possível modelo comercial | Founder + Security + Legal | **Decidido (2026-07-27)** — registry assinado e catálogos públicos permitidos; sem billing nem código in-process |
| D-19 | **Comunicação ao vivo** | Áudio/vídeo/screen share exigem SFU/TURN, capacidade, consentimento e gravação | Produto + Platform + Legal | **Decidido (2026-07-27)** — live opcional self-hosted, OSS, profile separado e off por default |
| D-20 | **Estratégia de clientes** | PWA, desktop empacotado e mobile nativo mudam custo e contratos suportados | Produto + Engenharia | **Decidido (2026-07-27)** — PWA canônica; desktop depois; mobile depois; uma API/contrato |
| D-21 | **Federação e bridges** | Dados e identidades passam a outros domínios/serviços; revogação e retenção deixam de ser locais | Founder + Security + Legal | **Decidido (2026-07-27)** — bridges antes; federação allowlisted; nunca aberta por default |
| D-22 | **Governança de IA e indexação semântica** | Define providers, inferência local, consentimento, orçamento, retenção de embeddings e citações | Produto + Security + Legal | **Decidido (2026-07-27)** — provider-neutral, externo opt-in, ACL/retention/citações/orçamento obrigatórios |
| D-23 | **Meta de compliance enterprise** | SCIM, legal hold, eDiscovery e DLP dependem dos mercados/regulações alvo | Founder + Legal/DPO + Security | **Decidido (2026-07-27)** — LGPD/GDPR + controles alinhados a SOC 2/ISO 27001, sem alegar certificação |
| D-24 | **White-label e política de marca** | Branding por tenant pode diluir VibeChat e afetar suporte/comercialização | Founder / Brand | **Decidido (2026-07-27)** — nome/logo/tokens limitados por tenant; sem CSS/JS arbitrário |
| D-25 | **Porte, SLO e disponibilidade alvo** | HA/multi-região/K8s só podem ser dimensionados com carga, RPO/RTO e orçamento | Platform owner + Founder | **Decidido (2026-07-27)** — perfis Standard/HA; sem multi-region write; escalar por evidência |
| D-26 | **E2EE versus compliance** | E2EE conflita com busca, moderação, legal hold, export e IA server-side | Founder + Security + Legal | **Decidido (2026-07-27)** — canais confidenciais E2EE opt-in, off default e com capacidades reduzidas |
| D-27 | **Kit UI OSS pós-PrimeNG** | Substitui PrimeNG sem dependência comercial; define a stack do B-104 | Founder / Frontend | **Decidido (2026-07-27)** — **spartan/ui** (não NG-ZORRO); ver registro D-27 |
| D-28 | **Perfis de recuperação e RPO Standard** | Separa laboratório de produção e evita prometer que backup diário basta para chat operacional | Founder + Platform | **Decidido (2026-07-27)** — Basic best effort RPO≤24h; Standard de produção RPO≤1h com PITR/WAL; HA mantém RPO≤5m após B-144 |

## Delegação do horizonte ambicioso

D-16…D-28 fecham as escolhas de produto e operação. Decisões técnicas reversíveis — seleção
de biblioteca OSS, shape interno, estratégia de migration e implementação —
foram delegadas aos agentes, que devem registrar ADR quando a arquitetura mudar.
Não reabrir decisão humana apenas porque a implementação é difícil.

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
Escolha: Secrets de infraestrutura/bootstrap apenas via .env / secrets manager; nunca em git.
         Credenciais de integrações externas (OpenRouter, SMTP password, webhook HMAC)
         podem ser persistidas criptografadas no PostgreSQL (ADR-020), com chave mestra
         versionada só no env; API nunca devolve plaintext.
Data: 2026-07-24; emendada 2026-08-10 (ADR-020)
Owner: Ops / Security
Impacto em código/docs: .env.example com placeholders + RuntimeSettings__Encryption__*;
  CI/compose sem credenciais reais; B-069/R-17 atualizados
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
Impacto em código/docs: B-040 sai de Blocked e vira W10-10; spec em
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
  tipo Canvas/Loop (CRDT + modelo de permissão próprio), **marketplace / App
  Directory público** e E2EE (incompatível com B-046/B-067).
  Esclarecimento (2026-07-27): trilha de **plugins locais** (sem loja):
  B-109 (núcleo bot+token+send) → B-110 (instalar/gerir na instância) →
  B-066 (capabilities avançadas, W15) → B-111.
  O gate de registry remoto foi posteriormente fechado por D-18 para B-137.
Data: 2026-07-25
Owner: Founder / Produto
Impacto em código/docs: waves 8-10 do roadmap; benchmark-mensageria.md;
  itens fora da fase 2 permanecem ordenados em W15–W17 quando aplicável
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
  - Emenda ADR-002 supersede a adoção B-073
  - Backlog B-104 (Wave 7) + spec docs/product/specs/B-104-remover-primeng.md
  - Follow-up B-106 (W7-8): admin shell com nav lateral, toolbars, listagens e
    filtros — após B-104; spec docs/product/specs/B-106-admin-shell.md
  - Remover primeng do package.json, providePrimeNG, preset, styles/_primeng.scss
  - Reescrever /admin (Table/Select/Tag) sem PrimeNG (paridade em B-104; polish
    de console em B-106)
  - UX-002 deixa de ser Blocked; fecha quando B-104 mergear
  - Agente NÃO compra/gera chave nem esconde o banner por CSS
  - Kit OSS substituto definido em D-27 (spartan/ui)
```

## Registros D-16…D-28 — autonomia de longo prazo

### D-16 — Produto

```text
Escolha: VibeChat evolui para plataforma OSS/self-hosted de comunicação,
  conhecimento e automação. Chat excelente continua sendo o núcleo.
Regra: novas superfícies reutilizam identidade, ACL, conversa, audit e outbox.
Não entra: suíte genérica de escritório nem dependência obrigatória de SaaS.
```

### D-17 — Conhecimento e canvas

```text
Escolha: B-120 entrega páginas e coleções server-authoritative com versionamento
  otimista, histórico e referências a mensagens. B-152 adiciona colaboração
  realtime por CRDT somente depois dessa base.
Regra: mesma ACL/retention/export do workspace; nenhuma permissão paralela.
Seleção técnica de CRDT: agente compara OSS e registra ADR.
```

### D-18 — Plugins e distribuição

```text
Escolha: registry assinado é permitido. Instância pode usar catálogo oficial,
  catálogos comunitários allowlisted ou catálogo privado.
Segurança: assinatura, checksum, provenance, compatibilidade, revogação e kill
  switch. Plugin continua config + callbacks/serviço externo; nunca DLL/JS
  não confiável dentro da API.
Fora: billing e checkout dentro do VibeChat.
```

### D-19 — Live

```text
Escolha: áudio/vídeo/screen share entram como módulo opcional self-hosted.
Arquitetura: interface de provider + SFU/TURN OSS em profile separado; não roda
  dentro de apps/api e não é requisito do chat.
Defaults: off; sem gravação; consentimento explícito para gravar/transcrever.
Seleção do stack: agente cria ADR com benchmark, licença e capacidade.
```

### D-20 — Clientes

```text
Escolha: PWA é cliente canônico e define comportamento. Desktop empacotado vem
  depois, seguido de mobile. Todos usam a mesma API/eventos e contract tests.
Offline: fila local criptografada quando disponível, retry idempotente e remote
  logout. Nenhum client ganha endpoint privilegiado próprio.
Tecnologia de empacotamento: escolha OSS por ADR técnico.
```

### D-21 — Bridges e federação

```text
Escolha: primeiro bridges com escopo explícito; depois federação server-to-server
  entre trust domains allowlisted. Descoberta/federação pública fica off.
Regras: consentimento visível, identidade remota marcada, cópia de dados
  documentada, revogação best-effort e audit de entrada/saída.
```

### D-22 — IA

```text
Escolha: contratos provider-neutral; inferência local e externa são adapters.
Externo permanece opt-in. RAG/embeddings são por workspace, revalidam ACL no
  retrieval, propagam edit/delete/purge e retornam citações.
Obrigatório: budget, rate limit, audit de uso, classificação do dado, timeout e
  fallback. Conteúdo do cliente não é usado para treino pelo VibeChat.
```

### D-23 — Compliance

```text
Escolha: baseline LGPD/GDPR e controles técnicos alinhados a SOC 2/ISO 27001.
O produto não declara certificação sem auditoria externa.
Entram: SSO federado (OIDC/SAML via Keycloak, B-164), SCIM, legal hold,
  eDiscovery, DLP, SIEM e cadeia de custódia.
Precedência: legal hold válido suspende purge apenas no escopo registrado; toda
  aplicação/remoção é segregada e auditada.
```

### D-24 — White-label

```text
Escolha: tenant pode configurar nome exibido, logos, favicon e tokens de cor
  dentro de limites de contraste. About/admin preserva versão e origem VibeChat.
Proibido: CSS/JS arbitrário, substituir textos legais, esconder security state.
```

### D-25 — Operação e SLO

```text
Escolha: dois perfis documentados.
  Standard: objetivo 99,9%; detalhes de recuperação refinados por D-28.
  HA: objetivo 99,95%; RPO <= 5 min; RTO <= 30 min, após B-144.
Sem multi-region write nesta linha de produto. K8s, bus e OpenSearch só quando
ADRs 015–017 mostrarem gatilho medido.
SLO é objetivo da referência self-host; não é SLA comercial automático.
```

### D-26 — E2EE

```text
Escolha: canais confidenciais E2EE são opt-in por workspace/canal e off default.
Capacidades reduzidas e visíveis: sem busca/IA/DLP/moderação de conteúdo,
legal hold de body ou preview server-side. Metadata mínima continua auditável.
Chaves não ficam em logs/outbox; perda de chave pode ser irrecuperável.
Escrow organizacional não entra no v1 e exige spec própria futura.
```

### D-27 — Kit UI OSS pós-PrimeNG

```text
Decisão: D-27 — Decidido
Contexto: D-15 mandou sair do PrimeNG. O substituto precisa ser 100% OSS,
  compatível com Angular 22 e com a identidade VibeChat (tokens --vc-*; sem
  aparência genérica de kit de terceiros). O uso atual do PrimeNG está restrito
  ao /admin (Table, Select, Tag); o chat já usa shared/ui.

Comparativo:

  NG-ZORRO (ng-zorro-antd)
  - Licença MIT e componentes maduros de Table/Select/Tag
  - Visual Ant Design opinionated e tema global mais difícil de subordinar a --vc-*
  - Conflita com a identidade própria e a regra de não adotar skin de terceiros

  spartan/ui (@spartan-ng/brain + Helm)
  - Licença MIT; pacote brain headless sobre Angular CDK
  - Peers publicados na data da decisão cobrem Angular 22 e incluem Tailwind CSS v4
  - Estilos Helm são copiados/editáveis: comportamento pronto, visual VibeChat
  - Há primitiva de tabela, mas não um data grid rico equivalente ao PrimeNG
    DataTable; paginação, filtros e ordenação seguem explícitos no produto
  - Custo: introduzir Tailwind v4 de forma limitada, preservando SCSS e --vc-*

Escolha: spartan/ui (não NG-ZORRO)
Data: 2026-07-27
Owner: Founder / Frontend
Impacto em código/docs:
  - Emenda ADR-002: spartan + CDK + tokens; NG-ZORRO rejeitado
  - B-104: desinstalar PrimeNG; adotar @spartan-ng/brain e estilos Helm copiados;
    Select/Tag via primitivas adequadas; tabela semântica + --vc-*
  - B-106 permanece responsável pelo polish do admin após a paridade funcional
  - Chat shell permanece shared/ui; spartan não substitui o shell
  - Validar versões e peers atuais no lock/build durante B-104
```

### D-28 — Perfis de recuperação

```text
Decisão: D-28 — Decidido
Contexto: backup diário com RPO de 24h é aceitável para laboratório/instalação
  básica best effort, mas insuficiente como objetivo de produção de chat.

Escolha:
  Basic/Dev:
  - Compose simples, sem objetivo de disponibilidade;
  - backup completo diário;
  - RPO <= 24h e RTO <= 4h best effort;
  - claramente rotulado como não recomendado para operação crítica.

  Standard de produção:
  - objetivo de disponibilidade 99,9%, sem SLA comercial automático;
  - RPO <= 1h e RTO <= 4h;
  - PostgreSQL com arquivamento contínuo de WAL/PITR;
  - backup completo diário + restore drill;
  - storage de objetos versionado/replicado conforme capacidade;
  - Keycloak/realm, configuração e secrets incluídos no plano;
  - Compose continua permitido; Kubernetes não é requisito.

  HA:
  - objetivo 99,95%, RPO <= 5 min e RTO <= 30 min;
  - somente após B-146/B-144 e evidência de failover/restore.

Regras:
  - backup só conta após restore verificável;
  - operador self-host é responsável por storage/credenciais de backup;
  - release não transforma objetivo em SLA comercial;
  - degradação para um perfil inferior deve ser visível.

Data: 2026-07-27
Owner: Founder + Platform
Impacto:
  - refina D-08/D-25;
  - atualiza backup/restore, operação, B-146 e B-144;
  - não adiciona K8s, multi-region write ou serviço proprietário.
```

## Orientações para agentes

- Features sensíveis (retenção/IA) com **feature flags** e defaults seguros
- Usar placeholders em `.env.example`, nunca valores reais de produção
- Catálogo operacional completo: fase W7-7 / B-105 → `docs/operations/configuracao-env.md`
- Não inventar marca/logo/domínio — usar assets em `apps/web/public/` e o inventário do design-system
- Decisão técnica reversível: agente decide, documenta evidência e cria ADR quando
  necessário; não abrir D-*.
- Abrir nova D-* apenas para licença/marca/legal, gasto/contrato externo,
  credencial/domínio real ou mudança irreversível do modelo de dados/produto que
  não tenha default definido em `docs/agents/autonomia.md`.

## Como fechar uma decisão

1. Owner registra a escolha (issue/ADR curto se técnico)
2. Atualizar esta tabela: Status = Decidido + data + link
3. Propagar para README, LICENSE, ops docs
