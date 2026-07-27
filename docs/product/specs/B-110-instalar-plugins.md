# B-110 — Instalar e gerir plugins na instância

> Wave W10-14 · Trilha B/C/D · Deps: B-109 · Decisões: D-11 (sem loja; catálogo só local)

## Problema

B-109 entrega o **núcleo** (Bot + token + send), mas a UX ainda é “criar
integração crua”. Falta o conceito de **plugin instalável**: manifesto, enable/
disable, listagem no admin e built-in que só encapsula a API de envio — tudo
**na instância**, sem marketplace.

## Escopo

- Modelo **Plugin** por workspace:
  - `pluginId` (slug estável), `name`, `version`, `manifest` (JSON)
  - `capabilities[]` — na fatia 1 só `messages.send` (outras = B-066 depois)
  - vínculo 1:1 com identidade Bot + token de B-109
  - escopos de canal/DM (reusa regras B-109)
  - `enabled`, `installedAt`, `updatedAt`
- **CRUD admin** (`workspace.admin`):
  - listar instalados
  - instalar a partir de manifesto colado/upload JSON **ou** built-in do repo
  - enable/disable; desinstalar (revoga token)
  - rotacionar credencial (delega a B-109)
- **Built-in inicial:** “Incoming Messages API” — manifesto oficial que só
  ativa `messages.send` (wrapper de B-109)
- Plugin = **configuração + identidade**, não carrega código de terceiro no
  processo
- UI no admin (slot B-106): área Plugins / Integrações

## Fora de escopo

- Capabilities avançadas (slash, events, UI hooks) — **B-066** (P3, por último)
- Registry remoto / loja / OAuth app directory (D-11)
- Executar script/DLL do plugin no servidor
- Alterar o pipeline de SendMessage além do que B-109 já define

## Contratos

- Tabelas sugeridas: `integrations.plugins` (+ FK para bot/token de B-109)
- Manifesto mínimo:

```json
{
  "schema": "vibechat.plugin.manifest.v1",
  "id": "incoming-messages",
  "name": "Incoming Messages API",
  "version": "1.0.0",
  "capabilities": ["messages.send"]
}
```

- Endpoints admin: `GET/POST/PATCH/DELETE /api/v1/admin/plugins` (nomes finais
  em `contratos.md` no PR de implementação)
- `contratos.md` + glossário; RLS por `TenantId`

## UX

- Lista com nome, version, capabilities chips, toggle enabled
- Instalar built-in com um clique; “manifesto custom” como avançado
- Token mostrado só no fluxo de credencial (B-109)
- Só `workspace.admin` (matriz B-106)

## Multi-tenant e authZ

- Plugins isolados por tenant/workspace
- Desinstalar revoga token na mesma transação
- Auditor/Member não veem a área

## Aceite

- [ ] Instalar built-in “Incoming Messages” cria bot+token usable (B-109)
- [ ] Disable impede novos sends (401/403) sem apagar histórico
- [ ] Uninstall revoga token; manifesto custom inválido → 400
- [ ] Capability desconhecida na fatia 1 → rejeitada ou ignorada com aviso
  (documentar uma das duas; preferir rejeitar)
- [ ] Sem UI de loja remota

## Testes

- Integration: install → send → disable → send falha → uninstall
- Security: Member 403; cross-tenant plugin id
- Unit (web): lista/filtros; empty state

## Riscos

- Duplicar CRUD de “integração” e “plugin” → B-109 vira API/núcleo; B-110 é a
  fachada de produto; evitar duas UIs paralelas no admin
- Manifesto gigante → validar schema estrito v1
