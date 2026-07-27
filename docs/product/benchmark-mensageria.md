# Benchmark de Mensageria — VibeChat vs. Slack, Teams, Discord e WhatsApp

Levantamento de 2026-07-25 para dimensionar paridade de features. Serve de fonte
para as specs em `docs/product/specs/` e para as waves 8–10 do `roadmap.md`.

**Como ler:** “Temos” é o que existe hoje no `apps/web` + API (inventário conferido no
código, não no roadmap). “Falta” vira spec. “Fora de escopo” tem justificativa e ADR
ou decisão associada — não é para o agente implementar.

O objetivo **não** é clonar nenhum dos quatro. É ter a mensageria de trabalho que uma
empresa pequena espera em 2026, mantendo a identidade visual própria
(`docs/architecture/design-system.md`) e as regras de `AGENTS.md`.

---

## 1. Estrutura de conversa

| Capacidade | Slack | Teams | Discord | WhatsApp | VibeChat |
|---|---|---|---|---|---|
| Canais públicos/privados | sim | sim | sim | — | **Temos** |
| Agrupamento (space/categoria) | sim | sim | sim | — | **Temos** (Space) |
| DM 1:1 | sim | sim | sim | sim | **Temos** |
| DM em grupo | sim | sim | sim | sim | **Falta** — B-101 |
| Threads | sim | sim (layout threads) | sim | — | **Temos** |
| Canal de fórum (post = tópico) | — | posts layout | Forum Channels | — | Fora de escopo fase 2 |
| Seguir thread / followed threads | sim | sim | — | — | **Falta** — B-102 |
| Convidado externo | Connect | convidado | convite | — | **Falta** — B-040 |

Referências: Teams passou o layout padrão do primeiro canal para *threads* em maio/2026,
com gesto “Follow thread” e uma visão agregada de threads seguidas. Discord separa
*Forum Channels* (cada post é um tópico com tags) de canais de texto.

## 2. Composição de mensagem

| Capacidade | Slack | Teams | Discord | WhatsApp | VibeChat |
|---|---|---|---|---|---|
| Texto simples | sim | sim | sim | sim | **Temos** |
| Formatação rica (negrito, itálico, lista, código, citação) | sim | sim | markdown | limitado | **Falta** — B-081 |
| Bloco de código com linguagem | sim | sim | sim | — | **Falta** — B-081 |
| Menções `@pessoa` / `@canal` | sim | sim | sim | sim (grupos) | **Falta** — B-082 |
| Emoji picker | sim | sim | sim | sim | **Falta** — B-083 |
| Anexo por botão | sim | sim | sim | sim | **Temos** (1 arquivo) |
| Múltiplos anexos | sim | sim | sim | sim | **Falta** — B-079 |
| Drag & drop na conversa | sim | sim | sim | web sim | **Falta** — B-079 |
| Colar imagem do clipboard | sim | sim | sim | sim | **Falta** — B-079 |
| Progresso de upload por arquivo | sim | sim | sim | sim | **Falta** — B-079 |
| Mensagem de áudio | Clips | gravação | mensagens de voz | sim (central) | **Falta** — B-080 |
| Transcrição de áudio | sim | sim | — | sim (on-device) | **Falta** — B-080 (flag) |
| Clipe de vídeo curto | Clips | sim | — | sim | Fora de escopo fase 2 |
| Responder citando (inline) | sim | sim | sim | sim | **Falta** — B-084 |
| Encaminhar mensagem | sim | sim | sim | sim | **Falta** — B-085 |
| Rascunho persistente por conversa | sim | sim | sim | sim | **Falta** — B-086 |
| Agendar envio | sim | sim | — | — | Fora de escopo fase 2 |
| Comandos slash | sim | sim | sim | — | **Falta** — B-087 |
| Enquete | sim (workflow) | sim | sim | sim | **Falta** — B-096 |

Nota de pesquisa (áudio): o caminho web é `MediaRecorder` com detecção de MIME em
runtime — Chrome/Firefox preferem `audio/webm;codecs=opus`, Safari exige `audio/mp4`.
Waveform ao vivo sai de um `AnalyserNode` do Web Audio desenhado em `<canvas>`. Não
existe formato único suportado por todos os navegadores, então a spec fixa negociação
de formato em vez de hardcode. O WhatsApp transcreve **no dispositivo** e só para quem
recebe; no VibeChat a transcrição é servidor + flag de IA (D-06), por isso é opt-in.

Nota de pesquisa (anexos): WCAG 2.2 criou o critério **2.5.7 Dragging Movements** — toda
ação de arrastar precisa de alternativa por clique. Drag & drop entra como *enhancement*
sobre o `<input type="file">` que já existe, nunca como caminho único.

## 3. Leitura da timeline

| Capacidade | Slack | Teams | Discord | WhatsApp | VibeChat |
|---|---|---|---|---|---|
| Agrupamento de mensagens do mesmo autor | sim | sim | sim | sim | **Falta** — B-088 |
| Separador de data | sim | sim | sim | sim | **Falta** — B-088 |
| Divisor de não lidas | sim | sim | sim | sim | **Falta** — B-088 |
| Botão “ir para a mais recente” | sim | sim | sim | sim | **Falta** — B-088 |
| Carregar histórico antigo (scroll) | sim | sim | sim | sim | **Falta** — B-089 |
| Pular para a mensagem a partir da busca | sim | sim | sim | sim | **Falta** — B-089 |
| Preview inline de imagem | sim | sim | sim | sim | **Falta** — B-090 |
| Preview de PDF/documento | sim | sim | — | sim | **Falta** — B-090 |
| Link preview (unfurl) | sim | sim | sim | sim | **Falta** — B-091 |
| Fixar mensagem no canal | sim | sim | sim | sim | **Falta** — B-092 |
| Salvos / marcadores pessoais | sim | sim | — | favoritos | **Falta** — B-093 |
| Recibo de leitura / não lidas persistentes | por canal | sim | — | sim | **Falta** — B-094 (cursor no Postgres) |
| Reações | sim | sim | sim | sim | **Temos** (6 emojis fixos) |
| Reações com emoji livre / custom | sim | sim | sim + custom | sim | **Falta** — B-083 |
| Editar / apagar | sim | sim | sim | sim | **Parcial** — B-023 Done; falta política (janela/papéis) — **B-107** |
| Marcar como não lida | sim | sim | sim | sim | **Falta** — B-094 |

## 4. Notificações e foco

| Capacidade | Slack | Teams | Discord | WhatsApp | VibeChat |
|---|---|---|---|---|---|
| Notificação do navegador / push | sim | sim | sim | sim | **Falta** — B-095 |
| Preferência por canal | sim | sim | sim | sim | **Falta** — B-097 |
| Não perturbe / horário de silêncio | sim | sim | sim | sim | **Falta** — B-097 |
| Badge de menção vs. mensagem comum | sim | sim | sim | sim | **Falta** — B-082 + B-097 |
| Resumo/catch-up com IA | sim | sim | — | — | **Temos** (summarize, flag) |

## 5. Busca e navegação

| Capacidade | Slack | Teams | Discord | WhatsApp | VibeChat |
|---|---|---|---|---|---|
| Busca full-text | sim | sim | sim | sim | **Temos** |
| Filtros (autor, canal, data, tipo) | sim | sim | sim | sim | **Falta** — B-098 |
| Filtrar só anexos / só links | sim | sim | sim | sim | **Falta** — B-098 |
| Atalhos de teclado | extensos | sim | extensos | poucos | **Temos** (2) |
| Paleta de comandos (Ctrl+K) | sim | sim | sim | — | **Falta** — B-099 |

## 6. Plataforma e acesso

| Capacidade | Slack | Teams | Discord | WhatsApp | VibeChat |
|---|---|---|---|---|---|
| PWA instalável | sim | sim | sim | web | **Temos** |
| i18n | ~10 idiomas | muitos | muitos | muitos | **Falta** — B-100 (hoje PT-BR hardcoded) |
| Acessibilidade WCAG AA | sim | sim | parcial | parcial | **Parcial** — B-103 |
| Tema claro/escuro | sim | sim | sim | sim | **Temos** |
| Webhooks / integrações | sim | sim | sim | Business API | **Parcial** — B-048 Done (`MessageCreated`); estender em **B-108** |
| Bots / apps | sim | sim | sim | — | **Planejado em fases** — núcleo **B-109/B-110** (W10); plugins avançados **B-066/B-111** e registry governado **B-137** (W15) |

## 7. Tempo real por voz e vídeo

| Capacidade | Slack | Teams | Discord | WhatsApp | VibeChat |
|---|---|---|---|---|---|
| Áudio/vídeo ao vivo | Huddles | reuniões | canais de voz | chamadas | **Fora de escopo** |
| Compartilhar tela | sim | sim | sim | sim | **Fora de escopo** |
| Palco / evento | — | live events | Stage | — | **Fora de escopo** |
| Soundboard | — | — | sim | — | **Fora de escopo** |

Chamada ao vivo exige SFU/TURN, um plano de capacidade e operação que a fase 2 não
comporta — ver **D-11**. Mensagem de áudio **assíncrona** (B-080) cobre boa parte do
valor sem essa infraestrutura.

## 8. Superfícies de documento colaborativo

Slack tem Canvas e Lists; Teams tem Loop components. São editores colaborativos com
CRDT/OT, presença em nível de bloco e um modelo de permissão próprio — outro produto
dentro do produto. **Fora de escopo** na fase 2 (D-11). Threads + anexos + fixar
mensagem cobrem o caso “documentar decisão do canal”.

## 9. E2EE

WhatsApp é E2EE por padrão; os outros três não são, em favor de compliance e busca
server-side. O VibeChat já decidiu por auditoria de conversa (B-067) e export (B-046),
que são incompatíveis com E2EE puro. Entra em W16 (B-064), como canal
confidencial opt-in com capacidades reduzidas (D-26).

---

## Resumo do que virou fila

| Wave | Tema | Itens |
|------|------|-------|
| 8 | Composição de mensagem | B-079…B-087 |
| 9 | Leitura da timeline | B-088…B-094 |
| 10 | Notificações, organização e acesso | B-095…B-103, B-040 |

Cada item tem spec em `docs/product/specs/`. Sem spec, o item não é elegível para a
automação de Build.
