# B-169 — Modo de auditoria de conteúdo por tenant

> Wave 14 · Trilha B/D/E · Deps: B-023, B-042, B-067, B-046, B-069, B-107, D-26 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Auditoria de conversas (B-067) e canais E2EE (B-064) são incompatíveis no mesmo
eixo: um tenant corporativo precisa escolher entre compliance server-side
(estilo Openfire) e confidencialidade forte (estilo WhatsApp). Hoje o produto
assume plaintext legível por auditores sem um gate explícito que bloqueie E2EE.
Além disso, o evento `message.delete` no audit log não carrega o body — após
purge (B-047) o conteúdo apagado some mesmo em tenants que precisam de
compliance.

## Escopo

- Setting de tenant/workspace `contentAuditEnabled` (default `true`).
- Modo compliance (`true`): auditores (B-067) e export de body (B-046) leem
  plaintext; criação de canal `ConfidentialE2EE` (B-064) é bloqueada.
- Modo confidencial (`false`): B-067 / export privilegiado **não** expõem body
  (403 ou redacted); E2EE fica permitido (opt-in por canal, B-064).
- Soft-delete (B-023 / ADR-018) + políticas de quem pode apagar (B-107):
  - Com `contentAuditEnabled=true`: ao soft-delete, o evento `message.delete`
    em `audit.audit_events` **inclui snapshot** do conteúdo apagado no
    `metadataJson` (sobrevive a hard-delete/purge).
  - Com `contentAuditEnabled=false`: `message.delete` grava só ids/timestamps
    (sem body).
- Soft-delete na tabela `messages` continua preservando body até purge; B-067
  lê esse body apenas com auditoria ligada.
- UI admin com escolha clara “compliance vs confidencial”, warnings e impacto.
- Mudança de setting gera audit `settings.content_audit.change` (sem body de
  mensagem no metadata).
- Troca `true → false` recusada se houver legal hold ativo (B-129) que dependa
  de body.
- Troca `false → true` **não** descriptografa histórico E2EE; canais
  confidenciais existentes permanecem ciphertext / read-only até política de
  migração (fora do v1).
- Metadata mínima (ids, seq, membership, timestamps) continua auditável nos
  dois modos.

## Fora de escopo

- Protocolo crypto, device keys ou MLS (B-064).
- Escrow organizacional de chaves.
- Mensagens efêmeras / “não salvar nada”.
- Store imutável separado de mensagens (B-129 eDiscovery).
- Streaming de conteúdo de mensagem para SIEM (B-132 cobre eventos de audit).
- Janela/papéis de quem pode apagar — [B-107](B-107-politicas-edicao-mensagem.md).

## Contratos

- Flag tipada em admin settings (B-069 / ADR-020): `contentAuditEnabled: bool`.
- Enforcement server-side em: viewer B-067, export de body, gate de criação de
  `ConfidentialE2EE`, e shape do metadata de `message.delete`.
- Código de erro estável ao bloquear E2EE com auditoria ligada (ex.
  `ContentAuditBlocksE2ee`).
- Snapshot planejado em `message.delete` quando `contentAuditEnabled=true`
  (`metadataJson`):

  | Campo | Tipo | Notas |
  |-------|------|-------|
  | `channelId` | uuid | Canal pai |
  | `threadId` | uuid? | Se reply/thread |
  | `sequence` | long | `seq` da mensagem |
  | `authorId` | uuid | Autor original |
  | `body` | string | Conteúdo no momento do soft-delete |

  Com `contentAuditEnabled=false`: mesmos ids/`sequence` **sem** `body` (e sem
  outros campos de plaintext).
- Eventos de audit que **não** são snapshot de delete (ex.
  `settings.content_audit.change`) continuam sem body de mensagem no metadata.

## UX

- Toggle/settings com rótulos em pt-BR/en e texto de impacto (busca, IA, legal
  hold, auditores, E2EE, preservação de conteúdo apagado no audit).
- Confirmação forte ao desligar auditoria.
- Indicador persistente no admin quando o tenant está em modo confidencial.

## Multi-tenant e authZ

- Somente `workspace.admin` altera o setting (Auditor com só `admin.dashboard`
  → 403, paridade B-069).
- Setting é por tenant/workspace do actor; nunca cross-tenant.
- Membership e RLS inalterados; o gate só restringe leitura privilegiada de body,
  criação E2EE e inclusão de body no metadata de delete.
- Feed `GET /admin/audit-events` permanece tenant-scoped; body no metadata só
  aparece para o próprio tenant com auditoria ligada.

## Aceite

- [ ] Default `contentAuditEnabled=true` preserva B-067/B-046 atuais.
- [ ] Com `true`, criar `ConfidentialE2EE` → 400/403 com código estável.
- [ ] Com `false`, auditor não lê body via B-067; metadata permanece.
- [ ] Com `false`, export privilegiado redige ou omite body.
- [ ] Com `true`, soft-delete gera `message.delete` com `body` no `metadataJson`.
- [ ] Com `false`, soft-delete gera `message.delete` **sem** `body`.
- [ ] Após purge da linha em `messages`, o evento `message.delete` ainda contém
      o body se a flag era `true` no momento do delete.
- [ ] `true → false` bloqueado sob legal hold de body; mudança auditada.
- [ ] `false → true` não revela ciphertext antigo.
- [ ] Cross-tenant: setting de A não afeta B.

## Testes

Matrix modo × papel (Member / Auditor / Admin); gate E2EE; export; legal hold
race; regressão B-067 com default; soft-delete × `contentAuditEnabled` (com e
sem body no audit); após purge simulado, audit ainda tem body se flag era
`true`; teste cross-tenant do setting.

## Riscos

Admin desliga auditoria esperando apagar histórico legível — o plaintext já
persistido permanece até purge/retenção; snapshots de `message.delete` gerados
enquanto a flag era `true` também permanecem. Warning obrigatório na UI.
Conflito com DLP/IA server-side em modo confidencial: capacidades reduzidas já
cobertas por D-26 / B-064.
