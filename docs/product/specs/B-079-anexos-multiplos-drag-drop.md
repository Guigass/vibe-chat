# B-079 — Anexos: múltiplos, drag & drop, colar e progresso

> Wave W8-1 · Trilha C/D · Deps: B-025 · Decisões: D-11

## Problema

O composer aceita **um** arquivo por vez, só pelo botão “Anexar”, sem progresso e sem
prévia (`apps/web/src/app/features/chat/composer/composer.ts` — `pendingFile` é um
único signal). Quem precisa mandar três prints faz três envios. Arrastar um arquivo
para a janela não faz nada; colar um print do clipboard também não.

## Escopo

- Até **10 anexos** por mensagem, com lista de pendentes no composer.
- Drag & drop sobre a área da conversa, com alvo de soltura visível só durante o drag.
- Colar arquivo/imagem do clipboard (`paste`) no composer.
- Barra de progresso por arquivo durante o upload, com cancelar.
- Validação client-side de tipo e tamanho **antes** de iniciar o upload, com mensagem
  dizendo qual arquivo e por quê.
- Falha de um arquivo não derruba os outros nem o texto já digitado.
- Remover anexo individual da lista antes de enviar.

## Fora de escopo

- Upload de pasta inteira e upload retomável (chunked) — sem demanda; reabrir se
  o limite de tamanho subir.
- Prévia inline de imagem na bolha — é B-090.
- Áudio gravado no navegador — é B-080.

## Contratos

Sem endpoint novo. Reusa o fluxo de B-025: `initiate` → `PUT` presigned → `complete` →
`SendMessage` com `attachmentIds`. Muda só a cardinalidade no cliente.

`Files:MaxSizeBytes` continua sendo o limite por arquivo. Novo:
`Files:MaxAttachmentsPerMessage` (default `10`), validado **também no servidor** — o
cliente não é fonte de verdade.

`contratos.md`: registrar o limite por mensagem e o 400 correspondente.

## UX

- Lista de pendentes acima do textarea: ícone por tipo, nome, tamanho, progresso, `×`.
- Drop zone cobre a timeline + composer; aparece só com `dragover` contendo arquivos,
  com borda tracejada em `--color-accent` e o texto “Solte para anexar”.
- Estados: validando, enviando (progresso determinado), enviado, falhou (com “tentar
  novamente” no item).
- `aria-live="polite"` anuncia “3 arquivos adicionados”, progresso e conclusão.
- Botão “Anexar” continua existindo e é o caminho principal — drag & drop e colar são
  enhancement (WCAG 2.2 · 2.5.7).

## Multi-tenant e authZ

Nada muda no modelo: presign já valida membership no canal e escopo de tenant. O limite
por mensagem é contado por requisição, não por usuário. Attachment órfão (upload feito,
mensagem nunca enviada) continua coberto pela limpeza existente.

## Aceite

- [ ] Enviar 3 imagens em uma mensagem, todas visíveis na bolha
- [ ] Arrastar 2 arquivos para a conversa preenche a lista de pendentes
- [ ] `Ctrl+V` com print no clipboard adiciona o arquivo
- [ ] Arquivo acima do limite é rejeitado antes de subir, com nome no erro
- [ ] Cancelar um upload em andamento não afeta os outros
- [ ] 11 arquivos → bloqueado no cliente e 400 no servidor se forçado
- [ ] Só teclado: `Tab` até “Anexar”, `Enter` abre o seletor, `Tab` chega ao `×` de cada item

## Testes

- Unit (web): validação de tipo/tamanho, limite de contagem, redução de lista.
- Integration: `SendMessage` com N `attachmentIds` persiste N linhas; N+1 → 400.
- Security: `attachmentId` de outro tenant no array → 403 e nenhuma mensagem criada.
- E2E: arrastar arquivo e enviar; verificar as duas bolhas em duas sessões.

## Riscos

- Drop zone capturando drag de texto ou de elemento interno → filtrar por
  `dataTransfer.types.includes('Files')`.
- 10 uploads paralelos saturando conexão lenta → fila com concorrência máxima de 3.
