# AGENTS.md — VibeChat

Regras para Cloud Agents e agentes de código que trabalham neste repositório.
Complementa `docs/agents/orientacoes.md`.

Execução contínua e autoridade delegada: `docs/agents/autonomia.md` e
`docs/agents/operacao-24x7.md`. Decisões D-01…D-28 estão fechadas; escolhas
técnicas reversíveis são feitas pelo agente e registradas em ADR quando necessário.

## Antes de editar

1. Ler o núcleo: `docs/architecture/visao-geral.md`,
   `docs/architecture/contratos.md`, `docs/architecture/diagrama-modulos.md`,
   `docs/product/glossario.md`, `docs/roadmap/decisoes-pendentes.md` e
   `docs/product/bug-findings.md` (safety lane de bugs funcionais Alta).
2. Identificar os módulos afetados (`modules/*`, `apps/*`, `src/*`, `infra/*`,
   `tests/*`) e então ler somente os documentos de `docs/architecture/` e ADRs
   relevantes à superfície tocada.
3. Planejar mudanças grandes antes de codar (escopo, contratos, testes, docs).
4. Em automações/long-running work, seguir
   `docs/agents/loop-engineering.md`: um Work-Item, estado durável, verificação e
   stop reason explícito.

## Regras universais

- **Não alterar contratos compartilhados em silêncio** — mudanças em APIs públicas, eventos, schemas ou claims exigem atualização de `docs/architecture/contratos.md` e testes.
- **Não mudar arquitetura sem ADR** — novos serviços, buses, OpenSearch, K8s, etc. exigem ADR aprovado/documentado.
- **Rodar testes relevantes** da trilha tocada (`task test`, `task test:architecture`, etc.).
- **Adicionar testes para correções** — bugfix sem regressão coberta não está pronto.
- **Preservar isolamento multi-tenant** — `tenant_id`, authZ e RLS em todo caminho de dado de negócio.
- **RLS usa role runtime não privilegiada** — API/Worker não conectam como owner,
  superuser ou `BYPASSRLS`; tabelas tenant-aware usam `FORCE ROW LEVEL SECURITY`.
- **Sem dependências proprietárias** — preferir OSS; não adicionar SDKs fechados sem decisão explícita.
- **Sem secrets em logs ou commits** — usar `.env.example` com placeholders; nunca credenciais reais.
- **Atualizar documentação** quando comportamento, DX ou decisão mudar.
- **Declarar risco na spec** — todo item planejado usa R0–R3; R3 começa pelo
  pacote correspondente em `docs/architecture/pacotes-decisao-r3.md`.
- **PRs pequenos e revisáveis** — uma intenção clara por mudança.
- **Apresentar evidência de funcionamento** — saída de `task verify` / testes, screenshots ou traces quando fizer sentido.
- **Validar o harness ao tocá-lo** — mudanças em `.cursor`, `AGENTS.md` ou nos
  contratos de agentes executam `task agent:check`.

## Coordenação

Declarar no PR/commit quando aplicável:

```text
Wave: WX-Y
Trilha: A|B|C|…
Deps satisfeitas: …
```

Parar para humano somente em `R4 — Externo` de `docs/agents/autonomia.md`
(licença incompatível, gasto/contrato, secret/produção real, certificação ou
operação destrutiva externa). Dificuldade técnica não reabre D-*; seguir o
protocolo autônomo de falha e continuar outra trilha independente quando possível.

## Backend

- Trabalhar dentro das fronteiras de módulo; composition root só em `apps/api` e `apps/worker`.
- Setar `TenantContext` / `app.tenant_id` em toda unit of work.
- Mutações de mensagem: idempotência + `seq` + outbox.
- Não publicar SignalR direto sem caminho de outbox para eventos duráveis.
- Não chamar provedores de IA de forma síncrona no hot path de `SendMessage`.
- Testes de integração com Testcontainers quando tocar persistência.
- Checklist: contratos, migration/RLS, testes de messaging, arch tests verdes.

## Frontend

- Angular 22 standalone + Signals + CDK; tokens do `docs/architecture/design-system.md`.
- Assets de marca (logo, fundos, favicon/PWA, sons) em `apps/web/public/` — inventário em `docs/architecture/design-system.md` § Assets de marca; reutilizar, não inventar.
- OIDC PKCE; ordenar mensagens por `seq`; Idempotency-Key estável por envio.
- Light/dark via `data-theme`; motion sutil (2–3), sem poluição visual.
- Não clonar visual Slack/Discord/WhatsApp; sem cards desnecessários no shell.
- Tratar reconnect SignalR, empty/error states e a11y básica.

## Infra

- Compose reproduzível com healthchecks e volumes nomeados.
- Scripts idempotentes em `infra/scripts`; usar `task setup` / `task dev`.
- `.env.example` completo; sem secrets reais.
- Realm Keycloak versionado; profiles opcionais (`tools`, `observability`, `apps`).
- Sem Helm/K8s como requisito da fase 1; não expor Postgres/Redis/MinIO publicamente em referências de prod.

## QA

- Cobrir `docs/product/criterios-aceite-fatia-vertical.md`.
- Priorizar testes de segurança cross-tenant e E2E de duas sessões (`tests/e2e`).
- Não skippar flaky sem issue; fatia vertical sem isolamento multi-tenant não passa.
- Reportar evidências (logs, trace ids, artefatos CI).

## Security

- Revisar PRs com checklist de `docs/security/multi-tenant.md`.
- Manter `docs/security/modelo-ameacas.md` quando a superfície mudar.
- Validar headers, rate-limit, uploads, hub authZ.
- Negar features que enviem PII a IA sem flag + docs.
- Não aprovar bypass temporário de RLS em main.

## Review

Olhar primeiro:

1. Viola ADR-001 / ADR-009 / ADR-010?
2. Há caminho cross-tenant?
3. Outbox / idempotency corretos?
4. UI foge do design system?
5. Scope creep de infra?
6. Docs/glossário desatualizados?

Comentários úteis apontam arquivo + regra, sugerem teste que falharia e diferenciam blocker vs nit.

## Definition of Done

- Código compila; testes da trilha passam
- Docs atualizadas se comportamento/ADR mudou
- Sem secrets
- Escopo limitado à tarefa
- Evidência de verificação anexada ou citada no PR

## Cursor Cloud specific instructions

Contexto para agentes rodando em VMs do Cursor Cloud. O update script já
instala/atualiza dependências (restore .NET + npm). Aqui ficam apenas caveats
não óbvios de execução. Comandos padrão estão no `README.md` e `Taskfile.yml`.

- **Docker não vem instalado** e não há systemd. `bash infra/scripts/agent-setup.sh`
  (o install script do ambiente) instala `docker.io`, `docker-compose-v2` e
  `fuse-overlayfs`, escreve `/etc/docker/daemon.json` com
  `storage-driver: fuse-overlayfs` + `containerd-snapshotter: false` (necessário
  no Docker 29), sobe o `dockerd` e libera `/var/run` + o socket. É idempotente:
  ~10 s na primeira vez, ~0 s depois. Se `docker info` falhar no meio de uma
  sessão, rode o script de novo em vez de repetir os passos à mão. Log:
  `/var/log/vibechat/dockerd.log`.
- **Runtime sempre em Docker** — data plane e apps via Compose (`compose.yaml`);
  não subir API/Web/Worker no host. Preferir `task apps` ou
`docker compose -f compose.yaml -f compose.dev.yaml --profile apps up -d --build`. Keycloak leva
  ~40s para ficar healthy.
- **`task dev` NÃO é o caminho neste ambiente** (e o interpretador do go-task /
  gosh não suporta `trap`/`kill 0` da recipe). Use Compose profile `apps`.
- **Sem toolchain no host do desenvolvedor** — agentes no PC não usam nvm/fnm nem
  `npm`/`ng`/`dotnet` locais para build/teste/serve. Preferir `task …` e
  containers (ver `.cursor/rules/docker-runtime.mdc`). Em Cloud/CI, `npm ci` /
  `ng build` / E2E no host só quando o update script ou o job já provisionou Node
  compatível; `infra/scripts/ci-e2e.sh` resolve via `ensure_web_node`
  (`WEB_NODE_MIN=22.22.3`). Prefira `task ux:stack` / `task test:e2e:ci` a
  improvisar serve no host.
- **Seed automático**: com `Seed:Enabled=true` (Development, já em
  `appsettings.Development.json`) a API aplica migrations e cria o tenant demo +
  `#geral` + alice/bob no startup. `task seed` só é necessário para re-seedar.
- **Login rápido sem Keycloak**: na tela de login use os botões DevAuth
  (Alice/Bob/Demo) ou o header `X-Dev-User: alice|bob|demo`. Valor desconhecido
  sem `X-Dev-Email` retorna **401** (B-177; sem fallback para demo). Convite
  dinâmico: `X-Dev-User` + `X-Dev-Email`. É o caminho mais simples para
  exercitar envio de mensagem ponta a ponta (persiste em `messaging.messages`).
- **Redis**: no Compose, a app usa o serviço `redis` da rede; falha de Redis é
  não-fatal (degrada presença/typing).
- **CRLF / `.env`**: scripts shell de infra e `DATABASE_URL` precisam de LF e de
  aspas, respectivamente (corrigido nesta branch). Se o Keycloak entrar em
  crash-loop com `role "keycloak" does not exist`, ou o migrate falhar com
  `role "ubuntu" does not exist`, é regressão de CRLF/aspas — ver os `fix(infra)`.
