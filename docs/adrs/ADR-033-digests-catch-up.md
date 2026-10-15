# ADR-033 — Digests e catch-up pessoal

- Status: Accepted
- Data: 2026-10-15
- Relacionado: B-122, B-117, B-097, B-121, ADR-009, ADR-010, ADR-012

## Contexto

Depois de uma ausência, o usuário precisa de uma lista determinística do que
mudou, sem uma segunda cópia do body e sem marcar as fontes como lidas. O
resumo por IA só entra quando a busca semântica (B-121) está ligada no
workspace.

## Decisão

1. Agenda pessoal em `notifications.digest_schedules` (uma linha por usuário).
   O catch-up sob demanda e o job diário/semanal gravam `digest_runs` +
   `digest_items` só com ids, canal, seq e instante. Não há coluna de body.
2. A seleção reutiliza a ACL da inbox (canal público para membro do workspace,
   demais canais só com membership atual) e acrescenta não lidas, decisões e
   tarefas abertas visíveis. Na leitura, membership, mensagem apagada e
   compromisso ausente derrubam o item.
3. Flag `Notifications:Digests:Enabled`, **off** em `appsettings.json`.
   Desligada, os endpoints respondem `DigestDisabled` (404) e o worker não
   varre. Development e o TestHost ligam a flag.
4. Chave de período `daily:yyyy-MM-dd`, `weekly:yyyy-Www` ou
   `ondemand:{Idempotency-Key}`. Índice único
   `(TenantId, UserId, WorkspaceId, PeriodKey)`. A segunda geração devolve a
   mesma linha e não incrementa `DeliveryCount`.
5. Entrega externa (`email` ou `push`) leva só contagens e o título
   localizado. Sem body, sem nome de canal, sem id de mensagem. DND adia o
   envio (`NextRetryAt`) e mantém a lista. SMTP/push desligados não viram
   falha: a página continua sendo a entrega.
6. IA opcional chama o provider só com contagens, e só se
   `SemanticSearch:Enabled` e o workspace optaram. Falha deixa
   `AiStatus=failed` e a lista intacta (`Status=partial`). Até 3 tentativas.
7. `digest.generated` sai em audit e outbox sem texto de mensagem. A métrica
   `vibechat.digests.generated` não leva conteúdo. O worker usa
   `app.job_role=digest` só para reivindicar agendas; a seleção roda com
   tenant e usuário.
8. O cursor de leitura não é atualizado.

## Alternativas

- Copiar o body para o e-mail: vira canal de exfiltração. Fora de escopo.
- Reusar só `DigestEnabled` sem tabela de run: não há idempotência nem
  snapshot de ids.
- Gerar o resumo no hot path de `SendMessage`: contradiz ADR-012.

## Consequências

- Rollback operacional: `Notifications:Digests:Enabled=false`. O chat não muda.
  Runs já gravados permanecem até a migration `Down`.
- Rollback de schema: `Down` remove `digest_schedules`, `digest_runs` e
  `digest_items`. Mensagens, cursores e a inbox não mudam.
- Capacidade: lote de 20 agendas por ciclo de 15 s; no máximo 30 itens por
  seção. Não é fan-out de canal.
- R3: sem serviço novo, sem bus, sem copiar conteúdo para o provider.

## Segurança

Ver `docs/security/modelo-ameacas.md` (B-122). Tenant vem da sessão. Outro
usuário recebe 404. Workspace de outro tenant recebe 403.
