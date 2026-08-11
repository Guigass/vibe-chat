# B-080 — Mensagem de áudio

> Wave W8-2 · Trilha C/D · Deps: B-079 · Decisões: D-12, D-06 · Risco R3

## Problema

Não há como gravar e mandar um áudio. Em conversa de trabalho no celular, é o caminho
mais rápido para explicar algo que levaria três parágrafos — é a razão de o WhatsApp
ter feito da mensagem de voz um recurso central.

## Escopo

- Botão de microfone no composer: pressionar inicia, com timer e waveform ao vivo.
- Parar, **ouvir antes de enviar**, descartar e enviar.
- Limites de D-12: **5 minutos** e **10 MB**; o cliente para a gravação no limite.
- Formato negociado em runtime via `MediaRecorder.isTypeSupported`, na ordem
  `audio/webm;codecs=opus` → `audio/ogg;codecs=opus` → `audio/mp4`. Sem transcodificação
  no servidor nesta fase; o player usa o formato original.
- Waveform amostrada durante a gravação e persistida como array de amplitudes nos
  metadados do anexo — a bolha desenha na hora, sem baixar o áudio.
- Player na bolha: play/pause, duração, barra de progresso arrastável, velocidade 1×/1,5×/2×.
- Transcrição sob demanda (**opt-in**), pelo caminho de IA existente.

## Fora de escopo

- Chamada de voz ao vivo e canal de voz — fora por D-11.
- Transcrição automática de todo áudio — só sob demanda, para não mandar tudo ao provedor.
- Cancelamento de ruído, normalização e transcodificação server-side.
- Vídeo curto / anexo de vídeo → **B-168**.

## Contratos

Áudio é um `Attachment` (D-12). Campos novos em `files.attachments`:

| Campo | Tipo | Nota |
|-------|------|------|
| `Kind` | enum (`File`, `Audio`) | default `File`; migration com backfill |
| `DurationMs` | int? | só para `Audio` |
| `Waveform` | `jsonb`? | array de 0–100, até 100 pontos |

Endpoint novo:

`POST /api/v1/workspaces/{workspaceId}/channels/{channelId}/messages/{messageId}/attachments/{attachmentId}/transcribe`

- Exige membership + permissão nova `ai.transcribe`
- `503` quando `Ai:Enabled=false` (mesmo comportamento de summarize/suggest-reply)
- Resposta `{ text, language, provider }`; resultado **não** é persistido na mensagem
- Nunca no hot path de `SendMessage` (D-06)

`Files:Audio:MaxDurationMs` (default `300000`) e `Files:Audio:MaxSizeBytes` (default
`10485760`), validados no servidor.

`contratos.md`: novo endpoint, novos campos de attachment, nova permissão.

## UX

- Microfone ao lado de “Anexar”. Sem permissão de mic, o botão explica em vez de sumir.
- Gravando: timer `mm:ss`, waveform ao vivo (`AnalyserNode` + `<canvas>`), botões
  **Parar** e **Descartar**; `Esc` descarta com confirmação.
- Gravado: player de prévia, **Regravar** e **Enviar**.
- Bolha: waveform estático, duração, play/pause, velocidade. Menu “Transcrever” só
  aparece quando a IA está ligada e o usuário tem a permissão.
- Sem suporte a `MediaRecorder` (ou fora de contexto seguro): botão oculto e dica
  apontando o anexo comum.
- Waveform é decorativa: `aria-hidden`. O player expõe `aria-label` com a duração.

## Multi-tenant e authZ

- Presign, membership e RLS iguais aos de qualquer anexo.
- `ai.transcribe` é permissão separada de `ai.summarize` — quem pode resumir não
  necessariamente pode transcrever áudio de terceiros.
- Transcrever anexo de outro tenant → 403 antes de qualquer chamada ao provedor.
- O texto transcrito não entra em log nem em audit; só o evento `ai.transcribe`.

## Aceite

- [ ] Gravar 10 s, ouvir, enviar; o outro usuário recebe e toca sem F5
- [ ] Gravação para sozinha em 5 min
- [ ] Áudio acima de 10 MB é rejeitado com mensagem clara
- [ ] Safari grava em `audio/mp4` e o Chrome toca o arquivo
- [ ] Waveform aparece na bolha sem baixar o áudio inteiro
- [ ] `Ai:Enabled=false` → “Transcrever” ausente; chamada direta devolve 503
- [ ] Negar permissão de microfone não quebra o composer

## Testes

- Unit (web): seleção de MIME por suporte, corte no limite, redução da waveform.
- Unit (api): validação de duração/tamanho; `Kind=Audio` exige `DurationMs`.
- Integration: enviar mensagem com anexo de áudio, ler no history com metadados.
- Security: transcrever anexo cross-tenant → 403; sem `ai.transcribe` → 403;
  `Ai:Enabled=false` → 503.
- E2E: gravar (áudio sintético), enviar, tocar na segunda sessão.

## Riscos

- Fragmentação de formato entre navegadores → negociar, nunca fixar; guardar o MIME real.
- Áudio longo em conexão ruim → progresso por chunk do upload já vem de B-079.
- PII em áudio indo para provedor externo → opt-in explícito, permissão dedicada e
  aviso na UI antes da primeira transcrição.
