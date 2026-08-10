# B-163 — Refatoração do message bubble (layout tipado + ações + preview)

> Wave W9-0 · Trilha D · Deps: B-023, B-024, B-083 · Decisões: D-11, D-15 · Risco R1

## Problema

A bolha (`apps/web/src/app/shared/ui/message-bubble/message-bubble.ts`) mistura três
dívidas na mesma superfície:

1. **Chrome barulhento** — seis emojis rápidos, “mais…”, Responder, Abrir thread,
   Editar e Apagar sempre visíveis; Waves 8–9 ainda vão acrescentar citar, encaminhar,
   fixar e salvar.
2. **Layout inconsistente** — texto, citação, encaminhada, áudio e anexos “arquivo”
   não compartilham a mesma grade de alinhamento (mine/theirs), largura máxima nem
   ritmo vertical; bolha só-imagem ou só-áudio “pula” em relação à de texto.
3. **Anexo sem preview** — tudo que não é `Audio` vira botão `nome + tamanho`; imagem,
   PDF, vídeo e demais tipos obrigam download para saber o que é.

## Escopo

### Interação (ações)

- Refatorar o layout da bolha para leitura primeiro: corpo, metadados e reações
  aplicadas; chrome de ação fora do fluxo visual padrão.
- Ações primárias (reagir, responder) só no **hover/focus** da bolha, em toolbar
  compacta (ícones), não como botões texto permanente.
- Demais opções (editar, apagar, abrir thread, e futuros: citar, encaminhar, fixar,
  salvar) em **context menu** (clique direito / long-press / botão “⋯”).
- Menu via Angular CDK + padrão spartan/ui (D-15 / B-104); teclado e leitor de tela.
- Manter contratos e eventos atuais de edit/delete/react/reply/thread; só muda a
  superfície de UI.

### Layout tipado (alinhamento)

- Uma única composição de bolha para todos os conteúdos: texto, citação (`replyTo`),
  encaminhada, anexos e reações — mesmos eixos mine/theirs, `max-width`, padding e
  gap entre blocos.
- Variantes por tipo de mídia **não** inventam outro shell; só o bloco interno muda
  (imagem, player, cartão de arquivo, etc.).
- Mensagem só-anexo (body vazio) e mensagem texto+anexo usam a mesma coluna e o mesmo
  alinhamento do autor.
- Metadados (autor, hora, status, “editada”) e chips de reação ficam nos mesmos
  pontos relativos em todas as variantes.

### Preview de anexos na bolha (UI)

Renderização tipada pelo `contentType` / `kind` já existentes (sem novo endpoint nesta
entrega). Toque/clique abre preview adequado:

| Tipo | Na bolha | No toque / clique |
|------|----------|-------------------|
| **Image** (`image/png`, `jpeg`, `webp`, `gif`) | Miniatura inline (proporção preservada, altura máx. ~320 px, `loading="lazy"`) | Lightbox / tela cheia; `Esc` fecha; baixar original |
| **PDF** (`application/pdf`) | Cartão com ícone PDF, nome, tamanho; se houver miniatura (B-090), primeira página | Abre visualização (nova aba ou overlay leve); baixar sempre disponível |
| **Audio** (`kind=Audio` / B-080) | Player existente (`vc-audio-message`) | Play in-place; sem lightbox extra |
| **Video** (`video/mp4`, `webm`, …) | Poster/frame + duração se houver; senão cartão tipado “Vídeo” | Player inline ou overlay com controles nativos; sem transcodificação |
| **Outros** (Office, zip, etc.) | Cartão de arquivo tipado (ícone por família MIME, nome, tamanho) | Download / abrir em nova aba |

Regras:

- Falha ao carregar mídia cai para o cartão de arquivo — bolha nunca quebra.
- GIF: anima no hover; com `prefers-reduced-motion`, fica no primeiro frame.
- Vários anexos na mesma mensagem: grade consistente (lista empilhada), cada um com
  seu preview; lightbox de imagem navega entre as imagens **daquela** mensagem.
- Até B-090 entregar `ThumbnailKey` / dimensões, a bolha pode usar URL presignada do
  original com `max-height` e lazy — otimização de bytes fica na B-090.

## Fora de escopo

- Novas capacidades de mensageria (B-084, B-085, B-092, B-093) — só o slot de
  entrada no menu quando esses itens existirem.
- Miniatura gerada no worker, `ThumbnailKey`, job de PDF/página, purge de derivados
  → **B-090**.
- Redesign do composer, avatar ou agrupamento da timeline (B-088).
- Visualizador de PDF embutido rico, anotação de imagem, preview de Office, transcode
  de vídeo.
- Clonar visual Slack/Discord/WhatsApp.

## Contratos

Nenhum novo endpoint, evento ou schema nesta item. AuthZ e payloads de
edit/delete/react/reply/thread e download de anexo permanecem iguais. Classificação
na UI usa `contentType` + `kind` já no `AttachmentDto`. Campos de miniatura
(`ThumbnailKey`, `Width`/`Height`, `ThumbnailStatus`) entram com B-090; a bolha
desta spec já reserva o slot visual para quando existirem.

## UX

### Ações

- Estado default: bolha sem barra de ações visível; chips de reação já aplicadas
  continuam abaixo do corpo.
- Hover (pointer fine) ou focus-within: toolbar flutuante discreta com reagir,
  responder e “mais”.
- Context menu e “mais” listam as mesmas opções; itens destrutivos (Apagar) no
  final, com estilo de perigo.
- Touch: long-press abre o menu; toolbar também acessível pelo botão “⋯” sempre
  alcançável no focus.
- `Esc` fecha menu/picker/lightbox; foco retorna ao gatilho.

### Layout e preview

- Densidade alinhada ao design system (`--vc-*`); motion sutil (toolbar + abrir/
  fechar preview), sem glow nem pills genéricos.
- Imagem e vídeo respeitam a largura da coluna da bolha; não estouram a timeline.
- Cartões de arquivo/PDF/vídeo compartilham tipografia e hierarquia (ícone · nome ·
  meta); só o tratamento do clique muda.
- `alt` de imagem = nome do arquivo; preview nunca é só decorativo.
- Lightbox/overlay com foco preso; setas navegam entre imagens da mensagem quando
  houver mais de uma.

## Multi-tenant e authZ

Sem mudança. Download/preview usam o mesmo presign e membership de hoje. Itens do
menu só aparecem se o cliente já teria mostrado o botão (ex.: Editar/Apagar só em
`message().mine` e políticas vigentes). Servidor continua a fonte de verdade.

## Aceite

### Ações

- [ ] Bolha sem hover/focus não mostra barra de reações rápidas nem Editar/Apagar
- [ ] Hover ou focus revela toolbar compacta com reagir + responder + “mais”
- [ ] Clique direito / long-press / “mais” abre o mesmo menu de opções
- [ ] Editar, apagar, reagir, responder e abrir thread continuam funcionando
- [ ] Menu e toolbar navegáveis só por teclado; `Esc` fecha
- [ ] Touch: long-press abre o menu sem selecionar texto por engano

### Layout e preview

- [ ] Texto, citação, áudio, imagem, PDF, vídeo e arquivo “genérico” alinham na
      mesma coluna mine/theirs (mesma `max-width` / padding)
- [ ] Mensagem só-imagem e só-áudio não deslocam avatar/meta em relação à de texto
- [ ] PNG/JPEG/WebP/GIF mostram preview inline; clique abre lightbox; `Esc` fecha
- [ ] PDF mostra cartão tipado; clique abre visualização / download sem quebrar a bolha
- [ ] Áudio continua no player B-080
- [ ] Vídeo tem cartão/player tipado no clique (sem transcode)
- [ ] Tipo desconhecido ou falha de carga → cartão de arquivo estável
- [ ] Várias imagens na mesma mensagem: lightbox com navegação entre elas

## Testes

- Unit (web): visibilidade da toolbar por hover/focus; itens do menu filtrados por
  `mine` / flags de ação; classificação MIME → variante de preview; fallback em
  erro de carga.
- Component/E2E: abrir menu, editar e apagar; reagir via toolbar; abrir lightbox de
  imagem; cartão PDF/vídeo/arquivo.
- Snapshot/layout (opcional): variantes mine/theirs para texto, imagem, áudio e
  arquivo com a mesma grade.
- a11y: foco preso no menu e no lightbox; anúncio do botão “mais”; `alt` da imagem.

## Riscos

- Descoberta de ações em mobile → long-press + “⋯” sempre no focus.
- Regressão em features que emitem outputs da bolha → manter a mesma API de
  `@Output`; só muda o gatilho visual.
- Preview com original grande antes de B-090 → lazy + `max-height`; não bloquear
  W9-0 esperando o worker.
- Sobreposição com B-090 → esta spec é UI/layout; B-090 é derivado no worker +
  endpoint de thumbnail. Quando B-090 aterrissar, a bolha só troca a fonte da
  miniatura.
