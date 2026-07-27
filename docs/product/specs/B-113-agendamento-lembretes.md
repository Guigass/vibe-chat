# B-113 — Agendamento de mensagem e lembretes

> Wave 11 · Trilha C/D · Deps: W9-7, B-093 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Usuários não conseguem preparar uma mensagem para depois nem criar lembrete
pessoal ligado a uma conversa.

## Escopo

- Agendar envio com timezone IANA e instante UTC canônico.
- Editar/cancelar antes do disparo.
- Lembrete pessoal sobre mensagem, thread ou horário.
- Worker durável com claim, retry e idempotência.
- Lista pessoal de agendados/lembretes.
- Notificação in-app e Web Push conforme preferências.

## Fora de escopo

- Recorrência complexa.
- Agendamento por bot sem capability.
- Garantia de segundo exato durante indisponibilidade.

## Contratos

Recursos `scheduled_messages` e `reminders`; endpoints CRUD; outbox
`scheduled_message.due`/`reminder.due`. No disparo, usa o comando canônico de
mensagem com idempotency key estável.

## UX

Composer oferece “Enviar agora/Agendar”. Mostra timezone, atraso e falha.
Rascunho não some até confirmação do agendamento.

## Multi-tenant e authZ

AuthZ é revalidada no disparo. Perda de membership cancela envio e notifica o
autor sem revelar conversa.

## Aceite

- [ ] Horário é consistente entre fusos.
- [ ] Retry não duplica mensagem.
- [ ] Cancelamento antes do claim impede envio.
- [ ] Membership revogada impede disparo.
- [ ] Lembrete é privado.

## Testes

Clock fake; corrida cancel/claim; retry do worker; security cross-tenant; E2E de
agendar, editar e cancelar.

## Riscos

Clock drift, DST e duplicação. Persistir UTC + timezone original e usar claim
transacional.

