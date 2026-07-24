# Segurança — VibeChat

## Reporte de vulnerabilidades

Se você descobrir uma falha de segurança, **não** abra uma issue pública com detalhes exploráveis.

1. Reporte de forma privada aos maintainers do repositório (GitHub Security Advisory / contato do owner).
2. Inclua: descrição, impacto, passos de reprodução, versão/commit, mitigação sugerida se houver.
3. Aguarde confirmação antes de divulgação pública.

## Escopo relevante

- Isolamento multi-tenant e RLS (`docs/security/multi-tenant.md`)
- Autenticação OIDC (Keycloak) e autorização por workspace/canal
- Hubs SignalR e uploads (MinIO)
- Secrets, tokens e PII em logs/traces
- Superfície de IA opcional (desligada/controlada por flag)

Modelo de ameaças: `docs/security/modelo-ameacas.md`.

## Práticas do projeto

- Credenciais de produção **nunca** no git (D-04)
- Dev/demo passwords apenas em `.env.example` com aviso explícito
- Bypass de RLS ou authZ “temporário” não é aceito em `main`
- Dependências: preferir auditoria (`dotnet list package --vulnerable` quando disponível) no CI

## Ambientes de desenvolvimento

Headers `X-Dev-User` (DevAuth) só funcionam com `ASPNETCORE_ENVIRONMENT=Development`. Não habilitar em produção.
