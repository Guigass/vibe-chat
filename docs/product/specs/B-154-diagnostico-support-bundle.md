# B-154 — Diagnóstico administrativo e support bundle

> Wave 11 · Trilha A/B/D/E/G · Deps: B-105, B-106, B-115 · Risco R2
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Healthcheck binário não explica configuração incompleta, e pedir logs brutos ao
operador expõe tokens, caminhos e conteúdo. Adoção e suporte precisam de
diagnóstico seguro e acionável.

## Escopo

- Página de preflight administrativo.
- Checks de API, Worker, banco, Redis, storage, OIDC, e-mail, Web Push, proxy,
  outbox e migrations.
- Testes opt-in de e-mail, push e storage com artefatos sintéticos.
- Reason codes estáveis, severidade, evidência e link de runbook.
- Repair actions apenas para projeções reconstruíveis e operações allowlisted.
- Dry-run e confirmação para reindex/reconcile.
- Support bundle versionado, sanitizado e com TTL.
- Inventário de versões, profiles, feature flags sem secrets, health, métricas
  agregadas, migrations e correlation IDs selecionados.
- Validador local/CLI futuro usando o mesmo contrato.

## Fora de escopo

- Incluir body de mensagem, arquivo de usuário, token, cookie ou secret.
- Dump automático de banco.
- Corrigir configuração de produção por inferência.
- Reiniciar serviço, restaurar backup ou alterar DNS/IdP sem operador.
- Shell/comando arbitrário pela UI.

## Contratos

`DiagnosticCheck`:

- stable code e versão;
- component;
- status `Pass | Warn | Fail | Skipped`;
- severity;
- summary sanitizado;
- evidence fields allowlisted;
- remediation/runbook;
- observed at e correlation.

Bundle `vibechat.support-bundle.v1` possui manifest, checksums, gerador/versão,
janela temporal, redaction report e TTL. Geração assíncrona, tenant-scoped,
auditada e idempotente.

Repair action declara:

- recurso reconstruível;
- dry-run;
- impacto estimado;
- preconditions;
- progress/checkpoint;
- cancel/rollback quando possível;
- evidence final.

## UX

Dashboard mostra:

- “pronto”, “degradado” ou “ação necessária”;
- diferença entre serviço obrigatório e feature off;
- último sucesso e latência;
- ação segura ou runbook;
- progresso e expiração do bundle.

“Desligado por configuração” não aparece como incidente. Mensagens evitam
detalhes exploráveis para atores sem permissão.

## Multi-tenant e authZ

Health de infraestrutura é de operador; admin de workspace vê somente checks de
seu escopo e valores mascarados. Gerar bundle exige capability dedicada. Bundle
não agrega eventos de outro tenant, mesmo para admin.

Download usa URL curta, uso limitado, audit e expiração. Redaction é allowlist,
não tentativa posterior de apagar secrets de um dump amplo.

## Aceite

- [ ] Instalação incompleta aponta check e runbook corretos.
- [ ] Feature off é `Skipped/Pass`, não falso erro.
- [ ] Testes de e-mail/storage usam dados sintéticos e limpam artefatos.
- [ ] Bundle passa secret/PII scan.
- [ ] Admin de A não vê diagnóstico/bundle de B.
- [ ] Reindex dry-run não escreve.
- [ ] Repair não aceita alvo arbitrário.
- [ ] Bundle expira e download posterior falha.

## Testes

- Fake checks por estado/timeout.
- Sanitização com canários de token, connection string, e-mail e body.
- AuthZ operador/admin/auditor/member.
- Cross-tenant.
- Bundle determinístico/checksum/TTL.
- Repair dry-run, checkpoint, cancel e falha.
- E2E preflight → teste sintético → bundle.

## Riscos

Bundle virar canal de exfiltração ou repair causar dano. Usar schema allowlisted,
capability separada, geração mínima, scan bloqueante, TTL curto, dry-run e
catálogo fechado de ações.
