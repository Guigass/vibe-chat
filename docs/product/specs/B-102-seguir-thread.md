# B-102 — Seguir thread

> Wave W10-8 · Trilha C/D · Deps: B-022, B-095 · Decisões: D-11 · Risco R2

## Problema

Threads existem, mas não há como acompanhar uma sem voltar ao canal e reabrir. Foi
exatamente esse gesto — “follow thread” mais uma visão agregada — que o Teams adotou
ao tornar threads o layout padrão de canal em 2026.

## Escopo

- Seguir/deixar de seguir thread, manual pelo menu.
- Seguir automático quando o usuário é autor da mensagem raiz, responde na thread ou é
  mencionado nela.
- Vista “Threads seguidas” na sidebar, ordenada por atividade recente, com contagem de
  não lidas por thread.
- Notificação de resposta em thread seguida respeita B-095/B-097.
- “Seguir todas as threads” por canal.
- Compartilhar uma resposta da thread de volta no canal (referência, não cópia).

## Fora de escopo

- Layout de canal em modo thread (mudança grande de UI — item próprio se houver demanda).
- Resolver/arquivar thread.

## Contratos

`messaging.thread_subscriptions`:

| Coluna | Tipo |
|--------|------|
| `TenantId`, `UserId`, `ThreadId`, `ChannelId` | uuid |
| `Source` | enum `Manual`\|`Author`\|`Reply`\|`Mention` |
| `LastReadSeq` | bigint |
| `CreatedAt` | timestamptz |

- `POST` / `DELETE .../threads/{threadId}/subscription`
- `GET /api/v1/workspaces/{workspaceId}/threads/following?cursor=&limit=`
- `POST .../threads/{threadId}/messages/{messageId}/share-to-channel` — publica no
  canal uma mensagem referenciando a resposta (usa o `replyTo` de B-084)
- Preferência de canal `followAllThreads` em `notifications.channel_preferences` (B-097)
- Assinatura automática criada na **mesma transação** da resposta/menção.

`contratos.md`: tabela, endpoints, campo de preferência.

## UX

- “Seguindo” no cabeçalho do painel de thread, com estado alternável.
- Vista de threads seguidas: canal de origem, trecho da raiz, últimos participantes,
  contagem de não lidas.
- Auto-seguir avisa na primeira vez (“você está seguindo esta thread”), com
  “desfazer” — ninguém gosta de ser inscrito sem saber.
- Compartilhar no canal mostra prévia da resposta com link para a thread.

## Multi-tenant e authZ

- Assinatura exige membership no canal da thread; perder membership esconde a thread
  da vista e suprime a notificação.
- Assinatura cross-tenant → 403.
- Compartilhar no canal exige permissão de postar no canal; conteúdo compartilhado
  respeita a visibilidade do canal de origem (mesmo canal, então não há elevação).

## Aceite

- [ ] Responder numa thread cria a assinatura automática
- [ ] Menção na thread também cria
- [ ] Vista lista as seguidas com não lidas corretas
- [ ] Deixar de seguir remove da vista e para de notificar
- [ ] “Seguir todas” no canal inscreve nas threads novas
- [ ] Compartilhar publica a referência no canal
- [ ] Sair do canal esconde as threads dele
- [ ] Assinar thread de outro tenant → 403

## Testes

- Integration: auto-seguir por autor/resposta/menção na mesma transação; contagem de
  não lidas; `followAllThreads`.
- Security: cross-tenant → 403; thread de canal sem membership não aparece.
- E2E: Alice responde, Bob (mencionado) vê a thread na vista de seguidas.

## Riscos

- Auto-seguir gerando ruído → aviso com desfazer e respeito às preferências de B-097.
- Contagem de não lidas por thread ficando cara → contador incremental via outbox, não
  agregação por leitura.
