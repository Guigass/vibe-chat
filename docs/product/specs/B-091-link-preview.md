# B-091 — Link preview

> Wave W9-4 · Trilha C/D · Deps: — · Decisões: D-11 · Risco R3

## Problema

URL na mensagem é texto. Quem lê não sabe para onde vai sem clicar — e num chat de
trabalho, metade das mensagens é link.

## Escopo

- O **worker** busca metadados Open Graph / oEmbed da primeira URL da mensagem.
- Cartão com título, descrição curta, domínio e imagem (via miniatura própria, nunca
  hotlink).
- Cache por URL e por tenant, com TTL de 7 dias.
- Autor pode remover o preview da sua mensagem.
- Preview desligável por workspace na configuração de admin.
- Sem metadados ou timeout: nenhum cartão, só o link como texto.

## Fora de escopo

- Embed rico (player de vídeo, tweet interativo).
- Preview de link privado atrás de autenticação.
- Unfurl por integração de app — fora por D-11.

## Contratos

Tabela nova `messaging.link_previews`:

| Coluna | Tipo |
|--------|------|
| `TenantId` | uuid, NOT NULL, RLS |
| `UrlHash` | text (SHA-256 da URL normalizada) |
| `Url`, `Title`, `Description`, `SiteName` | text |
| `ImageKey` | text? (miniatura no MinIO) |
| `FetchedAt`, `ExpiresAt` | timestamptz |
| `Status` | enum `Pending`\|`Ready`\|`Failed`\|`Blocked` |

`messaging.message_link_previews` liga mensagem ↔ preview.

- Job do worker disparado por evento de outbox de `MessageCreated`. **Nunca** síncrono
  no envio.
- `DELETE .../messages/{messageId}/link-preview` — só o autor ou `workspace.admin`.
- `LinkPreview:Enabled` (default `true`) e `LinkPreview:TimeoutMs` (default `8000`).

`contratos.md`: tabelas, endpoint, flags e o job.

## UX

- Cartão discreto abaixo do texto, com borda e fundo `--color-surface-2`; nunca maior
  que a bolha.
- Aparece quando fica pronto, sem pular o scroll de quem está lendo (altura reservada).
- Ação “remover preview” no menu da própria mensagem.
- Imagem do cartão tem `alt` com o título; o cartão inteiro é um link com nome acessível.

## Multi-tenant e authZ

Este é o item de **maior superfície de risco** da wave — o servidor passa a fazer
requisição para URL fornecida pelo usuário (SSRF). Controles obrigatórios:

- Só `http`/`https`; qualquer outro esquema → `Blocked`.
- Resolver o DNS e **recusar** IP privado, loopback, link-local e metadata
  (`169.254.169.254`), inclusive após cada redirecionamento.
- Máximo 3 redirecionamentos; timeout de 8 s; HTML até 1,5 MB; imagem até 512 KB.
- Sem cookie, sem credencial, sem header de autenticação; `User-Agent` próprio.
- Cache é **por tenant** — preview de um tenant nunca serve outro.
- `modelo-ameacas.md` ganha a entrada de SSRF com esses controles.

## Aceite

- [x] Link público vira cartão em poucos segundos, sem travar o envio
- [x] `http://169.254.169.254/...` → `Blocked`, sem requisição
- [x] Redirecionamento para IP privado é barrado
- [x] Site sem Open Graph não gera cartão e não gera erro
- [x] Autor consegue remover o preview
- [x] `LinkPreview:Enabled=false` desliga tudo
- [x] Mesma URL em dois tenants gera duas entradas de cache

## Testes

- Unit (worker): normalização de URL, parser de OG, allowlist de esquema, guarda de IP.
- Security: bateria de SSRF (IP privado, DNS rebinding, redirect chain, `file://`).
- Integration: outbox dispara o job; preview aparece no history.
- Security: remover preview de mensagem alheia → 403.

## Riscos

- **SSRF** é o risco central; sem a guarda de IP este item não passa em review.
- Fila de fetch travando o worker → timeout curto e concorrência limitada.
- Vazamento de conteúdo interno via preview de URL interna → guarda de IP resolve.
