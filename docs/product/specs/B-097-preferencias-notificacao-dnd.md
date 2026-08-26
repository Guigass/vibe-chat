# B-097 — Preferências de notificação e não perturbe

> Wave W10-3 · Trilha B/D · Deps: B-095 · Decisões: D-13, D-10 · Risco R2

## Problema

Com B-095, todo mundo passa a receber push do mesmo jeito. Sem controle por canal e sem
horário de silêncio, notificação boa vira ruído e a pessoa desliga tudo.

## Escopo

- Preferência global: todas as mensagens / só menções e DMs / nenhuma.
- Sobrescrita por canal, com a opção “usar o padrão”.
- Silenciar canal por 1 h, 8 h, até amanhã, ou indefinidamente.
- Não perturbe com janela de horário e dias da semana, no fuso do usuário.
- Exceção: DM de quem for marcado como prioritário fura o DND (opt-in).
- Resumo por e-mail do que foi perdido durante o DND, se SMTP estiver ligado (D-10).
- Prévia da mensagem ocultável na notificação.

## Fora de escopo

- Palavras-chave que notificam; agenda importada de calendário; escalonamento.

## Contratos

`notifications.user_preferences`:

| Coluna | Tipo |
|--------|------|
| `TenantId`, `UserId` | uuid |
| `Level` | enum `All`\|`MentionsAndDms`\|`None` (default `MentionsAndDms`) |
| `HidePreview` | bool |
| `DndEnabled` | bool |
| `DndStart`, `DndEnd` | time |
| `DndDays` | smallint (bitmask) |
| `TimeZone` | text (IANA) |
| `DigestEnabled` | bool |

`notifications.channel_preferences` (`TenantId`, `UserId`, `ChannelId`, `Level`,
`MutedUntil`).

- `GET` / `PUT /api/v1/notifications/preferences`
- `PUT /api/v1/notifications/preferences/channels/{channelId}`
- O worker de B-095 consulta essas tabelas antes de enviar; DND **suprime** o push,
  não a mensagem.

`contratos.md`: tabelas e endpoints.

## UX

- Página de Configurações → Notificações, com o efeito de cada opção em uma linha.
- Menu do canal com “Silenciar” e as durações.
- Canal silenciado fica com o nome esmaecido e ícone de sino cortado na sidebar.
- Indicador de DND ativo no cabeçalho, com “desativar agora”.
- Fuso detectado do navegador na primeira vez, editável depois.

## Multi-tenant e authZ

Preferência é por (`tenant`, `user`); ninguém lê nem escreve a do outro, incluindo
admin — a auditoria não cobre preferência pessoal. Canal de outro tenant no
`PUT` → 403.

## Aceite

- [x] Global “só menções” impede push de mensagem comum
- [x] Canal em “todas” fura a preferência global
- [x] Silenciar por 1 h volta sozinho
- [x] DND das 20h às 8h suprime push nesse intervalo, no fuso certo
- [x] Contato prioritário fura o DND
- [x] Ocultar prévia tira o texto da notificação
- [x] Ler preferência de outro usuário → 403

## Nota de implementação

O endpoint de preferências não tem parâmetro de usuário-alvo — ele sempre resolve
para o próprio ator — então "ler preferência de outro usuário" é estruturalmente
impossível em vez de checado em runtime; `tests/security` cobre o mesmo risco
provando que a escrita de um usuário nunca aparece na leitura de outro.

`DigestEnabled` é persistido (coluna + toggle na UI de preferências), mas o envio
do resumo por e-mail do que foi perdido durante o DND **não** foi implementado
nesta entrega — exigiria fila de mensagens suprimidas + worker periódico + template
de e-mail, sem critério de aceite nem teste definidos aqui. Fica como item futuro
quando houver demanda.

## Testes

- Unit (worker): matriz de decisão (global × canal × DND × prioridade × cursor).
- Integration: CRUD; expiração de `MutedUntil`; cálculo de DND com fuso.
- Security: preferência cross-usuário e cross-tenant → 403.

## Riscos

- Matriz de decisão silenciando o que não devia → tabela-verdade explícita em teste
  unitário, um caso por combinação.
- Fuso e horário de verão → guardar IANA e calcular na hora, nunca offset fixo.
