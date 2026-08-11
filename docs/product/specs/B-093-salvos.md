# B-093 — Salvos

> Wave W9-6 · Trilha C/D · Deps: B-089 · Decisões: D-11 · Risco R2

## Problema

Fixar (B-092) é coletivo e do canal. Falta o equivalente pessoal: “volto nisso depois”,
sem poluir o canal para os outros.

## Escopo

- Salvar/remover qualquer mensagem que o usuário possa ler.
- Vista “Salvos” na sidebar, com todas as conversas misturadas, mais recentes primeiro.
- Nota pessoal opcional (até 280 caracteres) em cada item.
- Marcar como concluído: sai da lista principal, fica no filtro “concluídos”.
- “Ir até a mensagem” abre a conversa na posição (B-089).
- Contador de salvos pendentes na sidebar.

## Fora de escopo

- Lembrete com horário (exigiria agendador e notificação — reabrir junto de B-097).
- Compartilhar lista de salvos; pastas/etiquetas.

## Contratos

Tabela nova `messaging.saved_messages`:

| Coluna | Tipo |
|--------|------|
| `TenantId` | uuid, NOT NULL, RLS |
| `UserId`, `MessageId`, `ChannelId` | uuid |
| `Note` | text? |
| `CompletedAt` | timestamptz? |
| `CreatedAt` | timestamptz |

Único por (`TenantId`, `UserId`, `MessageId`).

- `POST /api/v1/workspaces/{workspaceId}/saved` — `{ messageId, note? }`
- `PATCH /api/v1/workspaces/{workspaceId}/saved/{messageId}` — nota / concluído
- `DELETE /api/v1/workspaces/{workspaceId}/saved/{messageId}`
- `GET /api/v1/workspaces/{workspaceId}/saved?completed=&limit=&cursor=`

Sem evento de hub: é estado pessoal, não de conversa.

`contratos.md`: tabela e quatro endpoints.

## UX

- Ação “Salvar” no menu da bolha; marcador discreto quando salva.
- Vista “Salvos” com origem (canal/DM), autor, trecho, nota e data.
- Salvar mensagem que depois é apagada: item permanece mostrando “Mensagem removida”,
  com opção de remover o salvo.
- Estado vazio explicando para que serve, sem cartão desnecessário.

## Multi-tenant e authZ

- Só se salva mensagem legível **no momento de salvar**; a leitura da lista revalida
  membership e omite o que o usuário perdeu acesso.
- Perder membership do canal esconde o item da lista (não apaga a linha) — é o
  comportamento seguro.
- Nenhum admin lê salvos de terceiros; auditoria de conversa (B-067) não inclui salvos.

## Aceite

- [x] Salvar aparece na vista Salvos
- [x] Nota persiste e é editável
- [x] Concluir tira da lista principal e mantém no filtro
- [x] “Ir até” abre a conversa na mensagem
- [x] Salvar duas vezes não duplica
- [x] Sair do canal esconde o salvo daquele canal
- [x] Salvar mensagem de outro tenant → 403

## Testes

- Integration: CRUD, unicidade, filtro de concluídos, paginação por cursor.
- Security: salvar cross-tenant → 403; item de canal sem membership não aparece.
- Unit (web): contador, estado vazio, item com mensagem removida.

## Riscos

- Lista crescendo sem limite → paginação por cursor e poda opcional de concluídos
  antigos.
- Vazamento por membership revogada → revalidação na leitura, não só na escrita.
