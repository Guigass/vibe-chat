# B-120 — Base de conhecimento leve

> Wave 12 · Trilha B/C/D · Deps: B-119, D-17 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Pins e mensagens não bastam para manter procedimentos, FAQs e decisões curadas
em uma estrutura navegável.

## Escopo

- Pages server-authoritative em Markdown restrito/blocos básicos.
- Collections por workspace/space/channel.
- Versionamento otimista, histórico e restore.
- Referências embutidas a messages, decisions, tasks e attachments.
- Permissões de view/edit/manage.
- Busca, export e retention.

## Fora de escopo

- Coedição realtime/CRDT, reservada a B-152.
- JavaScript/HTML arbitrário.
- Banco de dados/no-code dentro da page.

## Contratos

Page/Collection com TenantId, version e ACL herdada/explicita. `ETag`/version em
update; evento `knowledge.page.changed`; schema renderizável e versionado.

## UX

Editor com preview, conflito amigável, autosave de rascunho local e navegação em
árvore rasa. Referências mostram origem e permission state.

## Multi-tenant e authZ

RLS; ACL nunca mais ampla que o workspace. Referência a conteúdo inacessível é
redigida. Export respeita escopo/admin.

## Aceite

- [ ] Conflito não sobrescreve silenciosamente.
- [ ] Histórico restaura versão.
- [ ] Busca respeita ACL.
- [ ] Referência revogada é redigida.
- [ ] Export/retention cobrem page e versões.

## Testes

Concurrency/integration, security ACL/ref, render sanitization, search/export e
E2E editor/conflito.

## Riscos

Virar suíte de documentos. Limitar blocos e profundidade; qualquer expansão usa
B-152 ou novo item.

