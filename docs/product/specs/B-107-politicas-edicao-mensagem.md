# B-107 — Políticas configuráveis de edição (e apagar) de mensagens

> Wave W10-11 · Trilha B/C/D · Deps: B-023, B-069 · Decisões: — · Risco R3

## Problema

Edição (B-023) está **sempre ligada** para o autor com `message.edit.own`, sem
janela de tempo, sem override por papel e sem configuração no admin. Em empresa,
admins precisam limitar edição (ex.: 15 min), restringir a certos papéis ou
permitir que moderadores editem/apaguem além do autor. Hoje isso não existe —
só a permissão binária no checker.

## Escopo

Política por **workspace** (mesmo padrão de `retention.*` / B-047), editável só
por `workspace.admin` via `/admin/settings` (e refletida no client):

| Chave | Semântica | Default sugerido |
|-------|-----------|------------------|
| `messaging.edit.enabled` | Liga/desliga edição pelo autor | `true` |
| `messaging.edit.windowMinutes` | Limite após `createdAt` (`null` = sem limite) | `null` ou `60` |
| `messaging.edit.roles` | Papéis que **podem** editar as próprias (subconjunto de Member/Moderator/Admin/Auditor) | todos com `message.edit.own` |
| `messaging.edit.allowModeratorOverride` | Moderator/Admin com `message.edit.any` pode editar de outros **e** ignorar a janela | `false` |
| `messaging.delete.enabled` | Soft-delete pelo autor | `true` |
| `messaging.delete.windowMinutes` | Janela para apagar a própria | igual à de edit ou `null` |
| `messaging.delete.roles` | Papéis que podem apagar as próprias | espelha edit |
| `messaging.delete.allowModeratorOverride` | Moderação pode apagar fora da janela / de outros | `true` (comum em compliance) |

Comportamento:

- API rejeita fora da política com **403** ou **422** estável (`EditWindowExpired`,
  `EditDisabled`, `EditRoleDenied`, e equivalentes de delete) — documentar em
  `contratos.md`.
- UI esconde/desabilita “Editar” / “Apagar” conforme a política efetiva do ator
  (não só tentar e falhar).
- Soft-delete continua respeitando ADR-018 (body na linha; leituras normais
  redigem). Quem pode apagar é deste item; **preservar o conteúdo apagado no
  audit log** (snapshot em `message.delete` quando `contentAuditEnabled`) é
  obrigação de [B-169](B-169-modo-auditoria-conteudo-tenant.md) — esta política
  não desliga nem substitui esse snapshot.
- Indicador “(editada)” permanece; histórico de body anterior continua só na
  auditoria admin (B-067) — sem versionamento completo nesta fatia.
- Expor política efetiva ao client: incluir em settings mascarados ou endpoint
  `GET .../workspaces/{id}/messaging-policy` (só campos não secretos).

## Fora de escopo

- Grupos arbitrários além dos **papéis** já existentes (Member/Moderator/Admin/
  Auditor/Guest) — sem inventar “user groups” novos; se produto quiser depois,
  nova decisão.
- Editar anexos/reações; editar mensagem de outro sem override de moderação.
- Versionamento completo de body (diff/histórico para membros) além do snapshot
  de delete definido em B-169.
- Política por canal individual (só workspace nesta fatia).
- Gate `contentAuditEnabled` e shape do metadata de `message.delete` (B-169).

## Contratos

- Extender settings admin (B-069) com bloco `messaging.edit.*` / `messaging.delete.*`.
- Opcional: claim/permission `message.edit.any` / `message.delete.any` no
  `RolePermissionCatalog` para override de moderação.
- `PUT` de edit/delete valida janela + papel **no servidor** (nunca só no client).
- `contratos.md` + glossário; RLS inalterado (settings já por tenant).

## UX

- Admin → Settings: seção “Edição e exclusão de mensagens” com toggles, minutos
  e multi-select de papéis (componentes do B-106 quando existir; senão controles
  próprios mínimos).
- Na timeline: menu da mensagem respeita política; tooltip se a janela expirou
  (“Edição disponível por X min após o envio”).
- Atalho `↑` (B-099) só abre edição se a política permitir.

## Multi-tenant e authZ

- Só `workspace.admin` altera a política.
- Avaliação sempre no tenant do canal; cross-tenant → 403.
- Guest (B-040): respeita a mesma política do workspace no canal convidado;
  tipicamente só as próprias dentro da janela.

## Aceite

- [ ] Com janela 15 min, editar após 16 min → erro de contrato estável; UI sem ação
- [ ] Com `edit.enabled=false`, autor não edita; moderador só se override ligado
- [ ] Restringir a papéis Admin: Member vê menu sem Editar
- [ ] Soft-delete respeita janela/papéis espelhados
- [ ] Com delete permitido pela política e `contentAuditEnabled=true` (B-169), o
      soft-delete **não** impede o snapshot de body em `message.delete` no audit
- [ ] Settings mascarados: membro não lê/altera a política (403)
- [ ] Teste cross-tenant negativo

## Testes

- Integration: matriz enabled × window × role × override (edit e delete).
- Security: membro altera settings → 403; editar mensagem alheia sem override → 403.
- Unit (web): esconder ações conforme policy snapshot.
- E2E: admin define 1 min (ou clock testável); autor perde o botão após expirar.

## Riscos

- Clock skew client vs server → decisão só no servidor; UI usa `createdAt` + policy
  como hint.
- `windowMinutes=0` ambíguo → tratar como “desligado” ou rejeitar no PUT settings.
- Escopo creep para grupos custom → barrar; só papéis do catálogo.
