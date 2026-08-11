# B-064 — Canais confidenciais E2EE

> Wave 16 · Trilha B/C/D/E · Deps: B-129, B-169, D-26 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Algumas conversas exigem que o servidor não consiga ler o body, mesmo ao custo
de busca, IA e compliance server-side reduzidos.

## Escopo

- Channel mode `ConfidentialE2EE`, opt-in e off default.
- Criação só permitida quando o tenant está em modo confidencial
  (`contentAuditEnabled=false`, B-169); com auditoria de conteúdo ligada →
  bloqueio com código estável (`ContentAuditBlocksE2ee`).
- Device keys, session verification e group key rotation.
- Encrypt body/attachments no client; servidor guarda ciphertext/metadata mínima.
- Adicionar/remover membro rotaciona chaves para eventos futuros.
- Backup/export de chaves pelo usuário com warning; sem escrow no v1.
- Capability matrix visível.

## Fora de escopo

- Converter canal existente silenciosamente.
- Server search/RAG/DLP/body moderation/legal hold de conteúdo.
- Recuperar chave perdida.
- Metadata-hiding completo.

## Contratos

ADR de protocolo/crypto obrigatório; usar biblioteca/protocolo auditado, nunca
criptografia própria. MLS deve ser avaliado como candidato padrão para grupos,
sem inventar protocolo se a implementação/ecossistema não atender os gates.
Envelope ciphertext versionado; outbox não contém plaintext.

## UX

Verification state, warnings de capability, recovery flow e indicadores
persistentes. Composer bloqueia client incompatível.

## Multi-tenant e authZ

Membership server-side continua; possession de key não substitui membership.
Dispositivo revogado deixa de receber chaves futuras.

## Aceite

- [ ] Servidor/log/outbox não recebem plaintext.
- [ ] Novo membro não lê histórico por default.
- [ ] Removido não lê futuro.
- [ ] Client incompatível falha fechado.
- [ ] Capability reduzida é clara antes de criar.
- [ ] Com `contentAuditEnabled=true` (B-169), criação de `ConfidentialE2EE` é bloqueada.

## Testes

Cryptographic test vectors, multi-device membership rotation, compromise/revoke,
ciphertext leakage scan e E2E two clients.

## Riscos

Crypto failure e perda de dados. Protocolo auditado, test vectors, no custom
crypto e rollout experimental.

