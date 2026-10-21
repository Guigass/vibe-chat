# ADR-047 — Administração delegada e quotas

- Status: Accepted
- Data: 2026-10-21
- Relacionado: B-134, B-128, D-23, ADR-009, ADR-010, ADR-046

## Contexto

`workspace.admin` não separa time, space, compliance, segurança e leitura de
capacidade. O pacote R3 pede delegação e quotas sem papel arbitrário e sem
bypass de RLS.

## Decisão

1. Flag `Features:DelegatedAdmin:Enabled` nasce **false**. Desligada, a API
   responde 404 `DelegatedAdminDisabled` e nenhum caminho de mensagem, canal,
   convite, anexo, automação ou IA consulta quota.
2. O catálogo de escopos é fixo: `tenant`, `workspace`, `space`, `compliance`,
   `security`, `billing-read`. Não há papel custom. `workspace.admin` continua
   no catálogo de roles e não ganha compliance nem security.
3. Quem concede é o WorkspaceOwner/PlatformOwner (raiz) ou alguém que já tem
   o escopo. A raiz concede qualquer escopo do catálogo, mas não passa a
   exercer compliance/security só por ser owner. Auto-atribuição é 403
   `DelegationSelfGrant`. Escopo acima do grantor é 403
   `DelegationScopeExceeded`. Space só alcança o mesmo `spaceId`.
4. Atribuição guarda motivo, expiração opcional e `Idempotency-Key`.
   Break-glass exige expiração de no máximo 4 horas, gera alerta na abertura
   e, ao expirar, a leitura revoga e grava `break-glass-expired`.
5. Quotas por workspace: `users`, `channels`, `storage` (bytes), `messages`,
   `automations` e `ai-budget` (uma unidade por conclusão de IA). Soft ≤ hard.
   O incremento é um `UPDATE` condicionado a `Used + delta <= HardLimit`.
   Sem linha de quota, o caminho segue. Se o contador falha, o chat continua
   (falha aberta, métrica `vibechat.quota.check_failed`). Estouro responde
   409 `QuotaHardLimit` e alerta `hard`. Soft cruzado alerta `soft`.
6. O uso devolve tendência (`up`/`down`/`flat`) e ação (`none`/`warning`/
   `blocked`). A UI em `/compliance` só lista as áreas do escopo do caller.
7. Tabelas `compliance.scoped_role_assignments`, `compliance.quotas`,
   `compliance.quota_counters` e `compliance.quota_alerts` têm `TenantId`,
   RLS FORCE e WITH CHECK.

## Alternativas

- Papel custom por tenant: a spec proíbe.
- Contar uso ao vivo sem contador: duas criações paralelas passam do hard limit.
- Falhar o chat quando o contador cai: derruba o caminho principal.

## Consequências

- Rollback da API: `Features:DelegatedAdmin:Enabled=false`. As linhas ficam.
  Convite, canal, mensagem, anexo, automação e IA deixam de consultar quota.
- A migration `AddDelegatedAdmin` cria as tabelas. Não há dado de produção
  para voltar atrás; dropar as tabelas só com o schema vazio.
- Não há billing nem checkout.
