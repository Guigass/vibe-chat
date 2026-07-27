# Pesquisa de Horizontes de Produto

Referências oficiais consultadas em 2026-07-27 para inspirar o horizonte
ambicioso. O objetivo é identificar padrões de valor, não reproduzir interfaces
ou importar arquiteturas.

## Sinais observados

### Conversas assíncronas orientadas a tópicos

O Zulip coloca tópicos no centro da conversa e oferece inbox/recent conversations
para navegar discussões sem depender de uma única timeline. A oportunidade para
o VibeChat é explorar **modo de canal por tópicos**, inbox e catch-up, preservando
o modelo atual de channel/thread.

Fontes: [Introduction to topics](https://zulip.com/help/introduction-to-topics),
[Getting started with Zulip](https://zulip.com/help/getting-started-with-zulip).

### Conversa conectada a trabalho estruturado

Slack combina conversas com canvas, listas e workflows. A lição útil é que
mensagem ganha valor quando pode virar decisão, tarefa, formulário ou processo.
Para o VibeChat, tarefas e playbooks são candidatos menores e mais seguros que
abrir imediatamente um editor colaborativo completo.

Fontes: [Slack features](https://slack.com/features),
[Slack Canvas](https://slack.com/features/canvas),
[Automations for lists](https://slack.com/help/articles/37752114318227-Set-up-automations-for-lists-in-Slack).

### Interoperabilidade amplia alcance e risco

Matrix demonstra o valor de homeservers, bridges e APIs abertas, mas também a
complexidade de namespaces, impersonation, cópias de dados e operação contínua.
O próprio material de privacidade alerta que conteúdo bridged/federado passa a
ser tratado por outros serviços e jurisdições. No VibeChat, bridge e federação
precisam de gate de governança, consentimento e retenção; não são apenas
“conectores”.

Fontes: [Elements of Matrix](https://matrix.org/docs/matrix-concepts/elements-of-matrix/),
[Matrix bridges](https://matrix.org/ecosystem/bridges/),
[Matrix privacy policy](https://matrix.org/legal/privacy-notice/).

### Live collaboration cria outro plano operacional

Áudio/vídeo, screen share e notas de reunião podem gerar grande valor, mas
introduzem mídia em tempo real, TURN/SFU, consentimento, gravação e retenção.
O VibeChat deve manter live como aposta separada da mensagem de áudio assíncrona.

Fonte: [Slack Huddles](https://slack.com/features/huddles).

## Teses para o VibeChat

| Tese | Experimento documental/produto |
|------|--------------------------------|
| Times sofrem mais com ruído que com falta de mensagens | Inbox, tópicos, digests e prioridade antes de live |
| Conversa perde valor quando decisão e ação somem na timeline | Decisão/action item vinculados à origem |
| Self-hosted vence quando administração é previsível | Policy packs, provisioning e observabilidade por porte |
| Ecossistema só é sustentável com contratos pequenos | Bot/token → install local → SDK → distribuição governada |
| IA gera confiança quando mostra fonte e respeita ACL | RAG opcional com citações, auditoria e orçamento |
| Interoperabilidade sem governança dilui soberania | Bridge/federação somente após D-21 |

## Anti-teses

- “Ter mais features que concorrentes” não é estratégia.
- Um canvas CRDT não deve nascer apenas porque mensagens podem ser fixadas.
- Marketplace público não deve preceder assinatura, revisão e modelo de suporte.
- Chamadas não devem entrar sem capacidade operacional e política de gravação.
- IA não deve indexar todo o tenant por conveniência.
- Cliente mobile nativo não deve criar contratos paralelos aos do web.

## Como usar esta pesquisa

Ideias daqui entram primeiro em
[`mapa-capacidades.md`](mapa-capacidades.md) e no
[`horizonte-ambicioso.md`](../roadmap/horizonte-ambicioso.md). Só viram spec
executável após decisão/gate, dependências e critérios de aceite.

