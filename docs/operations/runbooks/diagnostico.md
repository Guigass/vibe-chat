# Diagnóstico e support bundle (B-154)

Preflight em `/admin/diagnostics` e `GET /api/v1/workspaces/{workspaceId}/diagnostics`.
Feature desligada aparece como `Skipped` / `feature.off`, não como falha.

## Ler o veredito

| Veredito | Significado |
|----------|-------------|
| `ready` | Nenhum check `Fail` ou `Warn` |
| `degraded` | Há `Warn` (outbox acumulado ou worker parado com mensagem velha) |
| `action_required` | Há `Fail` (banco, Redis/storage configurado e indisponível, ou migration pendente) |

O código do check aponta o componente (`database`, `redis`, `storage`, `oidc`, `email`, `webpush`, `proxy`, `outbox`, `worker`, `migrations`). Admin de workspace não vê host. `PlatformOwner` vê endpoint público, sem senha.

## Probe sintético

`POST .../diagnostics/probes/email|push|storage` exige `support.repair`.

- E-mail ou push desligado → `Skipped`, sem envio.
- E-mail ou push ligado → resultado sintético, sem SMTP nem VAPID na resposta.
- Storage grava `diagnostics/probe-*` e apaga o objeto na mesma chamada.

## Support bundle

Exige `Features:SupportBundle:Enabled=true` e `support.bundle`. Fora isso a criação responde `404 SupportBundleDisabled`. O preflight continua.

1. `POST .../diagnostics/bundles` com `Idempotency-Key`.
2. Baixar `GET .../diagnostics/bundles/{id}` em até 15 minutos, no máximo 3 vezes.
3. Conferir `schema = vibechat.support-bundle.v1` e o checksum `sha256`.
4. Depois do TTL ou do terceiro download a resposta é `410 SupportBundleExpired`.

O manifesto não traz body, e-mail, token nem connection string. Não há dump de banco.

## Repair

Allowlist: `search.reindex` e `membership.reconcile`. Qualquer `target` responde `400 RepairActionNotAllowed`.

1. Dry-run (`dryRun: true`) só grava o job e a estimativa. Não altera `search_vector`.
2. Execução exige `confirm: true`, fica `running` e só escreve no `POST .../apply`.
3. `POST .../cancel` antes do apply não escreve a projeção.
4. `membership.reconcile` conta membros de canal sem membership de workspace e não apaga ninguém.

## Rollback

Não há migration de dados de negócio. Reverter o deploy deixa as tabelas `support.bundles` e `support.repair_jobs` órfãs e sem leitor. O down da migration `AddSupportDiagnostics` remove as duas tabelas. Bundles expiram sozinhos; não é preciso purga manual para cortar o download.
