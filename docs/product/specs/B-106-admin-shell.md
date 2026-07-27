# B-106 — Admin shell (navegação, listagens, filtros e visibilidade por papel)

> Wave W7-8 · Trilha D/G · Deps: B-104 · Decisões: D-15 · Risco R2

## Problema

Hoje `/admin` é uma página monolítica: seções empilhadas, sem navegação lateral, toolbar
fraca e listagens sem filtros/paginação de respeito. O B-104 só **remove o PrimeNG** com
paridade funcional — deixa o admin utilizável, mas não o transforma num console
administrativo claro. Depois da saída do kit comercial, falta um shell próprio alinhado
aos tokens `--vc-*` e ao design system.

Além disso, seções que o usuário **não pode** usar ainda aparecem com aviso
laranja/vermelho (“Sem permissão…”) — UX-005. Auditor vê settings/export como se
fossem erro; o correto é **não renderizar** o que o papel não enxerga.

## Escopo

- **Shell de admin** com:
  - menu lateral (nav) por área: overview, membros, diretivas/papéis, conversas
    (auditoria), audit log, settings (retenção/webhooks; slot para política de
    edição B-107), **plugins/integrações** (B-109/B-110), export
  - toolbar superior: título da área, ações primárias, busca/filtro contextual
  - rota filha por área (`/admin/...`) com estado de navegação óbvio (item ativo)
- **Visibilidade por permissão (obrigatório):**
  - Nav, ações de toolbar e blocos de conteúdo só entram no DOM se o ator tiver
    a claim exigida — **esconder**, nunca mostrar card/aviso de “sem permissão”
  - Deep-link para área sem claim → redirect para a primeira área permitida (ou
    empty state neutro “Nada a administrar neste perfil”), sem banner de erro
  - API continua sendo a fonte da verdade (403); UI não substitui authZ, só
    evita ruído
- **Listagens densas** (tabela semântica + CSS tokens): cabeçalho sticky, densidade
  confortável, empty/loading/error states, ordenação simples onde já houver dado
- **Filtros bons** por listagem (texto + chips/selects nativos próprios):
  - membros: papel, status (ativo/pending), busca por nome/e-mail
  - audit log: ação, intervalo de data
  - conversas (auditoria): tipo (canal/DM), workspace, busca por nome
- Paginação ou “carregar mais” quando a API já limitar (`limit`); não inventar
  virtual scroll sem medição
- Reusar shared UI existente (`Badge`, inputs, botões) + CDK (a11y, overlay se
  necessário); **sem** nova lib de UI de terceiros
- Preservar authZ de API atual (`workspace.admin`, `admin.dashboard`, etc.)

### Matriz de visibilidade (UI)

| Área / ação | Claim | Admin (`workspace.admin`) | Auditor (`admin.dashboard` sem `workspace.admin`) | Member |
|-------------|-------|---------------------------|---------------------------------------------------|--------|
| Overview / dashboard | `admin.dashboard` | sim | sim | não (sem `/admin`) |
| Membros (listar) | `admin.dashboard` ou `workspace.admin` | sim | sim (leitura) | não |
| Convidar / alterar papel | `workspace.admin` | sim | **não** | não |
| Audit log | `admin.dashboard` | sim | sim | não |
| Auditoria de conversas | `admin.dashboard` | sim | sim | não |
| Settings (retenção, webhooks, edit policy) | `workspace.admin` | sim | **não** | não |
| Plugins / integrações | `workspace.admin` | sim | **não** | não |
| Export ZIP | `workspace.admin` | sim | **não** | não |

Se o catálogo de roles mudar, atualizar esta matriz no mesmo PR. Guest nunca
entra em `/admin`.

## Fora de escopo

- Remoção do PrimeNG (é **B-104** — pré-requisito; este item assume pacote já fora)
- Novos endpoints de negócio, papéis ou policies (exceto query params mínimos de filtro)
- Redesign do shell de chat
- Dashboard analítico rico / gráficos (overview continua métricas já existentes)
- Filtros server-side avançados que a API ainda não expõe — se faltar query param
  mínimo, documentar no mesmo PR em `contratos.md` (mudança pequena e justificada)
- Inventar grupos além dos papéis do `RolePermissionCatalog`

## Contratos

Preferência: **sem** mudança de API. Se um filtro exigir query param novo (ex.
`?role=&q=` em membros), atualizar `docs/architecture/contratos.md` no mesmo PR.
Sem novos eventos de hub. Client precisa das permissões efetivas do ator (já
disponíveis via membership/role ou endpoint existente) para montar a nav.

## UX

- Primeira viewport do admin lê como **um console**: nav lateral + conteúdo, não
  uma pilha de cards
- Item ativo da nav com contraste de marca; foco de teclado visível
- Toolbar com uma ação primária por área **somente se** a ação for permitida
  (ex.: “Convidar membro” só com `workspace.admin`)
- Listagens: zebra/hover sutil via tokens; tags de papel/status consistentes
- Light/dark via `data-theme`; tipografia Sora (títulos) + IBM Plex Sans (corpo)
- Sem look de kit comercial; sem clonar Slack/Discord admin
- Fecha **UX-005**: zero avisos “Sem permissão…” em laranja/vermelho para estado
  esperado do papel

## Multi-tenant e authZ

Policies de API **não mudam**. UI reflete a matriz acima. Testes negativos
existentes (T7/T10/T12/T14…) continuam válidos — esconder na UI não relaxa o
servidor.

## Aceite

- [ ] Menu lateral cobre só as áreas que o ator pode usar; navegação por teclado ok
- [ ] Auditor **não vê** Settings, Export nem ações de convite/role na nav/toolbar
- [ ] Admin vê todas as áreas; Member não acessa `/admin` (ou redirect neutro)
- [ ] Deep-link `/admin/settings` como Auditor → sem formulário e sem banner de erro
- [ ] Toolbar contextual por área com título + ação primária quando couber **e**
      for autorizada
- [ ] Listagens de membros, audit e conversas têm busca/filtro útil e empty state
- [ ] Troca de área não perde o workspace selecionado (se aplicável)
- [ ] DevAuth alice (admin): convite, role change, settings, export e auditoria ok
- [ ] UX-005 fechado
- [ ] Tokens `--vc-*`; sem dependência de UI comercial

## Testes

- Unit/component: nav filtra por claims; filtros locais; empty states
- E2E: sessão Auditor — nav sem Settings/Export; sessão Admin — áreas completas
- Security: regressão T7/T10 (sem bypass por rota nova); UI hide ≠ API allow

## Riscos

- Escopo creep para “recriar todo o admin do zero” → partir das seções já
  existentes; só reorganizar + polish de listagem/filtro + hide por claim
- Inventar DataTable genérico enorme → tabela HTML + CSS tokens; extrair shared
  só o que repetir 2+ vezes
- Quebrar deep-links de docs/ops que apontam `/admin` → manter redirect de
  `/admin` → primeira área **permitida** ao ator
- Mostrar item desabilitado “por curiosidade” → **não**; omitir por completo
