# Critérios de Aceite — Fatia Vertical (Fase 1)

## Objetivo da fatia

Demonstrar o caminho feliz completo:

**Login (OIDC) → Workspace → Channel → Enviar e receber mensagem em tempo real**, com isolamento de tenant, persistência correta e operação via Docker Compose.

## Escopo incluído

1. Usuário autentica via Keycloak (OIDC)
2. Usuário vê ao menos um workspace e entra nele
3. Usuário abre um channel
4. Usuário envia mensagem de texto
5. Outro cliente (mesma ou outra sessão) recebe a mensagem em tempo real via SignalR
6. Mensagem persiste no PostgreSQL com `tenant_id`, `conversation_id` e `seq`
7. Retry com mesma idempotency key não duplica mensagem
8. Stack sobe com `docker compose` (API, worker, web, Postgres, Redis, Keycloak, MinIO, observabilidade básica)

## Critérios de aceite funcionais

### A1 — Autenticação

- [x] Fluxo OIDC Authorization Code + PKCE funciona no web (Angular)
- [x] Token inválido/expirado resulta em 401 na API e reconexão SignalR falha de forma controlada
- [x] Claims de tenant/usuário estão disponíveis no backend após validação do JWT

### A2 — Workspace e Channel

- [x] Após login, usuário enxerga apenas workspaces do seu tenant
- [x] Usuário consegue listar channels do workspace onde tem membership
- [x] Usuário sem membership não lista nem entra no channel (403/404 conforme política)

### A3 — Envio de mensagem

- [x] `POST` (ou RPC equivalente) cria mensagem com conteúdo, autor, timestamps
- [x] Mensagem recebe `seq` monotônico por conversation
- [x] Mesma `Idempotency-Key` retorna a mesma mensagem (sem duplicar)
- [x] Evento de outbox é gravado na **mesma transação** da mensagem

### A4 — Tempo real

- [x] Cliente assinante do channel recebe evento `message.created` via SignalR
- [x] Ordem observada respeita `seq` (ou cliente detecta gap e reconcilia)
- [x] Com duas instâncias de API (opcional no aceite mínimo: uma instância), backplane Redis está configurável

### A5 — Persistência e multi-tenant

- [x] Mensagem aparece em leitura posterior (reload / history API)
- [x] Query com `tenant_id` de outro tenant não retorna dados (RLS + testes)
- [x] Tentativa de forjar `tenant_id` no body é ignorada; tenant vem do token/contexto

### A6 — Operação local

- [x] `docker compose up` sobe dependências essenciais
- [x] Seed mínimo: realm Keycloak, tenant, workspace, channel, 2 usuários de teste
- [x] Documentação em `docs/operations/desenvolvimento.md` permite reproduzir em máquina limpa

## Critérios não-funcionais mínimos

| ID | Critério |
|----|----------|
| NF1 | Latência de eco local (envio → recebimento no outro cliente) tipicamente &lt; 300 ms |
| NF2 | Logs estruturados com `correlation_id` / `tenant_id` (sem PII desnecessária) |
| NF3 | Health checks: `/health` e `/ready` na API |
| NF4 | Migrações de schema aplicáveis de forma idempotente |
| NF5 | Testes automatizados cobrindo: idempotência, sequência, isolamento RLS |

## Fora do aceite desta fatia

- Anexos, reações, editar/apagar mensagem (podem existir stubs)
- Busca full-text avançada
- Features de IA
- Mobile nativo
- Alta disponibilidade multi-região
- Kubernetes

## Evidências esperadas

1. Captura ou script de demo (dois browsers / dois usuários)
2. Resultado de testes de integração + teste de segurança multi-tenant
3. Trace de um envio no Tempo/Grafana (opcional mas desejável)
4. Checklist deste documento marcado pelo time de QA

## Definição de pronto (DoD)

A fatia está pronta quando **todos os critérios A1–A6** passam em ambiente Compose local, com testes automatizados verdes na CI (ou pipeline local documentado), e sem regressão de isolamento entre tenants.
