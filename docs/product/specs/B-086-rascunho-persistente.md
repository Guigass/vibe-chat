# B-086 — Rascunho persistente

> Wave W8-8 · Trilha D · Deps: — · Decisões: D-11 · Risco R1

## Problema

O rascunho é um signal em memória (`draft` no `composer.ts`). Trocar de canal ou
recarregar a página apaga o que foi digitado. É perda de trabalho silenciosa.

## Escopo

- Rascunho por conversa (canal, DM e thread), preservado ao trocar de conversa.
- Persistência local (`IndexedDB`, com fallback `localStorage`), por usuário e por tenant.
- Indicador de rascunho no item da sidebar.
- Restaurar também os anexos pendentes já enviados ao storage (por id, não por bytes).
- Limpar ao enviar; expirar rascunho intocado após 30 dias.

## Fora de escopo

- Sincronizar rascunho entre dispositivos (exigiria persistir conteúdo não enviado no
  servidor — reabrir só com decisão explícita, tem implicação de retenção).
- Histórico de versões do rascunho.

## Contratos

Nenhum. É inteiramente cliente. Isso é deliberado: rascunho não enviado não deve
existir no servidor sem uma decisão de retenção (D-03).

## UX

- Voltar ao canal restaura texto e posição do cursor.
- Item da sidebar mostra “Rascunho” em itálico com a cor de texto secundária.
- Debounce de 400 ms na gravação, para não escrever a cada tecla.
- Trocar de tenant/usuário no mesmo navegador não mistura rascunhos.

## Multi-tenant e authZ

Chave do registro inclui `tenantId` + `userId` + `conversationId`. Logout limpa os
rascunhos daquele usuário — máquina compartilhada não pode vazar texto entre contas.

## Aceite

- [ ] Digitar, trocar de canal, voltar → texto preservado
- [ ] F5 preserva o rascunho
- [ ] Enviar limpa o rascunho e o indicador da sidebar
- [ ] Logout apaga os rascunhos do usuário
- [ ] Dois tenants no mesmo navegador não compartilham rascunho
- [ ] Sem IndexedDB, o fallback funciona

## Testes

- Unit (web): gravação com debounce, chave composta, expiração, limpeza no logout.
- E2E: digitar, recarregar, conferir o texto.

## Riscos

- Rascunho sensível em máquina compartilhada → limpeza no logout é obrigatória, não
  otimização.
- Cota de storage estourada → limitar tamanho por rascunho e podar os mais antigos.
