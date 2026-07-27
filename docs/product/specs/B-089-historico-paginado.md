# B-089 — Histórico paginado e pular para a mensagem

> Wave W9-2 · Trilha C/D · Deps: B-088 · Decisões: D-11 · Risco R2

## Problema

O cliente busca uma janela fixa (`getMessages` com `limit=50`, `after=0`) e não tem
como carregar mais nada. Mensagem de duas semanas atrás é inalcançável pela UI. A busca
encontra a mensagem, mas clicar no resultado só troca de canal — não leva até ela
(`shell.page.ts`).

## Escopo

- Carregar página anterior ao chegar perto do topo, preservando a posição visual.
- Carregar página posterior quando o usuário entra pelo meio do histórico.
- `GET .../messages?before=<seq>&limit=` para paginar para trás.
- Pular para uma mensagem específica por `seq`: carrega a janela ao redor, rola até ela
  e destaca por ~2 s.
- Resultado de busca e clique em citação (B-084) usam o mesmo caminho de “pular para”.
- Indicador “Início da conversa” quando não há mais história.

## Fora de escopo

- Busca dentro do canal com navegação anterior/próximo — é B-098.
- Arquivamento e limite de retenção de leitura (B-047 cuida do purge).

## Contratos

`GET /api/v1/workspaces/{workspaceId}/channels/{channelId}/messages`

| Param | Nota |
|-------|------|
| `after` | já existe — página para frente |
| `before` | **novo** — `seq` exclusivo, página para trás |
| `around` | **novo** — centraliza uma janela em torno de um `seq` |
| `limit` | já existe; máximo 100 |

`after`, `before` e `around` são mutuamente exclusivos; enviar mais de um → 400.
Resposta ganha `hasMoreBefore` e `hasMoreAfter`.

`contratos.md`: novos parâmetros e campos de resposta.

## UX

- Skeleton no topo enquanto carrega a página anterior; scroll ancorado para não pular.
- “Pular para” destaca a bolha com um flash sutil (respeitando `prefers-reduced-motion`).
- Se a mensagem foi apagada, avisa em vez de rolar para lugar nenhum.
- Erro de paginação mostra “tentar novamente” em vez de página em branco.

## Multi-tenant e authZ

`before`/`around` passam pelo mesmo filtro de membership e RLS do `after`. Um `seq`
de outro canal ou tenant devolve vazio, nunca conteúdo — e há teste negativo.

## Aceite

- [ ] Rolar ao topo carrega 50 mensagens mais antigas sem pular a posição
- [ ] “Início da conversa” aparece quando acaba a história
- [ ] Clicar num resultado de busca abre o canal **e** rola até a mensagem destacada
- [ ] `around` devolve mensagens antes e depois do `seq` pedido
- [ ] `after` + `before`, `around` + `after` ou `around` + `before` → 400
- [ ] Gap-fill do reconnect continua funcionando

## Testes

- Integration: `before` e `around` com limites e bordas (primeira e última página).
- Security: `around` com `seq` de canal alheio → vazio; cross-tenant → 403.
- Unit (web): ancoragem de scroll; deduplicação ao juntar páginas.
- E2E: buscar termo antigo, clicar e chegar na mensagem.

## Riscos

- Salto de scroll → medir `scrollHeight` antes e depois e compensar.
- Mensagens duplicadas ao juntar páginas com o realtime → chave por `messageId` e
  ordenação estável por `seq`.
