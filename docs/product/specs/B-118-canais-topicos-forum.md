# B-118 — Canais por tópicos/fórum

> Wave 11 · Trilha B/C/D · Deps: B-089, B-102 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Threads ancoradas em mensagens funcionam para replies, mas canais movimentados
precisam de conversas nomeadas que possam ser descobertas e acompanhadas.

## Escopo

- Novo modo de channel `Topics`.
- Tópico tem título, slug local, estado open/closed e tags.
- Cada tópico reutiliza Conversation e read cursor.
- Criar, renomear, seguir, fechar e mover tópico.
- Lista recent/unread com participantes.
- Migration explícita; canal normal não muda de semântica sozinho.

## Fora de escopo

- Hierarquia infinita de tópicos.
- Tags globais cross-tenant.
- Substituir threads existentes em canais normais.

## Contratos

Modelo Topic ligado a Channel/Conversation; eventos duráveis; endpoint de
paginação. ADR documenta relação Topic/Thread e evita agregados duplicados.

## UX

Visão de tópicos como lista principal; composer sempre mostra destino. Navegação
por teclado, filtros e estado fechado read-only.

## Multi-tenant e authZ

Herda membership do channel; criação/gestão por permissions separadas. Tags e
previews nunca vazam canal privado.

## Aceite

- [ ] Dois tópicos têm `seq` independente.
- [ ] Follow/unread integra com inbox.
- [ ] Closed bloqueia post sem permissão.
- [ ] ACL e search respeitam channel.
- [ ] Canal normal permanece inalterado.

## Testes

Architecture/domain, integration seq/outbox, security privado/cross-tenant,
search e E2E de criar/seguir/fechar.

## Riscos

Duplicar Thread/Conversation e confundir navegação. Reusar primitivas e exigir
ADR antes da migration.

