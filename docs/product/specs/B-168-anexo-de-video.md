# B-168 — Anexo de vídeo (aceite, preview e visualização)

> Wave W9-8 · Trilha C/D · Deps: B-079, B-163 · Soft: B-090 · Decisões: D-11 · Risco R2

## Problema

A bolha tipada de **B-163** já classifica `video/*` e desenha player/cartão, mas a
política de upload **não aceita** vídeo (`AllowedContentTypes` só imagem/PDF/texto;
áudio vai por allowlist separada). Sem MIME liberado, limites e metadados, o usuário
não consegue anexar um clipe e vê-lo no chat.

## Escopo

- Aceitar anexo de vídeo curto via o pipeline de anexos existente (presign →
  complete → `SendMessage` com `attachmentIds`).
- MIME allowlist: `video/mp4`, `video/webm` (sem codecs exóticos obrigatórios no
  servidor; o browser reproduz o original).
- Limites server-side: `Files:Video:MaxDurationMs` (default **60 s**) e
  `Files:Video:MaxSizeBytes` (default **25 MB**), além do teto de anexos por
  mensagem (B-079).
- `AttachmentKind.Video` + metadados `DurationMs`, `Width`, `Height` quando o
  cliente (ou o worker em B-090) conseguir extrair.
- Composer: escolher arquivo de vídeo, **prévia local** antes do envio (poster/
  `<video>` com controles), progresso de upload já coberto por B-079.
- Timeline: reutilizar o slot tipado de B-163 — poster + duração na bolha; clique
  abre player inline/overlay com controles nativos; botão baixar o original.
- Quando B-090 estiver `Ready`, preferir `ThumbnailKey` como poster na timeline
  (não baixar o original só para mostrar capa).

## Fora de escopo

- Chamada de voz/vídeo ao vivo, huddle, screen share → **B-147** / **B-148** (D-11).
- Transcodificação, HLS/DASH, bitrate ladder, legendas, edição de vídeo.
- Gravação de câmera no composer (só anexo de arquivo nesta fatia).
- Miniatura gerada no worker para tipos não-vídeo → **B-090** (imagem/PDF).
- Malware scan / quarentena → **B-131**.

## Contratos

Estender `AttachmentKind` com `Video`. Campos reutilizados:

| Campo | Tipo | Nota |
|-------|------|------|
| `Kind` | enum (+ `Video`) | migration / backfill; default `File` |
| `DurationMs` | int? | obrigatório quando `Kind=Video` no complete |
| `Width` / `Height` | int? | reservar layout; opcional se o cliente não souber |
| `ThumbnailKey` / `ThumbnailStatus` | (B-090) | poster quando disponível |

Allowlist de vídeo **separada** da de arquivo genérico (espelha o padrão de áudio):

- `Files:Video:MaxDurationMs` (default `60000`)
- `Files:Video:MaxSizeBytes` (default `26214400`)
- tipos em `DefaultAllowedVideoContentTypes` (`video/mp4`, `video/webm`)

Validação no `complete` do upload e no vínculo à mensagem: MIME, tamanho e
duração. Sem transcode — rejeitar fora da política com 400 claro.

`contratos.md`: kind, limites, allowlist e comportamento do history/DTO.

## UX

- Input de anexar inclui `video/mp4,video/webm` (e drag/colar quando o tipo for
  vídeo, via B-079).
- Prévia no composer: thumbnail/frame + duração + remover da lista.
- Bolha: não autoplay com som; `preload="metadata"`; play explícito do usuário.
- `prefers-reduced-motion`: não autoplay mesmo sem som; capa estática basta.
- Falha de decode no browser → cartão tipado “Vídeo” + baixar (mesmo padrão B-163).
- Mensagem de erro amigável se exceder 60 s / 25 MB ou MIME não permitido.

## Multi-tenant e authZ

- Mesmo prefixo MinIO, membership e RLS de qualquer anexo.
- Presign de download/thumbnail com a mesma authZ do arquivo.
- Purge (B-047) remove original e poster (`ThumbnailKey`) juntos.
- Vídeo de outro tenant → 403 antes de qualquer byte.

## Aceite

- [ ] Upload `video/mp4` ou `video/webm` dentro dos limites completa e aparece no history
- [ ] MIME fora da allowlist / acima de 25 MB / acima de 60 s → 400 com motivo
- [ ] Composer mostra prévia local antes do envio
- [ ] Destinatário vê player/cartão tipado sem F5 (hub + history)
- [ ] Sem autoplay com áudio; play é gesto do usuário
- [ ] Com B-090 `Ready`, timeline usa poster (`ThumbnailKey`) em vez do original
- [ ] Cross-tenant download/thumbnail → 403

## Testes

- Unit (api): allowlist, limites de tamanho/duração, `Kind=Video` exige `DurationMs`.
- Unit (web): classificação `video/*`, prévia no composer, sem autoplay.
- Integration: upload + send + history devolve metadados de vídeo.
- Security: presign/download cross-tenant → 403.
- E2E (opcional): Alice envia clipe curto; Bob reproduz na timeline.

## Riscos

- Arquivo grande enchendo disco/banda → limites curtos na v1; teto configurável por
  tenant sem ultrapassar ceiling de env (ADR-020).
- Codec que o browser do receptor não toca → cartão + download; sem transcode.
- Poster/worker pesado → só via B-090; esta fatia não bloqueia em frame extract
  server-side.
- Confundir com live (B-147) → copy e docs deixam explícito: anexo assíncrono.
