# Specs de Feature — VibeChat

Uma spec por item de backlog elegível ao Build (Waves 7–17). A automação de
Build **só** implementa item que tenha
spec aqui; sem spec, o item não é elegível.

Baseline atualizada em 2026-08-10: itens `Planned` W7–W17 com specs 1:1
(W7-9 / B-165 Done). Regras de auditoria:
[`roadmap/qualidade-documental.md`](../../roadmap/qualidade-documental.md).

Waves 11–17 estão autorizadas por D-16…D-28 e possuem specs executáveis. Todas
incorporam [`long-term-common.md`](long-term-common.md).

Origens: `docs/product/benchmark-mensageria.md`, `visao-longo-prazo.md` e
`mapa-capacidades.md`. Defaults D-01…D-28 estão em
`docs/roadmap/decisoes-pendentes.md`; autoridade de execução em
`docs/agents/autonomia.md`.

## Índice

### Sustentação — Wave 7

| ID | Spec |
|----|------|
| B-076 | [Atualização automatizada de dependências](B-076-atualizacao-dependencias.md) |
| B-077 | [Content Security Policy no web](B-077-csp-web.md) |
| B-078 | [Limite de tamanho do body de mensagem](B-078-limite-body-mensagem.md) |
| B-104 | [Remover PrimeNG (spartan/ui + CDK)](B-104-remover-primeng.md) |
| B-105 | [Catálogo de configuração self-host](B-105-catalogo-configuracao.md) |
| B-106 | [Admin shell — nav, filtros e visibilidade por papel](B-106-admin-shell.md) |
| B-165 | [Controle de versão do cliente web (cache / PWA)](B-165-controle-versao-cliente-web.md) |

### Wave 8 — Composição de mensagem

| ID | Spec |
|----|------|
| B-079 | [Anexos: múltiplos, drag & drop, colar e progresso](B-079-anexos-multiplos-drag-drop.md) |
| B-080 | [Mensagem de áudio](B-080-mensagem-de-audio.md) |
| B-081 | [Formatação de texto](B-081-formatacao-de-texto.md) |
| B-082 | [Menções](B-082-mencoes.md) |
| B-083 | [Emoji picker e reações livres](B-083-emoji-e-reacoes-livres.md) |
| B-084 | [Responder citando](B-084-responder-citando.md) |
| B-085 | [Encaminhar mensagem](B-085-encaminhar-mensagem.md) |
| B-086 | [Rascunho persistente](B-086-rascunho-persistente.md) |
| B-087 | [Comandos slash](B-087-comandos-slash.md) |

### Wave 9 — Leitura da timeline

| ID | Spec |
|----|------|
| B-163 | [Message bubble moderno (layout tipado + ações + preview)](B-163-message-bubble-context-menu.md) |
| B-088 | [Agrupamento, separadores e não lidas na timeline](B-088-timeline-agrupamento-separadores.md) |
| B-089 | [Histórico paginado e pular para a mensagem](B-089-historico-paginado.md) |
| B-090 | [Preview de anexos](B-090-preview-de-anexos.md) |
| B-091 | [Link preview](B-091-link-preview.md) |
| B-092 | [Fixar mensagem](B-092-fixar-mensagem.md) |
| B-093 | [Salvos](B-093-salvos.md) |
| B-094 | [Recibos de leitura e não lidas (persistência definitiva)](B-094-recibos-de-leitura.md) |
| B-168 | [Anexo de vídeo (aceite, preview e visualização)](B-168-anexo-de-video.md) |

### Wave 10 — Notificações, organização e acesso

| ID | Spec |
|----|------|
| B-095 | [Web Push](B-095-web-push.md) |
| B-096 | [Enquetes](B-096-enquetes.md) |
| B-097 | [Preferências de notificação e DND](B-097-preferencias-notificacao-dnd.md) |
| B-098 | [Busca com filtros](B-098-busca-com-filtros.md) |
| B-099 | [Paleta de comandos e atalhos](B-099-paleta-de-comandos.md) |
| B-100 | [Internacionalização](B-100-i18n.md) |
| B-101 | [DM em grupo](B-101-dm-em-grupo.md) |
| B-102 | [Seguir thread](B-102-seguir-thread.md) |
| B-103 | [Acessibilidade WCAG 2.2 AA](B-103-acessibilidade.md) |
| B-040 | [Guests por convite](B-040-guests-por-convite.md) |
| B-107 | [Políticas de edição/apagar mensagem](B-107-politicas-edicao-mensagem.md) |
| B-108 | [Extender webhooks outbound](B-108-extender-webhooks.md) |
| B-109 | [Núcleo plugin — bot/token + envio](B-109-api-integracao-envio-mensagens.md) |
| B-110 | [Instalar/gerir plugins na instância](B-110-instalar-plugins.md) |

### Wave 11 — Organização e comunicação

| ID | Spec |
|----|------|
| B-112 | [Anúncios e canais somente leitura](B-112-anuncios-canais-leitura.md) |
| B-113 | [Agendamento e lembretes](B-113-agendamento-lembretes.md) |
| B-114 | [Histórico de edição e movimentação](B-114-historico-edicao-movimentacao.md) |
| B-115 | [Templates e onboarding](B-115-templates-onboarding.md) |
| B-153 | [Migração e importação assistida](B-153-migracao-importacao.md) |
| B-154 | [Diagnóstico e support bundle](B-154-diagnostico-support-bundle.md) |
| B-116 | [Status e disponibilidade](B-116-status-disponibilidade.md) |
| B-117 | [Inbox unificada](B-117-inbox-unificada.md) |
| B-118 | [Canais por tópicos/fórum](B-118-canais-topicos-forum.md) |

### Wave 12 — Conhecimento e foco

| ID | Spec |
|----|------|
| B-119 | [Decisões e action items](B-119-decisoes-action-items.md) |
| B-120 | [Base de conhecimento](B-120-base-conhecimento.md) |
| B-121 | [RAG e busca semântica](B-121-rag-busca-semantica.md) |
| B-122 | [Digests e catch-up](B-122-digests-catch-up.md) |
| B-123 | [Tarefas derivadas de mensagens](B-123-tarefas-mensagens.md) |
| B-124 | [Formulários e aprovações](B-124-formularios-aprovacoes.md) |

### Wave 13 — Automação e operações

| ID | Spec |
|----|------|
| B-125 | [Automation builder](B-125-automation-builder.md) |
| B-126 | [Incident rooms e playbooks](B-126-incident-rooms-playbooks.md) |
| B-127 | [Conectores bidirecionais](B-127-conectores-bidirecionais.md) |
| B-128 | [SCIM e grupos](B-128-scim-grupos.md) |
| B-131 | [Malware scanning e quarentena](B-131-malware-quarentena.md) |
| B-139 | [Eventos versionados e schema registry](B-139-eventos-versionados.md) |

### Wave 14 — Enterprise soberano

| ID | Spec |
|----|------|
| B-129 | [Legal hold e eDiscovery](B-129-legal-hold-ediscovery.md) |
| B-169 | [Modo auditoria de conteúdo por tenant](B-169-modo-auditoria-conteudo-tenant.md) |
| B-130 | [Classificação e DLP](B-130-classificacao-dlp.md) |
| B-132 | [Audit para SIEM](B-132-audit-siem.md) |
| B-133 | [Policy packs](B-133-policy-packs.md) |
| B-134 | [Administração delegada e quotas](B-134-admin-delegada-quotas.md) |
| B-146 | [Capacity model e SLOs](B-146-capacity-slos.md) |

### Wave 15 — Plataforma e ecossistema

| ID | Spec |
|----|------|
| B-135 | [Developer portal](B-135-developer-portal.md) |
| B-066 | [Plataforma de plugins — capabilities avançadas](B-066-plugins-plataforma.md) |
| B-111 | [Interações governadas para plugins](B-111-plugins-horizonte.md) |
| B-136 | [SDK e contract-test kit](B-136-sdk-contract-kit.md) |
| B-137 | [Registry de plugins](B-137-registry-plugins.md) |
| B-138 | [Framework de bridges](B-138-framework-bridges.md) |
| B-140 | [White-label](B-140-white-label.md) |
| B-141 | [Cliente desktop](B-141-cliente-desktop.md) |
| B-063 | [Clientes mobile](B-063-clientes-mobile.md) |

### Wave 16 — Escala e continuidade

| ID | Spec |
|----|------|
| B-143 | [Offline sync](B-143-offline-sync.md) |
| B-145 | [Lifecycle e quotas de storage](B-145-lifecycle-storage.md) |
| B-144 | [HA e rolling upgrade](B-144-ha-rolling-upgrade.md) |
| B-065 | [Federação](B-065-federacao.md) |
| B-064 | [Canais E2EE](B-064-canais-e2ee.md) |

### Wave 17 — Colaboração avançada

| ID | Spec |
|----|------|
| B-147 | [Huddles de áudio/vídeo](B-147-huddles-live.md) |
| B-148 | [Screen share e gravação](B-148-screen-share-gravacao.md) |
| B-149 | [Transcrição e notas de reunião](B-149-notas-reuniao-ia.md) |
| B-152 | [Canvas colaborativo](B-152-canvas-colaborativo.md) |

### Wave 18 — Bots internos com IA

| ID | Spec |
|----|------|
| B-155 | [Catálogo e versionamento de bots](B-155-catalogo-versionamento-bots-ia.md) |
| B-156 | [Skills personalizadas para bots](B-156-skills-personalizadas-bots.md) |
| B-157 | [Fontes de conhecimento e Qdrant](B-157-fontes-conhecimento-qdrant.md) |
| B-158 | [Servidores MCP e grants](B-158-servidores-mcp-grants.md) |
| B-159 | [Auditoria e observabilidade de bots](B-159-auditoria-observabilidade-bots.md) |
| B-160 | [Guardrails e políticas para bots](B-160-guardrails-politicas-bots.md) |
| B-161 | [Runtime conversacional de bots](B-161-runtime-conversacional-bots.md) |
| B-162 | [Avaliação, publicação e templates](B-162-avaliacao-publicacao-templates-bots.md) |

## Template

Toda spec nova segue esta estrutura. Seção vazia é sinal de spec incompleta —
prefira escrever “nada” explicitamente a omitir.

```markdown
# B-0XX — Título

> Wave WX-Y · Trilha X · Deps: … · Decisões: D-… · Risco R0|R1|R2|R3

## Problema
Uma ou duas frases. O que o usuário não consegue fazer hoje, com evidência do código atual.

## Escopo
Lista do que entra. Cada linha deve ser verificável.

## Fora de escopo
O que explicitamente não entra nesta fatia, e para onde vai (outro B-, wave ou D-).

## Contratos
Endpoints, eventos de hub, schema e migrations. Se muda contrato público,
`docs/architecture/contratos.md` entra no mesmo PR.

## UX
Comportamento na interface, estados vazio/carregando/erro, tokens do design system.

## Multi-tenant e authZ
`tenant_id`, permissão exigida, o que um usuário sem permissão vê.

## Aceite
Checklist objetivo. É isso que o QA verifica.

## Testes
Quais suítes e quais casos, incluindo o caso cross-tenant negativo.

## Riscos
O que pode dar errado e como mitigar.
```

## Regras que valem para todas

- `tenant_id` + authZ + RLS em todo caminho de dado novo (`AGENTS.md`).
- Mutação de mensagem: idempotência + `seq` + outbox.
- Nada de dependência proprietária; nada de secret em log ou commit.
- UI usa tokens de `docs/architecture/design-system.md`; sem clonar Slack/Discord/WhatsApp.
- Toda ação de arrastar precisa de alternativa por clique (WCAG 2.2 — 2.5.7).
- Texto de UI em `pt-BR` e `en` a partir de B-100; antes disso, `pt-BR`.
- Feature de risco entra atrás de flag com default seguro.
