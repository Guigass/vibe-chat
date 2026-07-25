# B-088 — Agrupamento, separadores e não lidas na timeline

> Wave W9-1 · Trilha D · Deps: — · Decisões: D-11

## Problema

A timeline é uma pilha uniforme de bolhas: uma por mensagem, com o mesmo espaçamento,
sem separador de data e sem marca de onde a leitura parou
(`apps/web/src/app/features/chat/timeline/timeline.ts`). Em canal movimentado é difícil
saber o que é novo e onde um dia termina.

## Escopo

- Agrupar mensagens consecutivas do mesmo autor dentro de **5 minutos**: cabeçalho só na
  primeira; as seguintes mostram a hora no hover.
- Separador de data sticky (“Hoje”, “Ontem”, “12 de março”).
- Divisor “Novas mensagens” na posição do cursor de leitura ao abrir a conversa; some
  quando o usuário rola além dele.
- Botão flutuante “Ir para a mais recente” com contagem de novas, visível quando o
  usuário está longe do fim.
- Auto-scroll só quando já está no fim; quem está lendo histórico não é arrastado.

## Fora de escopo

- Virtualização da lista — só se a medição de B-089 mostrar necessidade.
- Modo compacto por mensagem (a densidade global já existe).

## Contratos

Nenhum novo. O divisor usa o cursor de leitura de B-094; até ele existir, usa o
`lastReadSeq` local.

## UX

- Agrupamento reduz o espaço vertical entre bolhas do mesmo bloco, sem repetir avatar.
- Separador de data centralizado, com linha e fundo do próprio surface.
- Divisor de não lidas com cor de destaque e rótulo à direita.
- Botão “ir para a última” fixo acima do composer; some ao chegar ao fim.
- Leitor de tela: o separador de data é `role="separator"` com `aria-label` da data
  completa; o divisor de não lidas é anunciado uma vez via `aria-live`.

## Multi-tenant e authZ

Nada novo — é apresentação de dados já autorizados.

## Aceite

- [ ] Três mensagens seguidas da Alice em 1 min viram um bloco com um cabeçalho
- [ ] Mensagem 6 min depois abre bloco novo
- [ ] Separador de data aparece na virada e gruda no topo ao rolar
- [ ] Abrir canal com 5 não lidas mostra o divisor na posição certa
- [ ] Rolando para o histórico, mensagem nova **não** arrasta a viewport
- [ ] Botão de ir para a última mostra a contagem e funciona

## Testes

- Unit (web): regra de agrupamento (autor + janela), cálculo de separadores,
  posição do divisor.
- E2E: duas sessões; conferir que o leitor de histórico não é puxado para baixo.

## Riscos

- Salto de scroll ao carregar histórico (B-089) → ancorar pela altura antes/depois.
- Agrupar mensagens editadas ou apagadas → estado do bloco recalculado por evento.
