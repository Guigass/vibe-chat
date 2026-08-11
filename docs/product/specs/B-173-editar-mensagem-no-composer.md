# B-173 — Editar mensagem no composer (não na bolha)

> Wave W9-10 · Trilha D · Deps: B-023, B-084, B-163 · Decisões: D-11 · Risco R1

## Problema

B-023 entregou editar/soft-delete, mas a UI de edição é um **textarea inline
dentro da bolha** (`message-bubble` em modo `editing`). Isso diverge do padrão
já estabelecido para responder (B-084): a ação na bolha/menu só prepara o
contexto; a composição acontece no **composer**. Também atrapalha o atalho `↑`
da B-099 (editar a última mensagem própria no composer vazio) e compete com o
layout tipado da bolha (B-163).

## Escopo

- Ação **Editar** (toolbar, context menu, `↑` no composer vazio) entra em modo
  de edição no **composer** do canal/thread — barra de contexto (“Editando…”)
  com prévia/autor, foco no textarea e `×` / `Esc` para cancelar.
- Salvar envia o mesmo `EditMessage` já existente; cancelar restaura o draft
  anterior do composer (respeitar B-086).
- Remover o modo `editing` / textarea inline da bolha; a bolha só mostra body +
  badge “editada”.
- Thread panel: mesma regra (editar no composer da thread, não na bolha).
- Políticas de quem/quando pode editar continuam em
  [B-107](B-107-politicas-edicao-mensagem.md) (`messaging.edit.*`); este item
  só muda a **superfície de UI**. Enquanto B-107 não existir, manter a authZ
  binária atual (`message.edit.own`).

## Fora de escopo

- Janela de tempo, papéis e override de moderação → **B-107**.
- Histórico de versões do body → B-114.
- Editar anexos/reações; editar mensagem alheia sem override.
- Mudança de contrato HTTP/hub além do já documentado em `EditMessage`.

## Contratos

Nenhum novo. Reusa `PUT …/messages/{id}` / hub `MessageEdited` (B-023).

## UX

- Espelhar a barra de citação (B-084): contexto acima do textarea, Enviar vira
  “Salvar” (ou equivalente) enquanto `editingMessageId` estiver setado.
- Menu/toolbar da bolha só disparam `startEdit` no store/composer; não trocam
  o corpo da bolha por formulário.
- Composer em edição não dispara envio novo nem slash que conflite; `Esc`
  cancela (alinhado a B-099).
- Após salvar/cancelar, foco volta ao composer no estado normal.

## Multi-tenant e authZ

Inalterado: só quem já pode editar vê a ação; o servidor continua a gate.

## Aceite

- [ ] “Editar” no menu/toolbar carrega o body no composer com barra de contexto
- [ ] Bolha não entra em modo textarea; badge “editada” permanece após save
- [ ] `Esc` / `×` cancelam sem persistir; draft do canal restaurado (B-086)
- [ ] Thread: editar usa o composer da thread
- [ ] Sem regressão de send/reply/anexo no composer

## Testes

- Unit: store/composer `editingMessageId`; bolha sem ramo `editing()`.
- Component: menu Editar → composer em modo edição; Salvar chama `edit`.
- E2E leve: editar via menu; body atualiza na timeline sem F5.

## Riscos

- Conflito reply + edit no mesmo composer → exclusão mútua (ou cancelar um ao
  iniciar o outro), igual ao cuidado reply/thread.
- B-099 `↑` antes deste item → atalho só fecha quando o modo composer existir.
