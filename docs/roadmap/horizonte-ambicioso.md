# Roadmap de Longo Prazo — VibeChat

Roadmap executável posterior às Waves 7–10. D-16…D-28 foram decididas em
2026-07-27; todos os itens abaixo estão autorizados e entram como `Planned`.
O Build só os inicia quando as dependências anteriores estiverem `Done` e a spec
individual existir.

## Regras de execução

1. concluir Waves 7–10 antes da Wave 11;
2. respeitar a ordem de wave e dependências por ID;
3. uma intenção por PR, salvo quando a spec exigir migration+contrato+UI juntos;
4. decisões técnicas reversíveis são do agente; mudança arquitetural gera ADR;
5. feature de risco nasce off por default e com rollback;
6. contratos, authZ, tenancy, retenção, observabilidade e operação fazem parte
   do item, não são follow-up opcional;
7. descoberta adicional é permitida dentro do item, sem reabrir decisão humana.
8. item R3 começa pelos defaults de
   [`pacotes-decisao-r3.md`](../architecture/pacotes-decisao-r3.md); o ADR escolhe
   tecnologia sem reabrir produto.

## Critério de priorização histórico

O método abaixo foi usado para ordenar o portfólio antes da promoção. A ordem
resultante já está decidida; agentes não recalculam prioridade durante a execução.

| Dimensão | Pergunta |
|----------|----------|
| Valor | Resolve problema frequente e relevante? |
| Diferenciação | Reforça self-host, soberania ou extensibilidade? |
| Alcance | Quantas personas/organizações se beneficiam? |
| Confiança | Temos evidência e clareza suficientes? |
| Custo | Quanto desenvolvimento e operação permanentes adiciona? |
| Risco | Quanto amplia segurança, compliance e superfície de falha? |

Ordem sugerida: `(Valor + Diferenciação + Alcance + Confiança) - (Custo + Risco)`.
O número orienta conversa; não substitui julgamento de produto.

## Wave 11 — Organização e comunicação

Objetivo: tornar a comunicação diária mais clara antes de criar superfícies
completamente novas.

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| B-112 | B/C/D | Anúncios e canais somente leitura, com confirmação opcional | W9-7, B-041 | [B-112](../product/specs/B-112-anuncios-canais-leitura.md) | Planned |
| B-113 | C/D | Agendar mensagem, lembrete pessoal e “lembrar deste item” | W9-7, B-093 | [B-113](../product/specs/B-113-agendamento-lembretes.md) | Planned |
| B-114 | C/D/E | Histórico de edição e movimentação de mensagens | B-107, B-089 | [B-114](../product/specs/B-114-historico-edicao-movimentacao.md) | Planned |
| B-115 | B/D | Templates de workspace/channel e onboarding guiado | B-106 | [B-115](../product/specs/B-115-templates-onboarding.md) | Planned |
| B-153 | B/C/D/E/G | Migração/importação assistida de usuários, estrutura e histórico | B-089, B-115, B-046 | [B-153](../product/specs/B-153-migracao-importacao.md) | Planned |
| B-154 | A/B/D/E/G | Diagnóstico administrativo e support bundle sanitizado | B-105, B-106, B-115 | [B-154](../product/specs/B-154-diagnostico-support-bundle.md) | Planned |
| B-116 | B/D | Status personalizado, disponibilidade e agenda resumida | B-097 | [B-116](../product/specs/B-116-status-disponibilidade.md) | Planned |
| B-117 | C/D | Inbox unificada com menções, threads, DMs e prioridade | B-094, B-102 | [B-117](../product/specs/B-117-inbox-unificada.md) | Planned |
| B-118 | B/C/D | Modo de canal por tópicos/fórum | B-089, B-102 | [B-118](../product/specs/B-118-canais-topicos-forum.md) | Planned |

### Critérios de saída

- timeline e não lidas persistentes estáveis;
- modelo claro para anúncio versus mensagem;
- inbox não cria uma segunda fonte de read state;
- topics reutilizam Conversation/Thread ou justificam mudança por ADR.
- migração usa dry-run, staging e adapters versionados sem criar credenciais;
- diagnóstico/support bundle usa allowlist e não inclui conteúdo ou secrets.

## Wave 12 — Conhecimento e foco

Objetivo: transformar conversa em memória institucional sem abrir um editor CRDT
por acidente.

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| B-119 | B/C/D | Decisões e action items vinculados à mensagem de origem | B-092, B-093 | [B-119](../product/specs/B-119-decisoes-action-items.md) | Planned |
| B-120 | B/C/D | Base de conhecimento leve, coleções e páginas curadas | B-119, D-17 | [B-120](../product/specs/B-120-base-conhecimento.md) | Planned |
| B-121 | B/C/D/E | Busca semântica/RAG com ACL, citações e opt-in | B-098, D-22 | [B-121](../product/specs/B-121-rag-busca-semantica.md) | Planned |
| B-122 | C/D | Digests programados e catch-up por canal/time | B-117 | [B-122](../product/specs/B-122-digests-catch-up.md) | Planned |
| B-123 | C/D | Tarefas pessoais/de time derivadas de mensagens | B-119 | [B-123](../product/specs/B-123-tarefas-mensagens.md) | Planned |
| B-124 | B/C/D | Formulários, aprovações e coleta estruturada | B-123 | [B-124](../product/specs/B-124-formularios-aprovacoes.md) | Planned |

### Critérios de saída

- toda decisão/tarefa aponta para evidência original;
- exclusão/retention propaga para projeções;
- IA nunca amplia ACL e sempre informa fonte;
- D-17 define se existe ou não nova superfície de conteúdo.

## Wave 13 — Automação e operações de time

Objetivo: permitir que equipes executem processos repetíveis dentro do contexto
da conversa.

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| B-125 | B/C/D | Automation builder com triggers, condições e ações | B-108, B-110, B-124 | [B-125](../product/specs/B-125-automation-builder.md) | Planned |
| B-126 | B/C/D/E | Incident rooms, timeline e playbooks operacionais | B-112, B-119, B-125 | [B-126](../product/specs/B-126-incident-rooms-playbooks.md) | Planned |
| B-127 | B/C/D/E | Conectores bidirecionais GitHub/GitLab/Jira/calendário | B-108, B-125 | [B-127](../product/specs/B-127-conectores-bidirecionais.md) | Planned |
| B-128 | B/D/E | SCIM 2.0, grupos e deprovisioning | B-041, B-106, D-23 | [B-128](../product/specs/B-128-scim-grupos.md) | Planned |
| B-131 | C/E/A | Malware scanning, quarentena e política de download | B-090 | [B-131](../product/specs/B-131-malware-quarentena.md) | Planned |
| B-139 | B/C/E/G | Versionamento formal de eventos e schema registry | B-108, B-109 | [B-139](../product/specs/B-139-eventos-versionados.md) | Planned |

### Critérios de saída

- automações são idempotentes, auditáveis e limitadas;
- conectores têm secrets mascarados, rotação e circuit breaker;
- deprovisioning define ownership e sessões;
- schema de eventos tem compatibilidade e depreciação testadas.

## Wave 14 — Enterprise soberano

Objetivo: operar com segurança, compliance e delegação em organizações maiores.

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| B-129 | B/C/D/E | Legal hold e eDiscovery | B-046, B-114, D-23 | [B-129](../product/specs/B-129-legal-hold-ediscovery.md) | Planned |
| B-130 | B/C/D/E | Classificação de dados e DLP | B-129, D-23 | [B-130](../product/specs/B-130-classificacao-dlp.md) | Planned |
| B-132 | B/E/A | Audit streaming/export para SIEM | B-042, B-108 | [B-132](../product/specs/B-132-audit-siem.md) | Planned |
| B-133 | B/D/E | Policy packs por tipo de organização | B-128, B-130 | [B-133](../product/specs/B-133-policy-packs.md) | Planned |
| B-134 | B/D/E | Administração delegada, escopos e quotas | B-128 | [B-134](../product/specs/B-134-admin-delegada-quotas.md) | Planned |
| B-146 | A/E/F | Benchmarks por porte, capacity model e SLOs | Wave 10 Done | [B-146](../product/specs/B-146-capacity-slos.md) | Planned |

### Critérios de saída

- matriz de papéis/escopos revisada;
- policy pack nunca reduz controle obrigatório silenciosamente;
- eventos enviados ao SIEM não vazam conteúdo sem política;
- malware scan tem estados claros, timeout e fallback seguro.
- legal hold tem precedência explícita e auditada sobre purge.

## Wave 15 — Plataforma e ecossistema

Objetivo: tornar extensibilidade um produto sustentável.

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| B-135 | B/D/G | Developer portal, credenciais e documentação de API | B-109, B-110 | [B-135](../product/specs/B-135-developer-portal.md) | Planned |
| B-066 | B/C/D/E | Plataforma de plugins com capabilities avançadas | B-108, B-109, B-110 | [B-066](../product/specs/B-066-plugins-plataforma.md) | Planned |
| B-111 | B/C/D/E | Interações, histórico e anexos governados para plugins | B-066 | [B-111](../product/specs/B-111-plugins-horizonte.md) | Planned |
| B-136 | B/C/E/G | SDK e contract-test kit para plugins | B-066, B-139 | [B-136](../product/specs/B-136-sdk-contract-kit.md) | Planned |
| B-137 | B/C/D/E | Registry assinado e catálogos governados | B-136, D-18 | [B-137](../product/specs/B-137-registry-plugins.md) | Planned |
| B-138 | B/C/E | Framework de bridges para redes externas | B-136, D-21 | [B-138](../product/specs/B-138-framework-bridges.md) | Planned |
| B-140 | D/G/B | White-label e branding por tenant | B-100, D-24 | [B-140](../product/specs/B-140-white-label.md) | Planned |
| B-141 | D/A/E | Cliente desktop empacotado | W10-9, D-20 | [B-141](../product/specs/B-141-cliente-desktop.md) | Planned |
| B-063 | D/A/E | Clientes mobile nativos | B-141, D-20 | [B-063](../product/specs/B-063-clientes-mobile.md) | Planned |

### Critérios de saída

- assinatura, provenance, revogação e compatibilidade de plugins definidas;
- bridge explicita cópia de dados e identidade remota;
- SDK deriva do contrato, não vira contrato paralelo;
- clients adicionais reutilizam APIs e regras de segurança.

## Wave 16 — Escala e continuidade

Objetivo: suportar organizações e instalações maiores por evidência, não por
arquitetura preventiva.

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| B-143 | C/D/E | Offline sync real e fila local confiável | B-089, B-094, D-20 | [B-143](../product/specs/B-143-offline-sync.md) | Planned |
| B-145 | A/C/E | Lifecycle de objetos, quotas de storage e CDN opcional | B-131, B-134 | [B-145](../product/specs/B-145-lifecycle-storage.md) | Planned |
| B-144 | A/B/C/E/F | HA, rolling upgrade e zero-downtime documentado | B-146, D-25, D-28 | [B-144](../product/specs/B-144-ha-rolling-upgrade.md) | Planned |
| B-065 | B/C/A/E | Federação entre instâncias | B-138, B-146, D-21 | [B-065](../product/specs/B-065-federacao.md) | Planned |
| B-064 | B/C/D/E | Canais confidenciais E2EE | B-129, D-26 | [B-064](../product/specs/B-064-canais-e2ee.md) | Planned |

### Critérios de saída

- ADRs 015–017 avaliados com métricas;
- RPO/RTO/SLO definidos para o novo porte;
- upgrade e rollback testados com dados representativos;
- offline/federação não quebram ordenação, revogação ou retenção.

## Wave 17 — Colaboração ao vivo e superfícies avançadas

Objetivo: apostas transformadoras, intencionalmente por último.

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| B-147 | B/C/D/A/E | Huddles de áudio/vídeo self-hosted | B-146, D-19 | [B-147](../product/specs/B-147-huddles-live.md) | Planned |
| B-148 | C/D/A/E | Screen share, gravação opcional e moderação | B-147, D-19 | [B-148](../product/specs/B-148-screen-share-gravacao.md) | Planned |
| B-149 | C/D/AI/E | Transcrição e notas de reunião com consentimento | B-147, D-22 | [B-149](../product/specs/B-149-notas-reuniao-ia.md) | Planned |
| B-152 | B/C/D/E | Canvas/documento colaborativo | B-120, D-17 | [B-152](../product/specs/B-152-canvas-colaborativo.md) | Planned |

### Critérios de saída

- SFU/TURN, capacidade, gravação e consentimento definidos;
- mídia não degrada a mensageria principal;
- notas de IA têm fonte, consentimento e retenção;
- canvas tem modelo de permissão, export e conflito antes de escolher CRDT/OT.

## Wave 18 — Bots internos com IA

Objetivo: permitir múltiplos bots corporativos especializados, com personalidade,
modelo, skills, conhecimento e tools diferentes, sem ampliar ACL nem transformar
o modelo em autoridade implícita.

Arquitetura canônica:
[`plataforma-bots-ia.md`](../architecture/plataforma-bots-ia.md). Qdrant é
projeção opcional conforme
[ADR-019](../adrs/ADR-019-qdrant-para-vetores-de-conhecimento.md).

| ID | Trilha | Tarefa | Deps | Spec | Status |
|----|--------|--------|------|------|--------|
| B-155 | B/C/D/E | Catálogo e versionamento de bots: system prompt, modelo, escopos e publicação | B-109, B-110, B-121, D-22 | [B-155](../product/specs/B-155-catalogo-versionamento-bots-ia.md) | Planned |
| B-156 | B/C/D/E | Skills personalizadas, declarativas e versionadas | B-155 | [B-156](../product/specs/B-156-skills-personalizadas-bots.md) | Planned |
| B-157 | A/B/C/D/E/AI | Fontes por arquivo/URL/página, ingestão e vetores no Qdrant | B-120, B-121, B-131, B-155 | [B-157](../product/specs/B-157-fontes-conhecimento-qdrant.md) | Planned |
| B-158 | B/C/D/E/AI | Registro de servidores MCP, catálogo de tools e grants mínimos | B-066, B-111, B-127, B-139, B-155 | [B-158](../product/specs/B-158-servidores-mcp-grants.md) | Planned |
| B-159 | B/C/D/E/F/AI | Auditoria, custos e observabilidade de configuração/runs/tools | B-132, B-139, B-155, B-157, B-158 | [B-159](../product/specs/B-159-auditoria-observabilidade-bots.md) | Planned |
| B-160 | B/C/D/E/AI | Guardrails, policy, budgets e aprovação de ações | B-130, B-133, B-155, B-158, B-159 | [B-160](../product/specs/B-160-guardrails-politicas-bots.md) | Planned |
| B-161 | B/C/D/E/AI | Runtime conversacional por DM/menção com RAG, skills e MCP | B-156, B-157, B-158, B-159, B-160 | [B-161](../product/specs/B-161-runtime-conversacional-bots.md) | Planned |
| B-162 | B/C/D/E/G/AI | Avaliação, canary/rollback e templates Oráculo/ERP/Treinador/Geral | B-161 | [B-162](../product/specs/B-162-avaliacao-publicacao-templates-bots.md) | Planned |

### Critérios de saída

- múltiplos bots possuem versões reproduzíveis de system prompt, model binding,
  skills, knowledge grants, MCP grants e guardrails;
- fontes em arquivo/URL respeitam scan, SSRF, ACL, retenção, citações e delete
  propagation; Qdrant pode ser reconstruído sem afetar o domínio;
- autoridade efetiva é a interseção usuário + bot + fonte/tool + policy atual;
- tool MCP read-only funciona com menor privilégio; escrita/egress exige policy
  e aprovação explícita quando aplicável;
- logs/audit não armazenam secret, chain-of-thought ou prompt integral por
  default, mas explicam versão, fontes, tools, decisões e custo;
- evals adversariais, staged rollout e rollback bloqueiam publicação insegura;
- chat continua operacional com IA, Qdrant ou MCP indisponíveis.

## Portfólio por natureza

| Evolução natural | Diferenciação forte | Aposta arquitetural |
|------------------|---------------------|---------------------|
| Anúncios, agendamento, inbox, status | Decisões, digests, playbooks, policy packs | Live media |
| Histórico, templates, malware scan | Developer portal, SDK, automation builder | Federação |
| Audit SIEM, quotas, lifecycle | RAG autorizado, bots internos e MCP governado | E2EE |
| Capacity model | Enterprise governance | Canvas CRDT |
| | Plugins/bridges governados | Mobile/offline completo |

## Sequência obrigatória

1. Concluir Waves 7–10.
2. Executar W11 → W12 → W13 → W14.
3. Estabilizar contratos antes de SDK/registry/bridges.
4. Medir porte real antes de HA, bus, OpenSearch ou Kubernetes.
5. Executar W15 → W16 → W17 → W18, respeitando dependências.
6. Publicar bots somente após audit, guardrails e evals da própria Wave 18.
7. Abrir no máximo **uma aposta arquitetural** por vez.
