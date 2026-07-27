# B-098 — Busca com filtros

> Wave W10-4 · Trilha C/D · Deps: B-089 · Decisões: D-11 · Risco R2

## Problema

A busca é uma caixa de texto que devolve 12 resultados sem nenhum recorte
(`shell.page.ts`, `limit: 12`). Achar “o PDF que a Alice mandou mês passado” é
impossível.

## Escopo

- Filtros: autor, canal, intervalo de datas, tem anexo, tem link, tipo de anexo.
- Sintaxe no próprio campo: `de:@alice em:#geral antes:2026-07-01 tem:anexo`,
  com autocomplete de cada operador.
- Página de resultados com paginação, trecho com o termo destacado e ordenação por
  relevância ou data.
- Clicar leva à mensagem (B-089).
- Buscas recentes (local, por usuário).
- Escopo: workspace inteiro ou canal atual.

## Fora de escopo

- Busca semântica/vetorial (ADR-016 é o gatilho para OpenSearch); busca dentro de PDF;
  busca por conteúdo de áudio (só a transcrição, se existir).

## Contratos

`GET /api/v1/workspaces/{workspaceId}/search/messages` ganha:

| Param | Nota |
|-------|------|
| `authorId` | filtro por autor |
| `channelId` | filtro por canal |
| `from` / `to` | intervalo (ISO-8601) |
| `hasAttachment`, `hasLink` | bool |
| `attachmentKind` | `image`\|`audio`\|`document` |
| `sort` | `relevance` (default) \| `date` |
| `cursor`, `limit` | paginação; máx. 50 |

Resposta ganha `total` (aproximado) e `cursor`. Índices: já existe GIN de `tsvector`;
acrescentar índice composto (`TenantId`, `ChannelId`, `CreatedAt`) para o recorte por
data.

`contratos.md`: parâmetros, resposta e a sintaxe dos operadores.

## UX

- Campo com chips dos filtros aplicados, removíveis um a um.
- Autocomplete: digitar `de:` sugere membros; `em:` sugere canais onde há membership.
- Resultados agrupados por canal, com contagem por grupo.
- Vazio explica o que tentar (“remova um filtro”), em vez de só “nada encontrado”.
- Buscas recentes abaixo do campo enquanto ele está vazio.

## Multi-tenant e authZ

- O filtro de membership continua sendo aplicado **no servidor**, sempre — filtro do
  usuário só restringe, nunca amplia.
- `authorId` ou `channelId` de outro tenant → 403.
- Autocomplete de canal só lista canais com membership; o de autor, só membros
  visíveis ao usuário.
- Guest (B-040) não usa busca global; escopo forçado ao canal do convite.

## Aceite

- [ ] `de:@alice tem:anexo` devolve só anexos da Alice
- [ ] Intervalo de datas recorta corretamente nas bordas
- [ ] Paginação avança sem repetir resultado
- [ ] Clicar leva à mensagem destacada
- [ ] Filtro por canal sem membership → 403
- [ ] Ordenar por data muda a ordem
- [ ] Busca em workspace com 50k mensagens responde em tempo aceitável

## Testes

- Integration: cada filtro isolado e combinados; bordas de data; paginação estável.
- Security: cross-tenant em `authorId`/`channelId` → 403; resultado nunca inclui
  canal sem membership.
- Unit (web): parser dos operadores, chips, autocomplete.
- Load: consulta filtrada no dataset de `tests/load`.

## Riscos

- Consulta lenta com filtro de data em base grande → índice composto e `EXPLAIN` no PR.
- Sintaxe de operador confundindo quem não conhece → chips e autocomplete tornam a
  sintaxe descobrível; digitar texto puro continua funcionando.
