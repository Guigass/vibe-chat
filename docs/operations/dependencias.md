# Atualização de dependências — VibeChat (B-076)

Mecanismo único OSS: **GitHub Dependabot** (`.github/dependabot.yml`).
Cobre NuGet, npm (`apps/web`, `tests/e2e`), GitHub Actions, Docker e
Docker Compose. Sem auto-merge irrestrito e sem ferramenta proprietária.

## O que o Dependabot faz

- Abre PRs semanais em dias escalonados, sempre às 06:00
  (`America/Sao_Paulo`): NuGet na segunda, npm web na terça, npm E2E na
  quarta, GitHub Actions na quinta, Docker na sexta e Docker Compose no
  sábado.
- Mantém no máximo **1 PR aberto por ecossistema**. O teto agregado cai de 22
  para 6 e, pelo escalonamento, o fluxo normal passa a ser de um ecossistema
  por dia. Isso evita rajadas de automações de QA/segurança sem desativar as
  atualizações semanais.
- Agrupa updates **minor/patch** compatíveis (NuGet/npm/Actions) para reduzir
  ruído; **majors** ficam em PRs separados para revisão humana.
- Todo PR do Dependabot passa pela CI existente (`CI` em
  `.github/workflows/ci.yml`: lint/build, unit, arch, security, integration,
  web, E2E, secret-scan e audit informativo). Falha de teste **impede** merge.

O agendamento vale para updates de versão. Alertas críticos de segurança
continuam seguindo a política de prioridade abaixo e não devem ser atrasados
artificialmente pelo escalonamento semanal.

## Exceção documentada — pins Docker

Os Dockerfiles usam `ARG`/`FROM ${…}` e o Compose usa
`${VAR:-pin}`. O parser do Dependabot pode não reescrever esses defaults.
Fonte da verdade dos pins: defaults em `compose.yaml` e espelho em
`.env.example`. Quando o bot não abrir PR de imagem:

1. revisar pins na rotina semanal (`operacao.md`);
2. abrir PR manual `chore(deps): bump <image>`;
3. preservar tags explícitas (nunca `latest` em artefato estável).

Pin atual do profile `observability`: `prom/prometheus:v3.13.2` (Compose default +
`.env.example`). Em Prometheus 3.x, scrapes sem `Content-Type` válido falham
fechado — validar otel/apps ao subir o profile.

## Labels do Dependabot

O `dependabot.yml` não declara labels GitHub: o repositório ainda não tem
`dependencies` / `nuget` / `npm` / `github-actions` / `docker`, e labels
ausentes geram comentário de erro do bot em cada PR. Quando o maintainer
criar esses labels no GitHub, pode reintroduzi-los no YAML.

## Política de triagem

| Tipo | Ação | Owner |
|------|------|--------|
| Patch / minor com CI verde | Revisar changelog curto; merge por squash | Maintainer / QA |
| Major | Sem auto-merge; revisar breaking changes, Angular/Node floor (`>=22.22.3`) e ADRs | Maintainer |
| Vulnerabilidade **crítica** (CVE / advisory) | Prioridade sobre roadmap não-safety; hotfix em ≤ 2 dias úteis ou mitigações documentadas | Security + Build |
| Dependência abandonada / sem release > 12 meses | Avaliar substituto OSS; se risco alto, issue + plano; não pin silencioso eterno | Maintainer |
| Update quebra CI | Não mergear; comentar no PR; abrir `BLOCKED-TECH` só após 3 falhas da mesma abordagem | Build / Watchdog |

SLA interno (best effort, sem SLA comercial — D-08):

- PRs Dependabot sem conflito: triagem em até **7 dias**.
- Crítico de segurança: início em **1 dia útil**; mitigação ou bump em **2 dias úteis**.
- Majors Angular/Node/.NET: janela dedicada; nunca forçar major no hot path.

## Rollback

1. Reverter o squash do PR de dependência (`git revert` do merge em `main`) ou
   abrir `HOTFIX-*` com pin da versão anterior.
2. Se o bump veio só de lockfile (`package-lock.json` / assets NuGet), restaurar
   o lock da revisão anterior e re-rodar `npm ci` / `dotnet restore`.
3. Imagens Compose: voltar o pin em `compose.yaml` / `.env` e redeploy
   (`runbooks/upgrade.md`).
4. Nunca gravar tokens do Dependabot/GitHub em arquivo versionado (D-04).

## Verificação local da config

```bash
python3 - <<'PY'
from pathlib import Path
import yaml
cfg = yaml.safe_load(Path(".github/dependabot.yml").read_text())
assert cfg["version"] == 2
ecosystems = {u["package-ecosystem"] for u in cfg["updates"]}
required = {"nuget", "npm", "docker", "docker-compose", "github-actions"}
assert required <= ecosystems, ecosystems
print("OK", sorted(ecosystems))
PY
```

A CI executa a mesma asserção no job `build-test`.

## Relação com outros controles

- Audit informativo na CI (`dependency-audit`) continua; não substitui PRs de bump.
- SBOM-lite (`artifacts/sbom/`) permanece inventário, não atualizador.
- Threat model: controle “Dependabot/Renovate ou equivalente” em
  `docs/security/modelo-ameacas.md`.
