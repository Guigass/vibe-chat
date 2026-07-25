# B-084 — Responder citando

> Wave W8-6 · Trilha C/D · Deps: — · Decisões: D-11

## Problema

A única forma de responder algo específico é abrir thread, o que tira a conversa do
canal. Falta a resposta leve e inline — citar a mensagem e responder no mesmo fluxo —
que existe nos quatro apps de referência.

## Escopo

- Ação “Responder” na bolha: coloca a mensagem citada acima do textarea.
- Envio grava a referência; a bolha mostra um bloco de citação com autor e trecho.
- Clicar na citação rola até a mensagem original e a destaca por ~2 s.
- Se a original foi apagada, a citação vira “Mensagem removida”, sem link.
- Cancelar a citação antes de enviar.

## Fora de escopo

- Citar mensagem de outro canal — reabrir junto de B-085 (encaminhar).
- Citar trecho selecionado. A citação é da mensagem inteira.

## Contratos

`Message.ReplyToMessageId` **já existe** no schema e no `SendMessageCommand`; hoje o
cliente nunca preenche. Esta fatia é sobretudo de UI + resposta da API.

- `GET .../messages` passa a devolver `replyTo: { messageId, authorName, preview, deleted }`
  (prévia de até 140 caracteres), resolvido no servidor para evitar N+1 no cliente.
- `MessageCreated` carrega o mesmo objeto.
- Validação: `replyToMessageId` tem de ser do **mesmo canal**; caso contrário 400.

`contratos.md`: campo `replyTo` no history e no evento.

## UX

- Barra de citação acima do textarea: autor, uma linha do texto, `×` para cancelar.
- Na bolha, o bloco citado é clicável, com borda esquerda em `--color-accent`.
- Rolar até a original: se estiver fora da página carregada, usa B-089 para buscar.
- Citação de mensagem apagada aparece em cinza, sem link, sem quebrar a bolha.

## Multi-tenant e authZ

A resolução de `replyTo` passa pelo mesmo filtro de membership do history — nunca
devolve prévia de mensagem que o leitor não poderia ler. Referência cross-tenant é
impossível pela validação de canal, e há teste negativo para isso.

## Aceite

- [ ] Responder mostra a citação no composer e na bolha enviada
- [ ] Clicar na citação rola e destaca a original
- [ ] Apagar a original transforma a citação em “Mensagem removida”
- [ ] `replyToMessageId` de outro canal → 400
- [ ] Citação também funciona dentro de thread

## Testes

- Unit (web): montagem e cancelamento da citação; corte da prévia.
- Integration: enviar com `replyToMessageId` devolve `replyTo` no history; canal
  diferente → 400.
- Security: `replyToMessageId` de outro tenant → 400/403 sem vazar prévia.
- E2E: Bob responde citando Alice; Alice vê a citação sem F5.

## Riscos

- N+1 ao resolver prévias → resolver em lote por página de history.
- Confusão entre citar e thread → rótulos distintos (“Responder” vs. “Abrir thread”).
