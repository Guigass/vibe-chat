# B-167 — Perfil público do membro

> Wave 11 · Trilha B/D · Deps: B-021 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

O membro só existe como `DisplayName` + iniciais. Colegas não veem cargo, foto,
apresentação nem mensagem de destaque — e o próprio usuário não tem onde
preencher esses dados de forma pública no workspace.

## Escopo

- Estender o perfil local (`UserProfile`) com campos públicos editáveis pelo
  dono:
  - **Avatar** — imagem (storage MinIO já usado no produto; UI reusa
    `vc-avatar`); fallback nas iniciais se ausente.
  - **Cargo / função** (`jobTitle`) — título curto.
  - **Observação / sobre** (`about`) — texto curto de apresentação.
  - **Mensagem de destaque** (`highlightMessage`) — tagline permanente
    (distinta do status temporário de B-116).
  - **DisplayName** — já existe; editável no mesmo fluxo.
- Ficha **pública no workspace**: membros autorizados do mesmo workspace leem
  a ficha de outro membro; não é exposição na internet aberta.
- Endpoints de leitura (ficha) e escrita (próprio perfil) + upload de avatar.
- UX: drawer/tela **Meu perfil** (edição) e abrir ficha ao clicar em membro
  (lista de contatos, menção, header de DM). Quando B-166 estiver Done, a lista
  agrupada é entrada natural; não bloqueia esta fatia.
- Superfícies existentes (lista, bolha, menção) passam a consumir avatar/nome
  atualizados; sem hub SignalR dedicado nesta fatia (invalidate/refetch
  suficiente).

## Fora de escopo

- Status personalizado com expiração / disponibilidade (B-116).
- Preferências pessoais (`locale` B-100, DND B-097, read receipts B-094).
- SCIM / sync IdP de atributos (B-128).
- Diretório de expertise / skills tags (follow-up Later).
- Guest fora do diretório do workspace (regra atual permanece).
- Alterar ACL de canal, DM ou papéis de workspace.
- Self-signup ou perfil público fora do tenant/workspace.

## Contratos

Estender `UserProfile` (tenant-aware, RLS / FORCE RLS) com campos opcionais:

| Campo | Tipo | Limite sugerido |
|-------|------|-----------------|
| `jobTitle` | string? | curto (ex. 80) |
| `about` | string? | curto (ex. 500) |
| `highlightMessage` | string? | curto (ex. 160) |
| `avatarObjectKey` / URL derivada | string? | key MinIO |

`DisplayName` permanece; e-mail pode aparecer na ficha conforme política atual
de membros (sem novo vazamento).

Endpoints:

| Método | Path | Notas |
|--------|------|--------|
| `GET` | `/api/v1/workspaces/{workspaceId}/members/{userId}/profile` | Ficha pública; exige membership no workspace |
| `PUT` | `/api/v1/me/profile` | Atualiza próprios campos; Idempotency-Key em retry |
| `POST` | `/api/v1/me/profile/avatar` (ou fluxo de upload alinhado a Files) | Upload imagem; valida tipo/tamanho; grava key no perfil |
| `DELETE` | `/api/v1/me/profile/avatar` | Remove avatar; volta às iniciais |

Resposta da ficha: `userId`, `displayName`, `jobTitle`, `about`,
`highlightMessage`, `avatarUrl?` (e campos já expostos de membro se fizer
sentido). Mutações do próprio perfil com audit leve (`profile.update` /
`profile.avatar`). Atualizar [`contratos.md`](../../architecture/contratos.md)
no PR de implementação.

## UX

- **Meu perfil**: formulário simples (nome, cargo, sobre, destaque) + troca de
  foto; preview do avatar.
- **Ficha de outro membro**: somente leitura; mesmos campos; CTA opcional para
  DM se a superfície já existir.
- Empty states: campos vazios omitidos ou “Não informado”; sem cards
  desnecessários — drawer alinhado ao design system.
- Acessível: labels, foco no drawer, alt no avatar.

## Multi-tenant e authZ

- Leitura só com membership válida no mesmo `workspaceId` / tenant.
- Escrita só do próprio usuário (`/me`); admin não edita perfil alheio nesta
  fatia (pode limpar abuso em fatia futura / B-116 clear).
- Cross-tenant e membro de outro workspace → 403/404.
- Avatar: MIME allowlist, tamanho máximo, sem path traversal; object key
  namespaced por tenant.
- Conteúdo do perfil é PII controlada pelo usuário; não enviar a provedores de
  IA sem opt-in existente.

## Aceite

- [ ] Usuário preenche cargo, sobre, destaque e avatar; outros membros do
      mesmo workspace veem na ficha.
- [ ] Sem avatar, UI continua com iniciais.
- [ ] Membro de outro workspace/tenant não lê a ficha.
- [ ] Guest sem diretório não ganha nova superfície de perfil.
- [ ] Distinto de B-116 (status temporário), B-166 (grupos) e preferências
      B-094/B-097/B-100.
- [ ] `contratos.md` + migration/RLS atualizados no PR de código.
- [ ] Cross-tenant e upload inválido falham com 4xx.

## Testes

- Unit: limites de tamanho, normalização de campos.
- Integração: PUT/GET perfil, upload/delete avatar, RLS/cross-tenant.
- Security: user A não altera perfil de B; outsider não lê ficha.
- E2E leve: editar meu perfil → abrir ficha como outro membro e ver dados.

## Riscos

PII e abuso em texto/imagem. Mitigar com limites, allowlist de MIME, authZ de
workspace e distinção clara status (B-116) vs perfil permanente. Upload reutiliza
padrão Files/MinIO — não inventar storage paralelo.
