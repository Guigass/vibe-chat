# B-187 — Template `.env` enxuto (setup vs catálogo)

> Wave W7-14 · Trilha A/G · Deps: B-105, B-069 · Decisões: D-04, D-05, D-10 · Risco R1

## Problema

O `.env.example` virou catálogo completo (~210 linhas): pins de imagem, portas,
profiles opcionais, aliases `SMTP_*` e connection strings duplicadas. O operador
copia tudo isso para subir o lab. B-105 fechou injeção e matriz env vs admin;
**não** prometeu esvaziar o `.env` no banco. Política/integração já pode ir ao
DB (ADR-020), mas `RuntimeSettings:DatabaseOverridesEnabled` continua opt-in
escondido e as docs ainda dizem “SMTP/AI só no env”.

## Escopo

- Separar dois artefatos: **template de setup** (`.env.example`) e **catálogo**
  (`docs/operations/configuracao-env.md`). O template só lista o que o humano
  preenche no dia a dia (senhas, URLs públicas, flags de lab/staging).
- Defaults de imagem, porta e profile opcional ficam no `compose.yaml`
  (`${VAR:-default}`); omitir no template não quebra `task apps`.
- Remover do template aliases mortos ou duplicados (`SMTP_*` vs `EMAIL__*`,
  `DATABASE_*_URL` quando o Compose já interpola `POSTGRES_*`).
- Documentar o caminho self-host pretendido: políticas e credenciais de
  integração (SMTP, OpenRouter, webhook, retenção por tenant, Files/RateLimit)
  em `/admin/settings` + DB quando o keyring estiver provisionado e
  `RuntimeSettings:DatabaseOverridesEnabled=true`. Kill switches globais
  (`Ai:Enabled`, `MessageRetention:Enabled`, `Push:Enabled`) permanecem no env.
- Ajustar `ComposeConfigCatalogTests`: completude do catálogo = Compose tem
  default + docs; o template curto não precisa repetir cada substituição.
- Alinhar `operacao.md`, guia do administrador e glossário com ADR-020
  (integração no admin; infra só no env).

## Fora de escopo

- Mover Postgres, Redis, Keycloak, MinIO, OIDC, CORS/proxy/TLS, seed, OTel,
  BaseUrl OpenRouter, keyring AES-GCM ou VAPID para o banco (ADR-020 / D-04).
- Tornar toda configuração dinâmica / registry `settings[key]=value`.
- Ligar `DatabaseOverridesEnabled=true` por default (continua fail-safe off
  até o operador gerar o keyring).
- Secret manager de cloud.
- Redesign da UI `/admin/settings`.

## Contratos

Sem endpoint novo. Precedência inalterada (ADR-020): kill switch/teto env →
override DB → fallback env → default seguro. `.env.example` deixa de ser o
inventário 1:1 das substituições do Compose; o inventário canônico permanece
`docs/operations/configuracao-env.md`.

## UX

Não muda o chat. `/admin/settings` já indica fonte efetiva (`env` /
`workspace` / `default`); B-187 só documenta quando o operador deve usar a UI
em vez do `.env`.

## Multi-tenant e authZ

Inalterado: `workspace.admin` em settings; secrets mascarados; RLS nas tabelas
de settings. Template enxuto não reduz authZ nem expõe secret.

## Aceite

- [ ] `.env.example` cabe no fluxo `cp` → ajustar `CHANGE_ME` → `task apps`
      sem pins de imagem, sem aliases `SMTP_*` e sem vars só de profile
      opcional (observability/proxy/k6).
- [ ] `docker compose --env-file .env.example config --quiet` continua válido
      (defaults do Compose cobrem o omitido).
- [ ] Catálogo em `configuracao-env.md` lista o inventário completo e a matriz
      env vs admin, incluindo o caminho `DatabaseOverridesEnabled`.
- [ ] Docs de operação/admin/glossário não contradizem ADR-020 (SMTP/AI key
      podem ir ao DB criptografadas; infra não).
- [ ] Testes de catálogo verdes sem exigir 1:1 template ↔ substituições.

## Testes

- `ComposeConfigCatalogTests`: bindings EMAIL/AI/retenção/push no Compose;
  parse do projeto com `.env.example` curto.
- Não exigir que cada `${VAR}` do Compose apareça no template quando há
  `${VAR:-default}`.
- Security: settings continuam mascarados; nenhum secret real no template.

## Riscos

- Operador some com uma var “sumida” do template: mitigar com catálogo +
  default no Compose, não com silent fail.
- Ligar overrides sem keyring: default da flag permanece `false`; runbook
  exige gerar `RuntimeSettings:Encryption:Keys` antes.
- Drift template/Compose: testes cobrem parse e bindings críticos, não o
  dump de pins.
