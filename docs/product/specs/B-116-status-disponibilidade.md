# B-116 — Status personalizado e disponibilidade

> Wave 11 · Trilha B/D · Deps: B-097 · Risco R1
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Presence online/away não comunica férias, foco, reunião ou quando a pessoa volta.

## Escopo

- Status com emoji, texto curto e expiração.
- Disponibilidade derivada de presence, DND e status.
- Opção “limpar ao fim do dia”.
- Exibição em avatar, perfil, DM e composer de menção.
- API para integração futura de calendário, desativada por default.

## Fora de escopo

- Publicar detalhes de agenda.
- Inferir produtividade.
- Sincronizar calendário nesta fatia.

## Contratos

Status persistente por usuário/tenant; evento realtime agregado sem dado de
agenda; limites de tamanho e allowlist de estado.

## UX

Editor rápido, preview e expiração acessível. Status longo é truncado visualmente
sem perder texto para screen reader.

## Multi-tenant e authZ

Visível apenas para membros autorizados do mesmo workspace. Usuário altera o
próprio; admin pode limpar abuso com audit.

## Aceite

- [ ] Expiração automática funciona.
- [ ] DND prevalece para notificação.
- [ ] Status não aparece a outsider.
- [ ] Reconnect recupera estado.

## Testes

Clock fake, realtime, visibility e E2E de set/clear/expire.

## Riscos

PII em status e spam. Limitar tamanho, oferecer denúncia/clear admin e documentar
que o usuário controla o conteúdo.

