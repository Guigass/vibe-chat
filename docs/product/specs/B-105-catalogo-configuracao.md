# B-105 — Catálogo de configuração self-host

> Wave W7-7 · Trilha A/G · Deps: W6-8, W0-2 · Decisões: D-04, D-05, D-06, D-10 · Risco R2

## Problema

`.env.example`, `compose.yaml` e `appsettings*.json` não formam hoje um contrato
único. Há variáveis documentadas que não são injetadas no profile `apps`,
especialmente e-mail e retenção, além de aliases legados e defaults adequados
apenas a desenvolvimento.

## Escopo

- Inventariar todas as substituições do Compose e todas as chaves consumidas
  pela API, Worker e build do web.
- Classificar cada chave por serviço, default, obrigatoriedade e sensibilidade.
- Remover ou marcar aliases legados sem efeito no caminho oficial.
- Garantir que toda chave prometida para `task apps` seja realmente injetada.
- Documentar matriz ambiente global versus configuração por workspace.
- Fornecer checklist de produção e validação pós-bootstrap.

## Fora de escopo

- Adotar secret manager específico de cloud.
- Tornar toda configuração dinâmica.
- Levar convites, papéis ou export para `.env`.
- Definir credenciais reais.
- Integração no `/admin` (SMTP/IA no DB) e keyring lab — **B-187**.

## Contratos

O catálogo canônico é `docs/operations/configuracao-env.md`; `.env.example` é o
template executável correspondente. Nomes ASP.NET usam `Section__Key`.

Gaps que a implementação deve resolver:

- `EMAIL__*`, `SMTP_*` e `MessageRetention__*` existem no template, mas não são
  injetados nos containers `api`/`worker` pelo `compose.yaml`;
- `AI__*` aparece duplicado com `OPENROUTER_*`, mas o Compose injeta apenas parte
  das formas;
- aliases de host/URL e variáveis de scripts não são configuração do runtime
  containerizado e precisam ser classificados;
- `Files`, `RateLimit`, `Cors` e metadata HTTPS precisam de decisão explícita de
  exposição ou default fixo.

## UX

Não muda a UI de chat. `/admin/settings` deve indicar a fonte efetiva
(`env`, `workspace`, `default`) sem revelar secrets.

## Multi-tenant e authZ

Ambiente define limites e kill switches globais. Política por workspace continua
protegida por `workspace.admin`; secrets nunca retornam em claro. Uma configuração
de tenant não pode alterar outro tenant.

## Aceite

- [ ] 100% das variáveis do Compose estão no catálogo.
- [ ] 100% das variáveis prometidas no `.env.example` têm consumidor ou rótulo
      explícito de script/legado.
- [ ] E-mail e retenção funcionam no profile `apps` quando habilitados.
- [ ] Produção não nasce com seed ou integração externa ligada por default.
- [ ] Toda senha/chave está classificada como secret.
- [ ] Matriz env versus admin está atualizada.
- [ ] Operação e troubleshooting apontam para o catálogo.

## Testes

- Renderizar `docker compose config` com template seguro.
- Smoke do profile `apps` com defaults.
- Smoke opt-in separado para SMTP, IA e retenção.
- Teste/checagem que compara substituições do Compose com `.env.example`.
- Confirmar que logs e endpoints admin não expõem secrets.

## Riscos

- Configuração “documentada” sem efeito: exigir smoke do container, não só diff.
- Defaults de Development em produção: checklist bloqueante.
- Duplicidade de aliases: declarar precedência e depreciação.
- Secret em saída de diagnóstico: mascarar valores em toda evidência.

