# B-090 — Preview de anexos

> Wave W9-3 · Trilha C/D · Deps: B-079, B-163 · Decisões: D-11 · Risco R2

## Problema

A bolha tipada e o lightbox de mídia entram em **B-163** (UI). Sem derivados no
worker, a timeline ainda baixa o **original** para mostrar preview — caro em
imagem/PDF grandes e sem reserva de espaço (`Width`/`Height`).

## Escopo

- Miniatura gerada pelo **worker** e servida no lugar do original na timeline
  (slot visual já definido em B-163).
- Imagem: WebP lado maior 640 px; cliente usa `ThumbnailKey` + `Width`/`Height`.
- Visualizador em tela cheia (B-163) passa a preferir a miniatura na grade e o
  original só no “baixar” / zoom.
- PDF: primeira página como miniatura + contagem de páginas.
- Áudio permanece no player de B-080; vídeo usa o cartão/player tipado de B-163.
  Aceite de MIME/limites de vídeo e prévia no composer → **B-168** (pode reutilizar
  `ThumbnailKey` desta fatia como poster quando `Ready`).
- Falha ao carregar a miniatura cai para o cartão de arquivo, sem bolha quebrada.

## Fora de escopo

- Layout tipado da bolha, toolbar/context menu e lightbox client-side → **B-163**
  (deps: B-163 entregue ou em paralelo com fallback ao original).
- Visualizador de PDF embutido rico, edição/anotação de imagem, preview de Office.
- Transcodificação de vídeo; liberar upload de vídeo → **B-168**.

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
- GIF anima só no hover; com `prefers-reduced-motion`, permanece no primeiro frame.

## Multi-tenant e authZ

Miniatura mora no mesmo prefixo de tenant do original (`{tenantId}/{workspaceId}/…`) e
usa a mesma verificação de membership no presign. Purge de retenção (B-047) apaga a
miniatura junto com o original — incluir no teste de purge.

## Aceite

- [x] `complete` do upload enfileira job; `ThumbnailStatus` → `Ready` com `ThumbnailKey`
- [x] Timeline (B-163) serve a miniatura no lugar do original (bytes baixados menores)
- [x] `Width`/`Height` evitam layout shift no placeholder
- [x] PDF: miniatura da 1ª página + contagem de páginas no DTO
- [x] Miniatura falhada → `Failed`; bolha cai no cartão de arquivo
- [x] Miniatura de outro tenant → 403
- [x] Purge (B-047) remove original e miniatura

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
