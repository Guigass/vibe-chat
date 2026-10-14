# ADR-031 — Canais por tópicos reutilizam Conversation

- Status: Accepted
- Data: 2026-10-13
- Relacionado: B-118, ADR-001, ADR-010, ADR-011

## Contexto

Threads (B-022) são subconversas ancoradas numa mensagem. Canais movimentados
precisam de conversas nomeadas, descobertas e acompanhadas, sem duplicar o
agregado de mensagem nem mudar o canal normal.

## Decisão

1. `Channel.Layout` é `Stream` (default) ou `Topics`. A troca é explícita na
   criação (`layout=Topics`) e só vale para canal público ou privado. Canal
   existente permanece `Stream`.
2. `conversations.channel_topics` é metadado: título, slug local ao canal,
   estado open/closed, tags locais e participantes recentes. `Topic.Id` é o
   id de conversa usado por `messages`, `conversation_sequences` e
   `read_cursors`. Não há segundo agregado de mensagem.
3. Thread continua ancorada em mensagem. Tópico não a substitui e não cria
   hierarquia.
4. Seguir grava `messaging.topic_subscriptions`. Não lidas = seq da conversa
   do tópico menos o read cursor da mesma conversa. A inbox ganha o kind
   `topic`, sem reordenar announcement/mention/dm/thread/saved.
5. Permissões separadas: `conversation.topic.create` (membro, guest, bot,
   moderador, admin) e `conversation.topic.manage` (moderador e admin) para
   fechar, reabrir e mover. Autor pode renomear o próprio título. Fechado
   recusa post sem `topic.manage`.
6. Mover troca o `ChannelId` do tópico. Mensagens permanecem na conversa do
   tópico. A ACL passa a ser a do canal de destino. Busca resolve o canal
   pai pelo tópico, como já faz com thread.
7. Flag `Conversations:Topics:Enabled`, **off** em `appsettings.json`. Com a
   flag desligada os endpoints respondem `TopicsDisabled` (404) e não gravam.
   Development e o TestHost ligam a flag. Teto de 200 tópicos por canal.
8. Eventos duráveis `topic.created|renamed|closed|reopened|moved|followed`
   saem pelo outbox com `tenantId` e `channelId`. Audit leva ids e estado,
   sem título.

## Alternativas

- Novo agregado de mensagem por tópico: duplica seq, outbox e cursor.
- Reusar `MessageThread` com título: mistura reply ancorado e fórum.
- Tags globais: vazam vocabulário entre canais e tenants.

## Consequências

- Rollback operacional: `Conversations:Topics:Enabled=false`. O chat em modo
  Stream não muda. Rollback de schema: `Down` de
  `20261013235533_AddTopicConversationId` e depois
  `20261013234908_AddChannelTopics` (tabelas e coluna `Layout`). Mensagens
  cujo `ConversationId` é um tópico ficam órfãs e só devem ser purgadas nesse
  rollback.
- Falha de tópico (quota, fechado, flag off) não impede envio no canal Stream.
- R3: sem serviço novo, sem bus, sem dependência proprietária.

## Segurança

Ver `docs/security/modelo-ameacas.md` (B-118). Membership do canal pai é a
ACL. Preview e tags não saem em listagem nem busca para quem não acessa o
canal. Tenant vem da sessão, nunca do body.
