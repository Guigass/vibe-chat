# B-097 — Preferências de notificação e não perturbe

> Wave W10-3 · Trilha B/D · Deps: B-095 · Decisões: D-13, D-10

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

- [ ] Global “só menções” impede push de mensagem comum
- [ ] Canal em “todas” fura a preferência global
- [ ] Silenciar por 1 h volta sozinho
- [ ] DND das 20h às 8h suprime push nesse intervalo, no fuso certo
- [ ] Contato prioritário fura o DND
- [ ] Ocultar prévia tira o texto da notificação
- [ ] Ler preferência de outro usuário → 403

## Testes

- Unit (worker): matriz de decisão (global × canal × DND × prioridade × cursor).
- Integration: CRUD; expiração de `MutedUntil`; cálculo de DND com fuso.
- Security: preferência cross-usuário e cross-tenant → 403.

## Riscos

- Matriz de decisão silenciando o que não devia → tabela-verdade explícita em teste
  unitário, um caso por combinação.
- Fuso e horário de verão → guardar IANA e calcular na hora, nunca offset fixo.
