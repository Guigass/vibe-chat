# ADR-066 — Screen share e gravação

- Status: Accepted
- Data: 2026-10-28
- Relacionado: B-148, B-147, D-19, ADR-065

## Contexto

D-19 deixa gravação fora do huddle até haver consentimento explícito. B-148
pede screen share com indicador, policy por workspace, gravação off por
default e o objeto na ACL da conversa. A mídia continua fora de `apps/api`.

## Decisão

1. Duas chaves nascem false: `Features:Live:ShareEnabled` e
   `Features:Live:RecordingEnabled`. Cada workspace tem uma policy, também
   false. O efeito é a conjunção. A policy não liga a chave da instância.
2. **Share.** `live.share` (Member e acima, sem Guest). O token só inclui
   `screen_share` enquanto a pessoa está compartilhando e a policy está ligada.
   Parar, revogar (`live.record`), sair ou desligar a policy zera o share.
3. **Gravação.** `live.record` (Moderator, Admin e dono) abre o estado
   `consent` para quem está na sessão. Só vira `recording` quando todos os
   que permaneceram aceitam. Recusa, ou a saída de todos, vira `declined` e
   não grava. Quem entra com a gravação já ativa não publica mídia até aceitar.
4. **Objeto.** O sink grava um manifesto `text/plain` no bucket privado, passa
   pelo scanner já usado nos anexos e devolve só a chave
   `t/{tenant}/live/{id}`. Não há URL pública no adapter. O download emite uma
   URL assinada de 15–300 s (default 60) depois da membership do canal.
5. **Ciclo.** Retenção default de 7 dias (1–30). Quota default de 32 MiB por
   workspace. Estouro responde `LiveRecordingQuota` e não grava. Legal hold
   ativo no workspace/canal impede o purge e mantém o download. Scan rejeitado
   não grava.
6. **Evento.** `live.recording.changed` schemaVersion 1 leva estado e `actorId`.
   Sem URL e sem mídia. A auditoria usa o mesmo ator.

## Alternativas

- LiveKit Egress gravando o WebRTC no SFU: pede outro processo e uma URL de
  callback. Fica atrás do mesmo sink quando a operação quiser os bytes de
  mídia. Nesta fatia o objeto é o manifesto selado, porque a API não recebe
  mídia.
- Gravação no Postgres: mistura bytea com a conversa e foge do lifecycle do
  bucket. Rejeitado.

## Rollback

1. `Features:Live:ShareEnabled=false` e `Features:Live:RecordingEnabled=false`.
   Share e gravação novos respondem 403. O áudio do huddle não muda.
2. Objetos já selados seguem a retenção. O `Down` remove `live_policies`,
   `live_recordings` e `live_recording_consents` e apaga os objetos do prefixo
   `t/*/live/`.

## Consequências

- **+** Ninguém grava em silêncio: sem aceite de todos, não há objeto.
- **+** A URL expira e a membership é revalidada no download.
- **−** O arquivo ainda não é o fluxo de mídia do SFU. O sink é o lugar desse
  byte quando o Egress entrar.
