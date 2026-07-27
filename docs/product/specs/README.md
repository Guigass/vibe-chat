# Specs de Feature — VibeChat

Uma spec por item de backlog elegível ao Build (Wave 7 de sustentação com UI, e
waves 8–10 de paridade). A automação de Build **só** implementa item que tenha
spec aqui; sem spec, o item não é elegível.

Origem do escopo: `docs/product/benchmark-mensageria.md`. Limites do que pode ou não
ser implementado sem perguntar: **D-11** em `docs/roadmap/decisoes-pendentes.md`.

## Índice

### Sustentação — Wave 7

| ID | Spec |
|----|------|
| B-104 | [Remover PrimeNG (UI própria + CDK)](B-104-remover-primeng.md) |
| B-106 | [Admin shell — nav, filtros e visibilidade por papel](B-106-admin-shell.md) |

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
| B-088 | [Agrupamento, separadores e não lidas na timeline](B-088-timeline-agrupamento-separadores.md) |
| B-089 | [Histórico paginado e pular para a mensagem](B-089-historico-paginado.md) |
| B-090 | [Preview de anexos](B-090-preview-de-anexos.md) |
| B-091 | [Link preview](B-091-link-preview.md) |
| B-092 | [Fixar mensagem](B-092-fixar-mensagem.md) |
| B-093 | [Salvos](B-093-salvos.md) |
| B-094 | [Recibos de leitura e não lidas (persistência definitiva)](B-094-recibos-de-leitura.md) |

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

### P3 — Plugins (por último na trilha)

| ID | Spec |
|----|------|
| B-066 | [Plataforma de plugins — capabilities avançadas](B-066-plugins-plataforma.md) |
| B-111 | [Horizonte plugins](B-111-plugins-horizonte.md) |

## Template

Toda spec nova segue esta estrutura. Seção vazia é sinal de spec incompleta —
prefira escrever “nada” explicitamente a omitir.

```markdown
# B-0XX — Título

> Wave WX-Y · Trilha X · Deps: … · Decisões: D-…

## Problema
Uma ou duas frases. O que o usuário não consegue fazer hoje, com evidência do código atual.

## Escopo
Lista do que entra. Cada linha deve ser verificável.

## Fora de escopo
O que explicitamente não entra nesta fatia, e para onde vai (outro B-, P3, ou D-).

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
