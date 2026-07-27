# B-117 — Inbox unificada

> Wave 11 · Trilha C/D · Deps: B-094, B-102 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

DMs, menções, threads e anúncios competem na sidebar; o usuário não tem uma fila
única para processar o que exige atenção.

## Escopo

- Inbox com menções, DMs não lidas, threads seguidas, anúncios e itens salvos.
- Filtros por tipo, workspace e estado.
- Prioridade determinística, não “score secreto”.
- Marcar lido/não lido usa o read cursor canônico.
- Snooze pessoal com data.
- Paginação e atualização realtime.

## Fora de escopo

- Caixa separada de mensagens/copiar conteúdo.
- Prioridade por IA obrigatória.
- Alterar read state de outra pessoa.

## Contratos

Query agregada/projeção reconstruível; cursor pessoal de snooze; nenhum segundo
estado de leitura. Endpoint retorna origem e ação de navegação.

## UX

Keyboard-first, contagens explicáveis, empty states e “por que está aqui”.
Abrir item leva à mensagem exata sem perder posição.

## Multi-tenant e authZ

Cada hit revalida membership e ACL. Remoção de acesso elimina item/projeção sem
mostrar título/preview.

## Aceite

- [ ] Tipos aparecem uma vez e na ordem definida.
- [ ] Ler na inbox atualiza origem e vice-versa.
- [ ] ACL revogada remove item.
- [ ] Paginação não duplica/perde item em update concorrente.
- [ ] Snooze é privado e expira.

## Testes

Projection/integration, mudanças de ACL, cursor concorrente, E2E keyboard e
reconnect.

## Riscos

Drift entre projeção e origem. Tornar projeção rebuildable, monitorar lag e
sempre resolver conteúdo pela fonte.

