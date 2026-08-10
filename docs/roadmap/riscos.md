# Riscos Conhecidos — VibeChat

## Matriz

| ID | Risco | Prob. | Impacto | Mitigação | Owner sugerido |
|----|-------|-------|---------|-----------|----------------|
| R-01 | Vazamento cross-tenant | M | Crítico | RLS + testes security + reviews; hardening contínuo no Registro de GAPs (`roadmap.md`) | Security / Backend |
| R-02 | Scope creep (K8s, Kafka, ES) | A | Alto | ADRs 015–017; roadmap waves; ADR autônomo só após gatilho medido | Architecture agent |
| R-03 | Dual-write mensagem/evento | M | Alto | Outbox obrigatório (ADR-010) | Backend |
| R-04 | Misconfig Keycloak em prod | M | Alto | Realm as code; checklist ops | Infra |
| R-05 | Secrets commitados | M | Crítico | .gitignore; scanning; env only | Todos |
| R-06 | SignalR scale sem backplane | M | Médio | Redis backplane doc + config | Backend / Infra |
| R-07 | Outbox lag sob carga | M | Médio | Métricas + replicas worker | Ops |
| R-08 | Dependência de AI vazar PII | B | Alto | Flag off; redaction; aceite admin | Security / Produto |
| R-09 | Licença/marca indefinidas | A | Alto | **Fechado** — D-01 Apache-2.0 e D-02 “VibeChat” decididos em 2026-07-24 | Legal / Founder |
| R-10 | Retenção legal indefinida | A | Alto | **Mitigado** — D-03 + ADR-018; retenção configurável por tenant e purge com kill switch (B-047), off por default | Legal |
| R-11 | Agentes divergindo do glossário | A | Médio | orientacoes.md + glossário canônico | Agents lead |
| R-12 | Flaky E2E OIDC | M | Médio | **Mitigado (W7-1)** — job E2E na CI roda em modo `devauth`, tirando o Keycloak do caminho crítico; modo `oidc` continua manual | QA |
| R-13 | Postgres como search esgota | B | Médio | ADR-016 gatilhos | Backend |
| R-14 | MinIO disco cheio | M | Alto | Alertas disco; retenção anexos | Ops |
| R-15 | Design genérico (clone Slack) | M | Médio | design-system.md enforce em review | Frontend |
| R-16 | Realtime degradado (só typing) | B | Alto | **Mitigado (B-070 Done)** — gap-fill + E2E dois usuários; monitorar métricas SignalR/outbox | Backend / Frontend |
| R-17 | Secrets/webhooks expostos a membros | M | Crítico | **Mitigado (B-069/B-048/ADR-020)** — settings só `workspace.admin`; envelopes AES-GCM; rotate dedicado; máscara sem plaintext; nunca logar tokens | Security |
| R-18 | Auditoria de conversa (break-glass de leitura) | M | Alto | B-067 authZ `admin.dashboard` + escopo tenant; testes security; ver `modelo-ameacas.md` | Security |
| R-19 | Dependência UI comercial (PrimeNG/PrimeUI) | A | Alto | **Mitigado (B-104 / #82)** — `primeng` removido; spartan/ui (MIT) + CDK + tokens; UX-002 Done; polish do console admin em **B-106** | Frontend |
| R-20 | Drift entre `.env.example`, Compose e appsettings | A | Alto | B-105 + catálogo canônico; smoke do profile `apps`; declarar aliases sem efeito | Infra / Docs |
| R-21 | Roadmap marcar `Done` sem evidência ou ficar divergente do backlog/spec | M | Alto | `estado-atual.md`, programa DOC-*, revisão por wave e rastreabilidade ID↔spec | Produto / QA / Docs |
| R-22 | Portfólio ambicioso diluir o chat principal | A | Alto | Waves 7–10 primeiro; W11–W17 em ordem; uma aposta arquitetural por vez | Product / Build agent |
| R-23 | Conhecimento/tarefas divergirem da mensagem de origem | M | Alto | Referência canônica, propagação de edit/delete/retention e audit | Produto / Backend |
| R-24 | Automação duplicar ações ou entrar em loop | M | Alto | Idempotência, depth limit, rate-limit, circuit breaker e identidade de execução | Backend / Security |
| R-25 | Conector/bridge permitir SSRF ou exfiltrar secret | M | Crítico | Egress policy, HMAC/OAuth scoped, rotação, URL validation e audit | Security / Integrations |
| R-26 | Legal hold conflitar com retenção e direito de exclusão | M | Crítico | D-23; precedência formal e trilha de custódia; validação legal externa só antes de alegação/produção regulada | Security / External legal |
| R-27 | Embeddings/RAG preservarem conteúdo já revogado | M | Crítico | ACL na consulta, delete propagation, reindex, provider opt-in e citations | AI / Security |
| R-28 | Federação reduzir soberania e revogação local | M | Crítico | D-21; consentimento, trust domains e política de cópia/retenção | Founder / Security / Legal |
| R-29 | Live media degradar chat e elevar custo operacional | M | Alto | D-19; plano SFU/TURN separado, quotas e SLOs | Platform / Realtime |
| R-30 | Desktop/mobile criarem clientes e contratos divergentes | M | Alto | D-20; APIs únicas, contract tests e matriz de suporte | Produto / Frontend |
| R-31 | Registry/plugin introduzir supply-chain compromise | M | Crítico | Assinatura, provenance, revisão, revogação e capabilities mínimas | Security / Ecosystem |
| R-32 | Agente entrar em loop de falhas ou reabrir decisões fechadas | M | Alto | Contrato de autonomia, protocolo de três falhas, `BLOCKED-TECH-*` e seleção de outra trilha | Automation / QA |
| R-33 | PR autônomo mergear sem evidência proporcional ao risco | M | Crítico | Classe R0–R3 no PR; QA independente; checks obrigatórios; `VibeChat Security Review` conclusivo | QA / Repository admin |
| R-34 | RLS existir no catálogo mas ser ignorada pela role owner/superuser da aplicação | B | Crítico | **Mitigado (`SEC-RLS-RUNTIME` Done #72/#73)** — roles `vibechat_migrator`/`app`/`backup`, FORCE+WITH CHECK, `RlsSession` SET LOCAL, testes com credencial runtime | Security / Infra / Backend |

## Riscos técnicos detalhados

### R-01 Isolamento

Qualquer endpoint novo é suspeito até prova de teste negativo. Preferir 404 a 403 quando enumeração for risco.
RLS só conta como defesa se a role efetiva da API/Worker estiver submetida às
policies; presença de `ENABLE ROW LEVEL SECURITY` no catálogo não basta.
Enforcement runtime fechado em `SEC-RLS-RUNTIME` (#72/#73); regressões novas
continuam exigindo teste cross-tenant na trilha tocada.

### R-02 Scope creep

Agentes não devem “já deixar pronto Kafka/K8s” sem gatilho. PRs que adicionem esses componentes na fase 1 devem ser rejeitados.

### R-09 / R-10 Legais

Sem licença clara, adoção OSS trava. Sem política de retenção, features de delete/export ficam ambíguas. Ambos endereçados em 2026-07-24: Apache-2.0 (D-01) e retenção soft-delete + purge configurável (D-03 / ADR-018 / B-047).

## Indicadores de risco emergente

- Outbox dead-letter crescendo
- Aumento de 403/401 anômalos
- Uso de memória Redis sem bound
- Tempo de PR arquitetural sem ADR
- Item `Planned` sem spec, dependências ou classe de risco
- Automação com retry/loop crescente
- Índice semântico com atraso de delete/revogação
- Diferença de comportamento entre web e novos clients
- Custos de mídia/storage crescendo acima de usuários ativos

## Revisões

Revisar esta lista a cada wave do roadmap ou incidente P0/P1. Última revisão:
2026-08-05, `SEC-RLS-RUNTIME` mitigado (#72/#73).
