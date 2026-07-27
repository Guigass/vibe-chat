# B-095 — Web Push

> Wave W10-1 · Trilha B/C/D · Deps: B-082, B-094 · Decisões: D-13, D-04

## Problema

Não existe notificação nenhuma. Sem a aba aberta, a pessoa não fica sabendo de nada —
o que basicamente obriga a deixar o VibeChat aberto o dia inteiro. Não há uso de
`Notification` no `src` e o service worker (`ngsw`) só faz cache de asset.

## Escopo

- Web Push padrão com VAPID, servido pela própria instância (D-13).
- Assinatura por dispositivo, opt-in, pedida **depois** de uma ação do usuário.
- Notificação do navegador com a aba em foco em outro canal (in-app, sem push).
- Push só quando: menção direta, DM, ou canal marcado como “todas as mensagens”.
- Nada de push para mensagem já lida em outro dispositivo (checa o cursor
  **persistente** de B-094 — `messaging.read_cursors`).
- Clicar na notificação abre a conversa na mensagem.
- Desativar por dispositivo e revogar todas as assinaturas.

## Fora de escopo

- FCM/APNs e app nativo — barrado por D-13.
- Agrupamento avançado por canal no centro de notificações do SO.
- E-mail de resumo — é B-097.

## Contratos

Tabela nova `notifications.push_subscriptions`:

| Coluna | Tipo |
|--------|------|
| `TenantId` | uuid, NOT NULL, RLS |
| `UserId` | uuid |
| `Endpoint` | text (único por usuário) |
| `P256dh`, `Auth` | text (chaves do cliente) |
| `UserAgent` | text? |
| `CreatedAt`, `LastSeenAt`, `FailedAt` | timestamptz |

- `POST /api/v1/notifications/push/subscriptions` — registra
- `DELETE /api/v1/notifications/push/subscriptions/{id}` — remove
- `GET /api/v1/notifications/push/public-key` — chave pública VAPID
- Envio pelo **worker**, consumindo o outbox de `MessageCreated` — nunca no hot path.
- `Push:Enabled` (default `false`), `Push:Vapid:PublicKey`, `Push:Vapid:PrivateKey`,
  `Push:Vapid:Subject` em env (`.env.example` com placeholders).
- Assinatura que devolve `404`/`410` é removida automaticamente.

`contratos.md`: tabela, três endpoints, flags e o caminho pelo outbox.

## UX

- Banner de opt-in só depois da primeira mensagem enviada, com “agora não” persistente.
- Notificação mostra remetente, canal e prévia curta; **payload mínimo** (D-13).
- Preferência por dispositivo em Configurações, com “remover este dispositivo”.
- Permissão negada: a UI explica como reverter no navegador em vez de insistir.

## Multi-tenant e authZ

- Assinatura pertence a (`tenant`, `user`); apagar membership apaga as assinaturas.
- O worker resolve destinatários **pela membership no momento do envio**, não pela
  lista gravada — quem saiu do canal não recebe push do canal.
- Prévia respeita a visibilidade: mensagem de canal privado só vai para membros.
- Chaves VAPID são secret (D-04): env, nunca em log, nunca no repo.
- `modelo-ameacas.md` ganha a entrada de vazamento por prévia em tela bloqueada.

## Aceite

- [ ] Opt-in registra assinatura e o teste chega no dispositivo
- [ ] Menção em canal gera push; mensagem comum em canal “só menções” não gera
- [ ] Ler no desktop impede o push no celular
- [ ] Clicar abre a conversa na mensagem certa
- [ ] Remover dispositivo para os pushes dele
- [ ] Endpoint expirado (410) é removido sozinho
- [ ] `Push:Enabled=false` desliga tudo, sem erro no cliente
- [ ] Push nunca vaza conteúdo de canal do qual o usuário saiu

## Testes

- Unit (worker): montagem do payload, seleção de destinatários, poda no 410.
- Integration: outbox → worker → assinatura; supressão por cursor de leitura.
- Security: registrar assinatura para outro usuário → 403; push cross-tenant → nunca.
- E2E: opt-in e recebimento com service worker em modo de teste.

## Riscos

- Prévia sensível em tela bloqueada → payload mínimo + opção “ocultar prévia”.
- Fila de push travando o worker → envio em lote com timeout e retry limitado.
- Chave VAPID vazada → rotação documentada no runbook.
