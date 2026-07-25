# B-090 — Preview de anexos

> Wave W9-3 · Trilha C/D · Deps: B-079 · Decisões: D-11

## Problema

Anexo aparece como nome + tamanho e um link de download que abre em nova aba
(`message-bubble.ts`). Mandar um print obriga quem recebe a baixar o arquivo para ver.

## Escopo

- Imagem (`png`, `jpeg`, `webp`, `gif`) renderizada inline com proporção preservada,
  altura máxima de 320 px e `loading="lazy"`.
- Miniatura gerada pelo **worker** e servida no lugar do original na timeline.
- Visualizador em tela cheia: `Esc` fecha, setas navegam entre as imagens da mensagem,
  botão de baixar o original.
- PDF: primeira página como miniatura + contagem de páginas; abre em nova aba.
- Áudio usa o player de B-080; demais tipos mantêm o cartão atual.
- Falha ao carregar a miniatura cai para o cartão de arquivo, sem bolha quebrada.

## Fora de escopo

- Visualizador de PDF embutido, edição/anotação de imagem, preview de Office.
- Transcodificação de vídeo.

## Contratos

Miniatura é um derivado, não um `Attachment` novo. Em `files.attachments`:

| Campo | Tipo | Nota |
|-------|------|------|
| `ThumbnailKey` | text? | object key da miniatura |
| `Width` / `Height` | int? | do original, para reservar espaço e evitar layout shift |
| `ThumbnailStatus` | enum `Pending`\|`Ready`\|`Failed` | |

Job novo no worker, disparado pelo outbox no `complete` do upload. Miniatura em WebP,
lado maior de 640 px. **Nunca** no hot path do envio.

`GET .../attachments/{id}/thumbnail` devolve URL presignada da miniatura; mesma authZ
do download.

`contratos.md`: campos, endpoint e o job do worker.

## UX

- Enquanto `Pending`, mostra um placeholder do tamanho certo (usa `Width`/`Height`).
- `Failed` cai para o cartão de arquivo com o motivo no `title`.
- Visualizador com foco preso, `Esc` para fechar e foco devolvido à bolha.
- `alt` da imagem usa o nome do arquivo; decorativo nunca.
- GIF anima só no hover quando `prefers-reduced-motion` estiver ativo.

## Multi-tenant e authZ

Miniatura mora no mesmo prefixo de tenant do original (`{tenantId}/{workspaceId}/…`) e
usa a mesma verificação de membership no presign. Purge de retenção (B-047) apaga a
miniatura junto com o original — incluir no teste de purge.

## Aceite

- [ ] Enviar PNG mostra a imagem inline nas duas sessões
- [ ] Clicar abre em tela cheia; `Esc` fecha e devolve o foco
- [ ] Miniatura é servida em vez do original (verificar tamanho baixado)
- [ ] PDF mostra a primeira página e a contagem
- [ ] Miniatura falhada cai para o cartão de arquivo
- [ ] Miniatura de outro tenant → 403
- [ ] Sem layout shift ao carregar (dimensões reservadas)

## Testes

- Unit (worker): geração de miniatura, limites de dimensão, tipo não suportado.
- Integration: `complete` do upload enfileira o job; status vira `Ready`.
- Security: presign de miniatura cross-tenant → 403.
- Integration (B-047): purge remove original **e** miniatura.

## Riscos

- Imagem gigante estourando memória do worker → limite de dimensão de entrada e recusa
  acima dele.
- Miniatura como vetor de conteúdo malicioso → gerar a partir do decode, nunca copiar
  bytes do original.
