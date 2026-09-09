# B-188 — Busca versátil (relevância e matching)

> Wave W10-16 · Trilha C/D · Deps: B-098 · Soft-deps: BUG-023 · Decisões: D-11 · Risco R2

## Problema

B-027 e B-098 entregaram FTS + filtros, mas o matching é rígido: `plainto_tsquery`
só no `Body`, lexema inteiro, sem prefixo, sem tolerância a acento/typo e sem
achar canal, pessoa ou nome de anexo. Relato de produto (BUG-023): os resultados
não são satisfatórios; a busca precisa ser versátil no dia a dia, sem esperar
RAG (B-121) nem OpenSearch (B-060 / ADR-016).

## Escopo

- Matching mais permissivo no Postgres FTS (ADR-011): prefixo (`:*`),
  `websearch_to_tsquery` (frase entre aspas, `OR`, `-termo`) e acento
  irrelevante (`unaccent` / config equivalente).
- Superfícies além do body: nome de canal, display name de autor visível e
  filename de anexo `Ready` — sempre com a mesma ACL de membership.
- Ranking útil: recência + boost de frase exata; preview com trecho destacado
  (`ts_headline`) quando houver termo.
- UI: um campo continua bastando; hits agrupados por tipo (mensagem / canal /
  pessoa / anexo) sem inventar um motor novo.

## Fora de escopo

- Busca semântica/vetorial / RAG → **B-121**.
- OpenSearch / cluster dedicado → **B-060**, só com gatilho ADR-016.
- Conteúdo de PDF/áudio (só transcrição já indexada, se existir).
- Painel desalinhado / fecha ao selecionar → **BUG-022**.
- Ampliar ACL: filtro e hit nunca mostram canal sem membership.

## Contratos

`GET /api/v1/search/messages` permanece o endpoint de mensagens. Estender
`q` para aceitar sintaxe websearch; documentar em `contratos.md`.

Se a UI unificar tipos, resposta ganha `kind` (`message` | `channel` | `person`
| `attachment`) **ou** endpoints irmãos no mesmo módulo Search — escolher o
menor delta; ACL idêntica. Sem `TenantId` no cliente.

Índices: GIN FTS existente + `pg_trgm` / `unaccent` só se o `EXPLAIN` do PR
justificar. Sem dual-write.

## UX

- Digitar “reuniao”, “reuni” ou `"plano Q3"` encontra o que o usuário espera.
- Vazio explica o que tentar (prefixo, aspas, `-termo`), sem jargão de FTS.
- Canal/pessoa no resultado navega; anexo abre a mensagem.
- Filtros B-098 (`de:`/`em:`/`tem:`) continuam só restringindo.

## Multi-tenant e authZ

- Membership no servidor em todo hit; `authorId`/`channelId` invisível → 403.
- Guest (B-040): escopo forçado ao canal do convite, como hoje.

## Aceite

- [ ] Prefixo e termo sem acento devolvem a mensagem canônica
- [ ] `"frase exata"` e `-excluir` recortam como websearch
- [ ] Hit de canal/pessoa/anexo nunca vaza membership
- [ ] Filtros B-098 e ordenação `relevance`/`date` intactos
- [ ] Kill switch de IA / B-121 não é requisito

## Testes

- Integration: prefixo, unaccent, aspas, `-termo`, combinação com `de:`/`em:`.
- Security: hit de canal privado sem membership e cross-tenant → vazio/403.
- Unit (web): parser da query websearch não quebra operadores B-098.

## Riscos

- `pg_trgm` + FTS em base grande → medir no PR; abortar trgm se piorar p95.
- Ranking mais solto aumenta ruído → boost de frase exata e recência.
