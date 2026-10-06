# ADR-029: Histórico de edição e movimentação de mensagens

## Status: Accepted

## Contexto

B-114 / W11 pede versionar o body a cada edição, mover mensagem ou thread
entre destinos autorizados sem reescrever o `seq` de origem, e incluir as
versões no export. A spec é R3 (authZ, RLS, dado de conteúdo). O pacote
`docs/architecture/pacotes-decisao-r3.md` não tem seção própria para este
item; vale a regra comum (flag off, ADR, threat model, rollback). Não há
decisão D-* nova. ADR-018 e ADR-025 continuam: apagar é soft-delete e a
política de quem pode editar não muda.

Legal hold (B-129) ainda não existe. As versões sobrevivem ao soft-delete e
entram no export para que o hold futuro as enxergue. O purge de retenção
apaga versões e o registro de move junto com a mensagem.

## Decisão

1. **Flag de processo** `Messaging:History:Enabled`, default **false**
   (`appsettings.json`). Development e o TestHost ligam, como as outras flags
   R3 de lab. Off: edição segue sem gravar versão; `GET/POST …/history` e
   `…/move` respondem **404** `HistoryDisabled`.
2. **Com a flag ligada, toda edição grava** o body anterior em
   `messaging.message_versions` (RLS FORCE), numerado a partir de 1. O body
   vivo continua em `messaging.messages`.
3. **Exibir** o histórico exige `message.history.read` (Member, Moderator,
   Admin, Auditor — não Guest/Bot) e `messaging.history.enabled` no workspace
   (default true quando não há linha). Admin com `workspace.admin` lê mesmo
   com a política oculta; essa leitura gera audit `message.history.read` sem
   body. Membro recebe **403** `HistoryHidden`.
4. **Move** exige `message.move` (Moderator, Admin e papéis equivalentes) e
   membership nos dois canais do mesmo workspace. O destino ganha mensagem
   nova, `seq` novo e `MovedFrom*`. A origem guarda `MovedTo*` e, por default,
   vira tombstone `<system:moved>` sem id de destino no corpo. `leaveTombstone`
   false faz soft-delete na origem. O `seq` da origem não muda.
5. **Thread:** `scope=message` numa raiz com respostas → **409**
   `MessageHasThread`. `scope=thread` copia raiz e respostas para uma thread
   nova no destino. Enquete, anúncio e corpo de sistema não movem.
6. **Idempotência** por `(tenant, idempotencyKey)`. A mesma origem não move
   duas vezes (`AlreadyMoved`). Outro tenant não resolve o canal → **403** e
   nada é gravado.
7. **Quem não é membro do destino** recebe só o aviso neutro. Ids e `seq` do
   destino não saem no histórico HTTP nem no evento do canal de origem.
8. **Realtime:** `MessageMoved` no canal de destino (sem webhook nem push) e
   `MessageEdited`/`MessageDeleted` na origem. Anexos prontos seguem por
   referência de `StorageKey`.
9. **Export** inclui `message-versions.json`. Purge remove versões e moves.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Reescrever a linha no destino | Viola a ordem `seq` do canal de destino |
| Colocar o id de destino no corpo do tombstone | Vaza para quem não tem ACL |
| `MessageCreated` no destino | Dispararia push e webhook de conteúdo já visto |
| Versão só quando a política de exibição está ligada | Esconderia a trilha de compliance |

## Rollback

1. `Messaging:History:Enabled=false`. Edições voltam ao comportamento anterior;
   versões já gravadas permanecem e só reaparecem se a flag voltar.
2. A migration é aditiva. Em lab, reverter a migration. Em dados reais, preferir
   o passo 1.

## Consequências

- **+** Edição e move deixam evidência no mesmo tenant, com RLS
- **+** Tombstone não entrega o destino a quem não é membro
- **−** Cada edição com a flag ligada grava mais uma linha
- **−** Hold que suspenda o purge fica para B-129
- **−** Revisão profunda de segurança fica para a rodada humana final
