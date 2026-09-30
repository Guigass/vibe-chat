# ADR-025: Política de edição e exclusão de mensagens

## Status: Accepted

## Contexto

B-107 / W10-11 pede limitar quem edita ou apaga mensagem e por quanto tempo,
por workspace, sem inventar grupos novos. A spec é R3 (authZ). O pacote
`docs/architecture/pacotes-decisao-r3.md` exige ADR, threat model, rollback e
caso negativo. A seção de compliance daquele pacote cobre B-128…B-134; daqui
sai só a regra comum: a política não enfraquece o soft-delete da ADR-018 nem
antecipa o snapshot de body da B-169.

Não há decisão D-* nova. D-03 / ADR-018 continuam: apagar é soft-delete.

## Decisão

1. **Uma linha por tenant** em `messaging.message_lifecycle_policies`, com RLS
   FORCE. Sem linha, vale o default que reproduz o comportamento atual.
2. **Autor** precisa da permissão `*.own`, do papel na allow-list (quando
   restrita) e da janela (`createdAt` + minutos, `null` = sem limite).
3. **`message.edit.any`** entra no catálogo de Admin e Moderator. Só vale com
   `messaging.edit.allowModeratorOverride` (default **false**). Esse é o flag
   da capacidade nova. Não há kill switch que pule a checagem no servidor —
   controle de authZ não recebe bypass.
4. **Delete de outros** continua disponível: `message.delete.any` +
   `allowModeratorOverride` default **true**. Desligar o override volta o
   apagar para o autor, dentro da janela e dos papéis.
5. **Papéis configuráveis:** Member, Moderator, Admin, Auditor, Guest.
   WorkspaceOwner e PlatformOwner seguem Admin na allow-list. Lista vazia é
   válida (ninguém na lista). `windowMinutes` menor que 1 ou maior que 525600
   é rejeitado.
6. **Leitura da política efetiva** em `GET /channels/{id}/messaging-policy`
   (`message.read`, inclusive guest do canal). Escrita só em
   `PUT /admin/settings` (`workspace.admin`).
7. **Body permanece na linha** após soft-delete, para o snapshot de B-169.
   Este item não grava body no audit.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Flag de processo que desliga a política | Bypass de authZ; o default já é o rollback |
| Política por canal | Fora da spec desta fatia |
| `windowMinutes=0` como “já expirou” | Ambíguo; o PUT rejeita |
| Override também na própria mensagem | A spec limita o override a mensagens de outros |

## Rollback

1. Apagar a linha do tenant (ou repor enabled=true, janela nula, roles sem
   restrição, edit override false, delete override true). O código sem linha
   usa esse mesmo default — coberto por teste de unidade.
2. Reverter a migration em lab. Em dados reais, preferir o passo 1: a tabela
   é aditiva e a ausência da linha é o estado anterior.

## Consequências

- **+** Janela, papel e moderação decididos no servidor; a UI só esconde a ação
- **+** Membro não lê `/admin/settings`; lê só a política não secreta do canal
- **−** Cada save do admin que envia `messaging.edit.roles` passa a restringir
  a lista explícita (o formulário manda Member/Moderator/Admin por default)
- **−** Revisão profunda de segurança fica para a rodada humana final
  (perfil econômico pré-release)
