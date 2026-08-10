# B-166 — Grupos na lista de contatos

> Wave 11 · Trilha B/D · Deps: B-021 · Risco R1
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

A lista de pessoas do workspace é plana. Em empresas, contatos precisam aparecer
agrupados por departamento (Vendas, Estoque, TI) e cada usuário também quer
organizar favoritos ou conjuntos próprios — sem isso virar permissão de canal.

## Escopo

- **Contact group** com dois kinds:
  - `department` — departamento do workspace (ex.: Vendas, Estoque, TI); criado
    e gerido por `workspace.admin`; visível a todos os membros do workspace.
  - `personal` — grupo pessoal; criado pelo membro; visível só ao dono.
- CRUD de grupos e atribuição/remoção de membros do workspace a grupos.
- Um membro pode pertencer a vários departamentos; grupos pessoais são
  independentes.
- Listagem da lista de contatos já agrupada (seções por grupo + “sem grupo”).
- UI no seletor/lista de pessoas (DM, menções e diretório conforme superfícies
  atuais), com criação de grupos pessoais pelo próprio usuário e gestão de
  departamentos na área admin de membros.

## Fora de escopo

- Alterar ACL de canal, DM ou workspace (grupo de contatos **não** autoriza).
- `SpaceMembership` ou reutilizar Space como membership de pessoas (Space
  continua agrupando **canais** — B-020).
- DM em grupo (B-101).
- Sincronização SCIM / grupos IdP (B-128); mapeamento futuro pode consumir
  departamentos, mas não entra nesta fatia.
- Papéis ou quotas por departamento.
- Guest fora do diretório do workspace (regra atual de guest permanece).

## Contratos

Entidades tenant-aware com `TenantId` + `WorkspaceId`, RLS e FORCE RLS.

- `ContactGroup`: `id`, `tenantId`, `workspaceId`, `kind` (`department` |
  `personal`), `name`, `order`, `ownerUserId?` (obrigatório se `personal`;
  nulo se `department`), timestamps.
- `ContactGroupMember`: `groupId`, `userId` (membro do workspace), unique
  `(groupId, userId)`.

Endpoints sob `/api/v1/workspaces/{workspaceId}/`:

| Método | Path | Notas |
|--------|------|--------|
| `GET` | `contact-groups` | Departamentos + grupos pessoais do caller |
| `POST` | `contact-groups` | Body `{ name, kind, order? }`; `department` exige admin |
| `PATCH` | `contact-groups/{groupId}` | Renomear / reordenar; authZ por kind |
| `DELETE` | `contact-groups/{groupId}` | Idem |
| `PUT` | `contact-groups/{groupId}/members` | Body `{ userIds: Guid[] }` (replace set) |
| `GET` | `contacts?grouped=true` | Seções `{ groupId?, name?, kind?, members[] }` + ungrouped |

Mutations aceitam Idempotency-Key quando retry puder duplicar. Audit em create /
delete / replace de membros de departamento. Nomes com limite de tamanho;
unicidade de nome por (`workspaceId`, `kind`, `ownerUserId` efetivo).

## UX

- Lista de contatos com seções colapsáveis: departamentos (ordem admin), grupos
  pessoais do usuário, depois “Sem grupo”.
- Ações rápidas: criar/renomear/apagar grupo pessoal; adicionar/remover pessoas.
- Admin: CRUD de departamentos e atribuição em massa na área de membros.
- Empty states claros; guest não vê o diretório (inalterado).
- Labels de UI podem dizer “Departamento”; termo canônico em código/API é
  `ContactGroup` / `department`.

## Multi-tenant e authZ

- Tenant só do contexto autenticado; membership de workspace obrigatória para
  ler a lista.
- `department`: leitura para membros; escrita só `workspace.admin`.
- `personal`: leitura/escrita só do `ownerUserId`; outro usuário → 404/403
  conforme política de não-vazamento.
- Atribuição só a `userId` com membership válida no mesmo workspace.
- Grupo de contatos **nunca** concede `channel.*`, DM ou papel de workspace.
- Teste cross-tenant obrigatório em tabelas novas.

## Aceite

- [ ] Admin cria departamentos Vendas/Estoque/TI e atribui membros; todos os
      membros veem as seções.
- [ ] Membro cria grupo pessoal e só ele o vê.
- [ ] Membro em vários departamentos aparece em todas as seções correspondentes.
- [ ] Remover membership do workspace remove o usuário das atribuições de grupo.
- [ ] Grupo de contatos não altera acesso a canais/DMs.
- [ ] Cross-tenant e leitura de grupo pessoal alheio falham com 403/404.
- [ ] Distinto de Space, B-101 e B-128 na UX/docs.

## Testes

- Unit: regras de kind, unicidade, ownership.
- Integração: CRUD + replace members + listagem agrupada; RLS/cross-tenant.
- Security: member não cria/edita departamento; user A não lê grupo pessoal de B.
- E2E leve: seções na lista de contatos / seletor de DM.

## Riscos

Confundir ContactGroup com Space ou com membership de autorização. Mitigar com
glossário, contratos explícitos e testes de “grupo não autoriza canal”. Evitar
escopo creep para SCIM (B-128) nesta fatia.
