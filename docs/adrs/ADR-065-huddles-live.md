# ADR-065 — Huddles de áudio/vídeo

- Status: Accepted
- Data: 2026-10-28
- Relacionado: B-147, B-146, D-19, ADR-048

## Contexto

D-19 pede áudio/vídeo opcional, self-hosted, fora de `apps/api`, desligado por
padrão. B-146 já fixa o perfil Small. O pacote R3 pede interface de provider,
SFU/TURN OSS, token efêmero e chat independente da mídia.

## Decisão

1. Flag `Features:Live:Enabled` nasce false. Vídeo é outra chave,
   `Features:Live:VideoEnabled`, também false. Desligada, as rotas `/live`
   respondem 404 `LiveDisabled`. Enviar mensagem não consulta o provider.
2. **Stack.** LiveKit server (`livekit/livekit-server`, Apache-2.0) no profile
   Compose `live`, com TURN embutido. A API só assina um JWT HS256 de curta
   duração (15–120 s, default 60). O cliente fala WebRTC com o SFU. A API não
   abre mídia.
3. **Sala.** O nome é `t/{tenant}/c/{channel}/s/{session}`. O token traz essa
   sala e o `sub` do usuário. Outra sala ou outro usuário recebe
   `LiveTokenRoomMismatch` ou `LiveTokenInvalid`. O hash do token fica em
   `conversations.live_tokens`; o segundo `connect` recebe `LiveTokenReplayed`.
4. **Provider.** `none` (default) não emite token. `dev` assina com chave de
   lab e não aponta SFU — é o duplo dos testes. `livekit` exige URL, API key e
   segredo de pelo menos 32 bytes e faz GET curto na URL configurada.
   `down` fica unhealthy de propósito. Health check `live` degrada; não marca
   Unhealthy, para o readiness do chat continuar.
5. **Capacidade Small.** 8 participantes e 2 sessões ativas por workspace.
   Acima disso, 429 `LiveCapacityExceeded`. Uma sessão ativa por canal.
6. **Vídeo.** A sessão grava `VideoAllowed` na criação. Com a policy desligada,
   o grant não inclui `camera` e ligar a câmera responde `LiveVideoDisabled`.
7. **Evento.** Outbox `live.session.changed` schemaVersion 1 leva estado e
   contagem. Sem token e sem mídia. Métricas
   `vibechat.live.sessions|joins|rejected|participants`.
8. **AuthZ.** Tenant da membership. RLS FORCE nas três tabelas. Join e connect
   revalidam o canal.

## Alternativas

- Janus (GPL-3.0): process separado seria possível, mas a licença copyleft
  piora a provenance do profile padrão. Rejeitado.
- mediasoup (ISC): SFU forte, porém pede worker Node/C++ e TURN à parte.
  Mais operação do que um huddle Small. Rejeitado nesta fatia.
- Jitsi (Apache-2.0): vários processos (Prosody, Jicofo, JVB). Pesado para o
  profile isolado. Rejeitado.
- coturn ao lado do LiveKit: o servidor já embute TURN. Um segundo processo
  fica para quando o profile precisar de TURN externo.

## Rollback

1. `Features:Live:Enabled=false`. Sessão nova não nasce. Token já emitido
   expira em até 120 s. O chat não muda.
2. `Down` remove `live_sessions`, `live_participants` e `live_tokens`. Não há
   mídia no Postgres.

## Consequências

- **+** O chat sobe sem o profile `live`.
- **+** Token de uma sala não entra em outra, e replay no `connect` falha.
- **−** O cliente web pede o microfone ao confirmar e resgata o token. Quem
  aponta `signalUrl` usa o JWT no SFU; o bundle não embute o SDK do LiveKit.
- **−** Gravação e transcrição continuam fora (B-148 / B-149).
