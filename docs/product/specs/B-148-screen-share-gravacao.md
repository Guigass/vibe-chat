# B-148 — Screen share e gravação

> Wave 17 · Trilha C/D/A/E · Deps: B-147, D-19 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Huddles sem compartilhamento limitam colaboração; gravação pode ser necessária,
mas exige consentimento e lifecycle rigorosos.

## Escopo

- Screen share com seleção do browser/OS e indicator persistente.
- Policy por workspace para habilitar share/recording.
- Gravação off por default, start/stop por role.
- Consentimento explícito e aviso contínuo a todos.
- Recording no object storage, scan/lifecycle/quota e ACL da conversa.
- Audit e download/export controlados.

## Fora de escopo

- Gravação silenciosa.
- Background recording sem participantes.
- Live streaming público.

## Contratos

Recording asset ligado a LiveSession; events consent/start/stop/ready; encryption
at rest e signed download. Provider adapter não retorna public URL.

## UX

Banner/voz visual de recording, lista de quem iniciou e confirmação. Usuário
pode sair antes de gravar.

## Multi-tenant e authZ

`live.share`/`live.record`; membership e policy revalidadas. Recording herda ACL
e legal hold/retention.

## Aceite

- [ ] Share para ao parar/revogar.
- [ ] Recording nunca inicia sem consent flow.
- [ ] URL expira e respeita ACL.
- [ ] Lifecycle/quota/hold funcionam.
- [ ] Evento/audit identifica actor.

## Testes

Browser permission, consent state machine, provider fake, storage security,
retention/hold e E2E multi-user.

## Riscos

Privacidade e alto storage. Default off, consentimento inconfundível, quotas e
retention curta configurável.

