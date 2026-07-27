# B-140 — White-label governado

> Wave 15 · Trilha D/G/B · Deps: B-100, D-24 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Instâncias precisam refletir identidade da organização sem permitir CSS/JS que
quebre segurança, acessibilidade ou suporte.

## Escopo

- Nome exibido, logos light/dark, favicon e cores brand/accent.
- Validação de formato, tamanho, contraste e safe area.
- Preview e rollback.
- Branding por tenant no login autorizado e shell.
- About/admin preserva versão/origem VibeChat.
- Cache/versioning dos assets.

## Fora de escopo

- CSS/JS/HTML arbitrário.
- Remover avisos de segurança/licença.
- Branding por usuário.

## Contratos

BrandSettings tenant-scoped; upload via Files com tipo dedicado e scan; endpoint
público mínimo resolve branding pelo host/tenant sem vazar config.

## UX

Editor de tokens limitado, preview responsivo e fallback para assets oficiais.
Contraste reprovado não salva.

## Multi-tenant e authZ

Somente brand/admin scope. Host→tenant mapping é operator-controlled; nunca
aceitar TenantId do query para tela pública.

## Aceite

- [ ] Branding aparece só no tenant correto.
- [ ] Contraste e arquivos inválidos são rejeitados.
- [ ] Fallback funciona em asset ausente.
- [ ] Sem arbitrary code.
- [ ] About preserva identificação técnica.

## Testes

Visual regression light/dark/responsive, host mapping, upload security,
cross-tenant e a11y.

## Riscos

Phishing cross-tenant e contraste ruim. Host allowlist, fallback e validação
server-side.

