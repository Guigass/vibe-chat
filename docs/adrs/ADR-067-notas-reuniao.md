# ADR-067 — Transcrição e notas de reunião

- Status: Accepted
- Data: 2026-10-29
- Relacionado: B-149, B-147, B-148, D-22, ADR-065, ADR-066

## Contexto

D-22 exige provider neutro, externo opt-in, orçamento, auditoria e citação.
B-149 pede transcrição de reunião com consentimento, nota com trechos temporais
e confirmação humana antes de publicar decisão ou tarefa. A mídia continua fora
de `apps/api`. A gravação de B-148 já é o objeto autorizado.

## Decisão

1. A flag `Features:MeetingAi:Enabled` nasce false. A policy do workspace
   também nasce false. O efeito é a conjunção. O provider nasce `none`.
   `local` processa a referência do objeto no processo e não faz egress.
   `external` é opt-in e só recebe a chave do objeto e metadado de policy
   (`rag=off`), nunca o corpo da conversa nem URL pública.
2. A transcrição é outro serviço da gravação. Sem gravação `ready`, a rota
   responde `LiveTranscriptionNoMedia` e não chama o provider. O pedido abre
   em `consent`. Só vai para a fila quando cada participante ainda na sessão
   aceita. Recusa, cancelamento ou saída de todos em `consent` não envia áudio.
3. O job é o outbox `live.transcription.requested`. Falha de provider ou de
   orçamento `ai-budget` marca a transcrição `failed` e não apaga o objeto da
   gravação. O evento no hub leva estado e ids, sem texto.
4. A nota guarda segmentos com início e fim. Rótulo de falante só no formato
   `speaker-N`. Nome ou biometria não é persistido. Sugestões nascem
   `suggested`. Publicar ou dispensar é um comando humano. Nada vira decisão
   ou tarefa de outro módulo sozinho.
5. A nota não entra no RAG. Apagar remove segmentos, sugestões e a referência
   no provider quando ele existe. A retenção segue a da gravação. Legal hold
   no workspace ou canal impede o purge. Canal confidencial responde
   `E2eeCapabilityUnavailable`.

## Alternativas

- Transcrever o fluxo do SFU dentro da API: a API passaria a receber mídia.
  Rejeitado. O job usa a chave do objeto já selado.
- Criar decisão e tarefa no mesmo passo do modelo: a spec proíbe publicação
  sem confirmação. A sugestão fica na nota até o comando.

## Rollback

1. `Features:MeetingAi:Enabled=false`. Pedido novo responde 403
   `LiveTranscriptionDisabled`. Gravação, huddle e chat seguem.
2. O `Down` remove `live_transcripts`, `live_transcript_consents`,
   `live_transcript_segments`, `live_note_suggestions` e a coluna
   `TranscriptionEnabled` de `live_policies`. Objetos de gravação permanecem.

## Consequências

- **+** Sem aceite, o contador de envio externo permanece em zero.
- **+** A falha da nota não apaga a gravação.
- **−** O texto do laboratório não é o áudio do SFU. O contrato do provider é
  o lugar desse byte quando um adapter local de fala existir.
