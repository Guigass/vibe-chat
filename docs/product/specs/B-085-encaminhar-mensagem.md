# B-085 — Encaminhar mensagem

> Wave W8-7 · Trilha C/D · Deps: B-084 · Decisões: D-11 · Risco R2

## Problema

Para levar uma informação de um canal a outro, hoje só copiando e colando — o que
perde autoria, anexos e contexto.

## Escopo

- Ação “Encaminhar” na bolha, com seletor de destino (canais e DMs onde o usuário é membro).
- Encaminhar para até 5 destinos de uma vez.
- Comentário opcional junto do encaminhamento.
- A mensagem encaminhada mostra origem: autor original, canal e data.
- Anexos vão junto por **referência**, sem recopiar bytes no MinIO.

## Fora de escopo

- Encaminhar para fora do workspace ou por e-mail.
- Encaminhar thread inteira.

## Contratos

`POST /api/v1/workspaces/{workspaceId}/messages/{messageId}/forward`

```json
{ "targetChannelIds": ["..."], "comment": "opcional", "idempotencyKey": "..." }
```

- Exige membership na origem **e** em cada destino; qualquer destino inválido → 403 e
  nenhum envio parcial.
- Cria uma mensagem nova por destino, com `seq` e outbox próprios (invariante mantida).
- Novos campos em `messaging.messages`: `ForwardedFromMessageId`, `ForwardedFromChannelId`.
- Anexos: novas linhas em `files.attachments` apontando para o **mesmo** object key,
  com contagem de referência para o purge não apagar bytes ainda usados.

`contratos.md`: endpoint, campos, regra de anexo por referência.

## UX

- Diálogo com busca de destino, chips dos selecionados e campo de comentário.
- Bolha encaminhada: cabeçalho discreto “Encaminhada de #origem · Alice · 12/03”.
- Se a origem foi apagada depois, o cabeçalho permanece (é registro histórico).

## Multi-tenant e authZ

- Membership verificada em origem e destino, sempre no servidor.
- Encaminhar para canal de outro tenant é impossível; teste negativo obrigatório.
- Encaminhar de canal privado para público é permitido só para quem é membro dos dois —
  a UI avisa que o conteúdo ficará visível a mais gente.
- Cada encaminhamento gera audit `message.forward` com origem e destinos.

## Aceite

- [x] Encaminhar para 2 canais cria 2 mensagens com `seq` próprio
- [x] Anexo aparece nos destinos sem duplicar bytes no MinIO
- [x] Um destino sem membership → 403 e nenhum envio
- [x] Cabeçalho de origem correto na bolha
- [x] Reenvio com a mesma `idempotencyKey` não duplica
- [x] Purge de retenção não apaga blob ainda referenciado

## Testes

- Integration: fan-out para N destinos; idempotência; contagem de referência de anexo.
- Security: destino cross-tenant → 403; origem sem membership → 403.
- Unit: contagem de referência impede delete prematuro no purge (B-047).

## Riscos

- Vazamento de canal privado para público → aviso explícito e audit.
- Purge apagando blob compartilhado → contagem de referência com teste dedicado.
