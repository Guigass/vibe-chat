# B-147 — Huddles de áudio/vídeo

> Wave 17 · Trilha B/C/D/A/E · Deps: B-146, D-19 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Times precisam resolver assuntos síncronos no contexto do channel/DM sem
depender obrigatoriamente de serviço fechado.

## Escopo

- LiveSession vinculada a channel/DM.
- Provider interface e stack SFU/TURN OSS em profile separado.
- Join/leave, mute, camera, participant list e reactions.
- Token efêmero scoped e server-side authZ.
- Quotas, capacity, health e graceful degradation.
- Chat permanece funcional com live indisponível.

## Fora de escopo

- Gravação/transcrição, reservadas a B-148/B-149.
- PSTN.
- Rodar mídia dentro de `apps/api`.

## Contratos

ADR compara stacks OSS, licença, WebRTC, SFU/TURN, operação e client support.
API cria/join token curto; eventos session state sem payload de mídia.

## UX

Start/join no header, preflight de device, indicators de consentimento e
fallback claro. Permissões só são pedidas ao entrar.

## Multi-tenant e authZ

Join revalida membership; room namespace por tenant/conversation. Provider token
não permite room arbitrária.

## Aceite

- [ ] Dois+ clients conectam com áudio.
- [ ] Vídeo é opcional e pode ser desabilitado por policy.
- [ ] Outsider/token replay não entra.
- [ ] Falha do provider não derruba chat.
- [ ] Capacity/health/metrics estão visíveis.

## Testes

Provider fake, token/replay, two-browser media smoke, network degradation,
capacity e cross-tenant.

## Riscos

Custo/abuso e novo serviço. Off default, quotas, ADR, profile isolado e SLO
separado.

