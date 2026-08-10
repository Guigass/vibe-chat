# B-158 — Servidores MCP e grants de tools para bots

> Wave W18-4 · Trilha B/C/D/E/AI · Deps: B-066, B-111, B-127, B-139, B-155 · Decisões: D-18, D-22 · Risco R3
> Requisitos comuns: [Waves 11–18](long-term-common.md)

## Problema

Um bot como o assistente de ERP precisa consultar ou agir em sistemas internos,
mas acesso genérico transforma o modelo em confused deputy e esconde
credenciais/efeitos atrás de linguagem natural.

## Escopo

- Registro tenant/workspace-scoped de MCP server remoto por Streamable HTTP.
- Negociação de versão/capabilities e inventário `tools/list`.
- Auth por secret reference ou OAuth compatível; TLS, audience/resource binding,
  rotação, revogação e status.
- Egress allowlist, SSRF protections, timeout, circuit breaker e health.
- Catálogo observado de tools com schema/hash/version; schema drift bloqueia
  uso até revisão.
- Grants por bot + tool + operação + canais/usuários/classificação.
- Classificação local `read`, `write-reversible`, `write-high-impact` e
  `external-communication`.
- Validação de input/output schema, limites de payload e conteúdo não confiável.
- Preview/teste sintético, enable/disable e revogação imediata.
- Audit de discovery, grant, revoke, call, resultado e aprovação sem secrets.

## Fora de escopo

- Workspace admin fornecer comando `stdio` para a API/Worker executar.
- Código/DLL/JS de MCP server dentro do VibeChat.
- Token passthrough para ERP/API downstream.
- Grant amplo `*` ou confiança automática em annotations/descrições do servidor.
- Escrita de alto impacto sem B-160 e confirmação humana.

## Contratos

Entidades `integrations.mcp_servers`, `integrations.mcp_auth_references`,
`integrations.mcp_tools`, `integrations.mcp_tool_versions` e
`integrations.bot_mcp_grants`. Endpoints admin `/api/v1/admin/integrations/mcp*`.
Manifesto/export nunca contém secret. Tool key é `(serverId, toolName,
schemaHash)`; mudança exige reaprovação.

Seguir a revisão MCP suportada e registrar `protocolVersion`. HTTP protegido
usa token destinado ao MCP server; credencial do ERP pertence ao servidor MCP,
não é repassada pelo VibeChat.

## UX

Admin conecta, autentica, testa e revisa tools antes de conceder ao bot. Mostrar
servidor, tool, schema diff, classe de impacto, scopes, última chamada, quota e
revogação. Usuário vê quando uma tool será/usada e a aprovação requerida.

## Multi-tenant e authZ

`integration.mcp.manage` registra servidor; `ai.bot.manage` concede somente
tools que o admin pode delegar. Server/grant/tool são tenant-scoped. Chamada usa
interseção do solicitante, bot, tool, resource e policy atual.

## Aceite

- [ ] Bot consulta tool read-only do ERP apenas no scope concedido.
- [ ] Tool nova ou schema alterado fica negado até aprovação.
- [ ] Token revogado/audience incorreta falha fechado e não vaza.
- [ ] Tool fora do grant, servidor desativado ou cross-tenant não executa.
- [ ] Resultado inválido/muito grande é rejeitado/quarentenado.
- [ ] Nenhum subprocesso arbitrário é lançado por configuração de workspace.

## Testes

Fake MCP servers; lifecycle/version negotiation; OAuth/audience/no passthrough;
schema drift; HMAC/OAuth secret redaction; SSRF/DNS rebinding; timeout/retry;
tool scope/cross-tenant; malicious result/prompt injection; E2E connect→grant→call.

## Riscos

Confused deputy, exfiltração, prompt injection por tool, token theft e ação
irreversível. Mitigar com grants mínimos, audience binding, egress, schema pin,
classificação local, aprovação, audit e deny-by-default.

